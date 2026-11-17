using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UAParser;
using ClientInfo = NIJMITRA.Application.Abstractions.ClientInfo;

namespace NIJMITRA.Infrastructure.Services;

public sealed class SettingsService(IAppDbContext db, IMemoryCache cache, TimeProvider clock) : ISettingsService
{
    private const string Key = "platform";
    private const string CacheKey = "nm:platform-settings";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out PlatformSettings? cached) && cached != null) return Clone(cached);
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        var settings = row == null ? new PlatformSettings() : JsonSerializer.Deserialize<PlatformSettings>(row.Value, Json) ?? new PlatformSettings();
        cache.Set(CacheKey, settings, TimeSpan.FromSeconds(30));
        return Clone(settings);
    }

    public async Task SaveAsync(PlatformSettings settings, Guid adminId, CancellationToken ct = default)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        var json = JsonSerializer.Serialize(settings, Json);
        if (row == null) db.SystemSettings.Add(new SystemSetting { Key = Key, Value = json, UpdatedAt = clock.GetUtcNow().UtcDateTime, UpdatedBy = adminId });
        else { row.Value = json; row.UpdatedAt = clock.GetUtcNow().UtcDateTime; row.UpdatedBy = adminId; }
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    private static PlatformSettings Clone(PlatformSettings s) =>
        JsonSerializer.Deserialize<PlatformSettings>(JsonSerializer.Serialize(s, Json), Json)!;
}

public sealed class UserAgentParserService : IUserAgentParser
{
    private static readonly Parser Parser = UAParser.Parser.GetDefault();

    public ClientInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return new ClientInfo(null, null, null);
        var c = Parser.Parse(userAgent);
        var browser = c.UA.Family == "Other" ? null : $"{c.UA.Family} {c.UA.Major}".Trim();
        var os = c.OS.Family == "Other" ? null : $"{c.OS.Family} {c.OS.Major}".Trim();
        var device = c.Device.Family is "Other" or null
            ? (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Desktop")
            : c.Device.Family;
        return new ClientInfo(browser, os, device);
    }
}

public sealed class AuditService(IAppDbContext db, ICurrentUser user, IUserAgentParser ua, TimeProvider clock) : IAuditService
{
    public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var info = ua.Parse(user.UserAgent);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = entry.UserId ?? user.UserId, Event = entry.Event, EntityType = entry.EntityType, EntityId = entry.EntityId,
            Details = Truncate(entry.Details), Success = entry.Success, IpAddress = user.IpAddress, UserAgent = Truncate(user.UserAgent),
            Browser = info.Browser, OperatingSystem = info.OperatingSystem, Device = info.Device, SessionId = user.SessionId,
            ImpersonatedBy = user.ImpersonatorId, Timestamp = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task LogLoginAsync(Guid? userId, string? identifier, string evt, bool success, string? reason, Guid? sessionId, CancellationToken ct = default)
    {
        var info = ua.Parse(user.UserAgent);
        db.LoginAuditLogs.Add(new LoginAuditLog
        {
            UserId = userId, MaskedIdentifier = identifier == null ? null : TextUtil.MaskMobile(identifier), Event = evt, Success = success,
            FailureReason = reason, IpAddress = user.IpAddress, UserAgent = Truncate(user.UserAgent), Browser = info.Browser,
            OperatingSystem = info.OperatingSystem, Device = info.Device, SessionId = sessionId ?? user.SessionId, Timestamp = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task LogAdminAsync(Guid adminId, string action, Guid? targetUserId, string? details, CancellationToken ct = default)
    {
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = adminId, Action = action, TargetUserId = targetUserId, Details = Truncate(details),
            IpAddress = user.IpAddress, UserAgent = Truncate(user.UserAgent), Timestamp = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? s) => s is { Length: > 1000 } ? s[..1000] : s;
}

public sealed class HtmlSanitizerService : IHtmlSanitizerService
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https"); s.AllowedSchemes.Add("http"); s.AllowedSchemes.Add("mailto");
        s.AllowedAttributes.Remove("style");
        s.AllowedTags.Remove("img"); // images are stored as private attachments, not remote URLs
        return s;
    }

    public string Sanitize(string html) => Sanitizer.Sanitize(html ?? "");
}

public sealed class ExportService : IExportService
{
    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] ToExcel(TableExport table)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(table.Title));
        for (var c = 0; c < table.Headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = table.Headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EAFE");
        }
        for (var r = 0; r < table.Rows.Count; r++)
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                var v = table.Rows[r][c];
                var cell = ws.Cell(r + 2, c + 1);
                if (v != null && v.Length < 16 && (!v.StartsWith('0') || v == "0" || v.StartsWith("0.")) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    cell.Value = d;
                else cell.Value = Neutralize(v);
            }
        ws.Columns().AdjustToContents(1, Math.Min(table.Rows.Count + 1, 500), 8, 60);
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ToCsv(TableExport table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Headers.Select(Csv.Escape)));
        foreach (var row in table.Rows) sb.AppendLine(string.Join(",", row.Select(v => Csv.Escape(Neutralize(v)))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public byte[] ToPdf(TableExport table)
    {
        var landscape = table.Headers.Count > 5;
        return QuestPDF.Fluent.Document.Create(doc => doc.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(9));
            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("NIJMITRA").FontSize(16).Bold().FontColor("#4F46E5");
                    row.RelativeItem().AlignRight().Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
                col.Item().Text(table.Title).FontSize(13).SemiBold();
                if (!string.IsNullOrEmpty(table.Subtitle)) col.Item().Text(table.Subtitle).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingVertical(6).LineHorizontal(1).LineColor("#C7D2FE");
            });
            page.Content().Table(t =>
            {
                t.ColumnsDefinition(cd => { foreach (var _ in table.Headers) cd.RelativeColumn(); });
                t.Header(h =>
                {
                    foreach (var head in table.Headers)
                        h.Cell().Background("#EEF2FF").Padding(4).Text(head).SemiBold();
                });
                var i = 0;
                foreach (var row in table.Rows)
                {
                    string bg = i++ % 2 == 0 ? "#FFFFFF" : "#F8FAFC";
                    foreach (var v in row) t.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(v ?? "");
                }
            });
            page.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); x.Span(" of "); x.TotalPages(); });
        })).GeneratePdf();
    }

    public IReadOnlyList<Dictionary<string, string?>> ReadTabular(Stream content, string extension)
    {
        var result = new List<Dictionary<string, string?>>();
        extension = extension.ToLowerInvariant();
        if (extension == ".csv")
        {
            using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var rows = Csv.Parse(reader.ReadToEnd());
            if (rows.Count == 0) return result;
            var headers = rows[0].Select(h => h.Trim()).ToList();
            foreach (var r in rows.Skip(1).Take(10000))
            {
                if (r.All(string.IsNullOrWhiteSpace)) continue;
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++) row[headers[i]] = i < r.Count ? r[i] : null;
                result.Add(row);
            }
            return result;
        }
        if (extension is ".xlsx" or ".xls")
        {
            using var wb = new XLWorkbook(content);
            var ws = wb.Worksheets.First();
            var range = ws.RangeUsed();
            if (range == null) return result;
            var headerRow = range.FirstRow();
            var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
            foreach (var r in range.RowsUsed().Skip(1).Take(10000))
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                {
                    var cell = r.Cell(i + 1);
                    row[headers[i]] = cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd") : cell.GetString();
                }
                result.Add(row);
            }
            return result;
        }
        throw new ValidationAppException("file", "Only .csv and .xlsx files can be imported.");
    }

    /// <summary>Prevents CSV/Excel formula injection when users open exports in spreadsheet apps.</summary>
    private static string? Neutralize(string? v) =>
        v is { Length: > 0 } && "=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ? "'" + v : v;

    private static string SafeSheetName(string s)
    {
        var name = new string(s.Where(c => !"[]*?/\\:".Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Length > 31 ? name[..31] : name;
    }
}

/// <summary>Minimal RFC 4180 CSV helpers.</summary>
public static class Csv
{
    public static string Escape(string? v)
    {
        if (v == null) return "";
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
