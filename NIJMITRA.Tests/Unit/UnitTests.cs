using Microsoft.Extensions.Options;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.CustomTables;
using NIJMITRA.Application.Files;
using NIJMITRA.Application.Finance;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Application.People;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Security;
using OtpNet;
using Xunit;

namespace NIJMITRA.Tests.Unit;

public class FormulaTests
{
    [Theory]
    [InlineData("{MRP} * {GST} / 100", 18)]
    [InlineData("({MRP} + 20) * 2", 240)]
    [InlineData("-{MRP} + 150", 50)]
    public void Evaluates(string formula, decimal expected) =>
        Assert.Equal(expected, FormulaEvaluator.Evaluate(formula, n => n == "MRP" ? 100 : n == "GST" ? 18 : null));

    [Fact] public void DivideByZeroReturnsNull() => Assert.Null(FormulaEvaluator.Evaluate("{A} / 0", _ => 5));
    [Fact] public void MissingValueReturnsNull() => Assert.Null(FormulaEvaluator.Evaluate("{A} + 1", _ => null));
    [Fact] public void RejectsCodeInjection() => Assert.False(FormulaEvaluator.IsValid("System.IO.File.Delete(\"x\")", ["A"]));
    [Fact] public void RejectsUnknownColumn() => Assert.False(FormulaEvaluator.IsValid("{Unknown} * 2", ["A"]));
}

public class RecurrenceTests
{
    [Fact]
    public void MonthlyKeepsAnchorDay()
    {
        var r = new RecurringExpense { StartDate = new DateOnly(2026, 1, 31), Frequency = RecurrenceFrequency.Monthly };
        var dates = RecurringScheduler.DueDates(r, new DateOnly(2026, 4, 30)).ToList();
        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30)], dates);
    }

    [Fact]
    public void RespectsEndDateAndCustomInterval()
    {
        var r = new RecurringExpense { StartDate = new DateOnly(2026, 1, 1), Frequency = RecurrenceFrequency.Custom, CustomIntervalDays = 10, EndDate = new DateOnly(2026, 1, 25) };
        Assert.Equal(3, RecurringScheduler.DueDates(r, new DateOnly(2026, 12, 31)).Count());
    }
}

public class FileSignatureTests
{
    [Fact] public void AcceptsRealPdf() => Assert.True(FileService.SignatureMatches(".pdf", Samples.Pdf));
    [Fact] public void RejectsExeRenamedToPdf() => Assert.False(FileService.SignatureMatches(".pdf", [0x4D, 0x5A, 0x90, 0x00]));
    [Fact] public void RejectsBinaryAsText() => Assert.False(FileService.SignatureMatches(".txt", [0x41, 0x00, 0x42]));
    [Fact] public void RejectsUnknownExtension() => Assert.False(FileService.SignatureMatches(".exe", [0x4D, 0x5A]));
    [Fact] public void SanitizesNames() => Assert.Equal("evil.pdf", FileService.SanitizeName("../../etc/<evil>.pdf"));
}

public class EncryptionTests
{
    private static FieldEncryptor Create() => new(Options.Create(new SecurityOptions { DataEncryptionKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()) }));

    [Fact]
    public void RoundTripsAndUsesRandomNonce()
    {
        var e = Create();
        var a = e.Encrypt("secret diary text");
        var b = e.Encrypt("secret diary text");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("secret", a);
        Assert.Equal("secret diary text", e.Decrypt(a));
    }

    [Fact]
    public void DetectsTampering()
    {
        var e = Create();
        var bytes = Convert.FromBase64String(e.Encrypt("hello")[3..]);
        bytes[^1] ^= 0xFF;
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => e.Decrypt("v1:" + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void PasswordHashIsNotPlaintextAndVerifies()
    {
        var h = new PasswordHasherService();
        var hash = h.Hash("Str0ng@Pass");
        Assert.DoesNotContain("Str0ng", hash);
        Assert.True(h.Verify(hash, "Str0ng@Pass", out _));
        Assert.False(h.Verify(hash, "wrong", out _));
    }
}

public class TotpTests
{
    [Fact]
    public void AcceptsCurrentCodeRejectsReplayAndWrongCode()
    {
        var svc = new TotpService();
        var secret = svc.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        var step = svc.Verify(secret, code, 0);
        Assert.NotNull(step);
        Assert.Null(svc.Verify(secret, code, step!.Value)); // replay blocked
        Assert.Null(svc.Verify(secret, code == "000000" ? "111111" : "000000", 0));
    }
}

public class TemplateTests
{
    [Fact]
    public void EncodesHtmlVariables()
    {
        var html = TemplateRenderer.Render("<p>Hello {{UserName}}</p>", new Dictionary<string, string> { ["UserName"] = "<script>x</script>" }, true);
        Assert.DoesNotContain("<script>", html);
    }
}

public class CustomValueTests
{
    private static CustomTableColumn Col(CustomFieldType t, string? options = null) => new() { Name = "Field", FieldType = t, OptionsJson = options };

    [Theory]
    [InlineData(CustomFieldType.Currency, "₹1,234.567", "1234.57")]
    [InlineData(CustomFieldType.Date, "25/12/2026", "2026-12-25")]
    [InlineData(CustomFieldType.Boolean, "Yes", "true")]
    [InlineData(CustomFieldType.Url, "nijmitra.com", "https://nijmitra.com")]
    public void Normalizes(CustomFieldType t, string raw, string expected) => Assert.Equal(expected, CustomTableService.Normalize(Col(t), raw));

    [Fact] public void RejectsBadEmail() => Assert.Throws<ValidationAppException>(() => CustomTableService.Normalize(Col(CustomFieldType.Email), "not-an-email"));
    [Fact] public void RejectsUnknownOption() => Assert.Throws<ValidationAppException>(() => CustomTableService.Normalize(Col(CustomFieldType.Dropdown, "[\"A\",\"B\"]"), "C"));
    [Fact] public void RejectsFractionInNumber() => Assert.Throws<ValidationAppException>(() => CustomTableService.Normalize(Col(CustomFieldType.Number), "1.5"));
}

public class CreditMathTests
{
    [Fact]
    public void PartialPaymentsUpdateStatus()
    {
        var t = new CreditTransaction { Amount = 1000 };
        t.Payments.Add(new CreditPayment { Amount = 300 });
        UdharService.Recalculate(t);
        Assert.Equal(CreditStatus.PartiallySettled, t.Status);
        Assert.Equal(700, t.Outstanding);
        t.Payments.Add(new CreditPayment { Amount = 700 });
        UdharService.Recalculate(t);
        Assert.Equal(CreditStatus.Settled, t.Status);
        Assert.Equal(0, t.Outstanding);
    }
}

public class TextUtilTests
{
    [Fact] public void EscapesLikeWildcards() => Assert.Equal("%50\\%\\_off%", TextUtil.LikePattern("50%_off"));
    [Fact] public void NormalizesIndianMobile() => Assert.Equal("+919876543210", TextUtil.NormalizeMobile("98765 43210"));
    [Fact] public void MasksMobile() => Assert.Equal("*********3210", TextUtil.MaskMobile("+919876543210"));
}
