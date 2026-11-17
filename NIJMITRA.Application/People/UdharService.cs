using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.People;

public record PaymentDto(Guid Id, decimal Amount, DateOnly PaymentDate, PaymentMethod Method, string? Note, DateTime CreatedAt);
public record CreditDto(Guid Id, CreditDirection Direction, Guid? ContactId, string PersonName, string? ContactNumber, decimal Amount, decimal SettledAmount, decimal Outstanding,
    DateOnly TransactionDate, DateOnly? ExpectedReturnDate, DateTime? NextReminderAt, string? Description, CreditStatus Status, bool IsOverdue,
    IReadOnlyList<PaymentDto> Payments, IReadOnlyList<FileDto> Attachments, DateTime CreatedAt);
public record CreditRequest(CreditDirection Direction, Guid? ContactId, [StringLength(150)] string? PersonName, [StringLength(20)] string? ContactNumber,
    [Range(0.01, 999999999)] decimal Amount, DateOnly TransactionDate, DateOnly? ExpectedReturnDate, DateTime? NextReminderAt, [StringLength(2000)] string? Description);
public record PaymentRequest([Range(0.01, 999999999)] decimal Amount, DateOnly PaymentDate, PaymentMethod Method, [StringLength(500)] string? Note);
public record CreditQuery(CreditDirection? Direction, CreditStatus? Status, bool OverdueOnly, Guid? ContactId, string? Search, int Page = 1, int PageSize = 25);
public record CreditSummaryDto(decimal TotalReceivable, decimal TotalPayable, decimal AmountReceived, decimal AmountPaid, decimal TotalGiven, decimal TotalBorrowed,
    decimal NetBalance, int OverdueCount, decimal OverdueAmount, IReadOnlyList<CreditDto> UpcomingReminders, IReadOnlyList<PersonBalance> People);
public record PersonBalance(string PersonName, Guid? ContactId, decimal Receivable, decimal Payable, decimal Net);

public sealed class UdharService(IAppDbContext db, FileService files, IExportService export, IAuditService audit, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private CreditDto Map(CreditTransaction t) => new(t.Id, t.Direction, t.ContactId, t.PersonName, t.ContactNumber, t.Amount, t.SettledAmount, t.Amount - t.SettledAmount,
        t.TransactionDate, t.ExpectedReturnDate, t.NextReminderAt, t.Description, t.Status,
        t.Status != CreditStatus.Settled && t.ExpectedReturnDate != null && t.ExpectedReturnDate < Today,
        t.Payments.OrderByDescending(p => p.PaymentDate).Select(p => new PaymentDto(p.Id, p.Amount, p.PaymentDate, p.Method, p.Note, p.CreatedAt)).ToList(),
        t.Attachments.Where(a => a.StoredFile != null).Select(a => FileService.ToDto(a.StoredFile!)).ToList(), t.CreatedAt);

    private IQueryable<CreditTransaction> Filter(CreditQuery q)
    {
        var query = db.CreditTransactions.AsNoTracking();
        if (q.Direction is { } d) query = query.Where(t => t.Direction == d);
        if (q.Status is { } s) query = query.Where(t => t.Status == s);
        if (q.ContactId is { } c) query = query.Where(t => t.ContactId == c);
        if (q.OverdueOnly) { var today = Today; query = query.Where(t => t.Status != CreditStatus.Settled && t.ExpectedReturnDate != null && t.ExpectedReturnDate < today); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(t => EF.Functions.ILike(t.PersonName, like) || (t.Description != null && EF.Functions.ILike(t.Description, like)) || (t.ContactNumber != null && EF.Functions.ILike(t.ContactNumber, like)));
        }
        return query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.CreatedAt);
    }

    public async Task<PagedResult<CreditDto>> GetAsync(CreditQuery q, CancellationToken ct)
    {
        var page = await Filter(q).Include(t => t.Payments).Include(t => t.Attachments).ThenInclude(a => a.StoredFile).AsSplitQuery().ToPagedAsync(q.Page, q.PageSize, ct);
        return new PagedResult<CreditDto>(page.Items.Select(Map).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CreditDto> GetOneAsync(Guid id, CancellationToken ct) =>
        Map(await db.CreditTransactions.AsNoTracking().Include(t => t.Payments).Include(t => t.Attachments).ThenInclude(a => a.StoredFile)
            .FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Transaction"));

    public async Task<CreditDto> SaveAsync(Guid? id, CreditRequest r, CancellationToken ct)
    {
        string personName;
        string? number = TextUtil.Clean(r.ContactNumber, 20);
        if (r.ContactId is { } cid)
        {
            var c = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new ValidationAppException("contactId", "Contact not found.");
            personName = c.Name; number ??= c.Mobile;
        }
        else personName = TextUtil.Clean(r.PersonName, 150) ?? throw new ValidationAppException("personName", "Enter the person's name or choose a contact.");
        if (r.ExpectedReturnDate is { } erd && erd < r.TransactionDate) throw new ValidationAppException("expectedReturnDate", "Return date cannot be before the transaction date.");
        CreditTransaction t;
        if (id is { } e)
        {
            t = await db.CreditTransactions.FirstOrDefaultAsync(x => x.Id == e, ct) ?? throw new NotFoundException("Transaction");
            if (r.Amount < t.SettledAmount) throw new ValidationAppException("amount", $"Amount cannot be less than the ₹{t.SettledAmount:N2} already settled.");
        }
        else { t = new CreditTransaction(); db.CreditTransactions.Add(t); }
        t.Direction = r.Direction; t.ContactId = r.ContactId; t.PersonName = personName; t.ContactNumber = number; t.Amount = Math.Round(r.Amount, 2);
        t.TransactionDate = r.TransactionDate; t.ExpectedReturnDate = r.ExpectedReturnDate; t.NextReminderAt = r.NextReminderAt; t.Description = TextUtil.Clean(r.Description, 2000);
        Recalculate(t);
        await db.SaveChangesAsync(ct);
        await SyncReminderAsync(t, ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "CreditTransaction", t.Id.ToString()), ct);
        return await GetOneAsync(t.Id, ct);
    }

    private async Task SyncReminderAsync(CreditTransaction t, CancellationToken ct)
    {
        await db.CreditReminders.Where(r => r.TransactionId == t.Id && r.SentAt == null).ExecuteDeleteAsync(ct);
        if (t.NextReminderAt is { } at && t.Status != CreditStatus.Settled && at > clock.GetUtcNow().UtcDateTime)
        {
            db.CreditReminders.Add(new CreditReminder { TransactionId = t.Id, UserId = t.UserId, RemindAt = at,
                Note = t.Direction == CreditDirection.Given ? $"Collect ₹{t.Amount - t.SettledAmount:N2} from {t.PersonName}" : $"Repay ₹{t.Amount - t.SettledAmount:N2} to {t.PersonName}" });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var t = await db.CreditTransactions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Transaction");
        db.CreditTransactions.Remove(t);
        await db.CreditReminders.Where(r => r.TransactionId == id && r.SentAt == null).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "CreditTransaction", id.ToString()), ct);
    }

    /// <summary>Records a partial or full payment. Overpayment is rejected; status moves Open → PartiallySettled → Settled.</summary>
    public async Task<CreditDto> AddPaymentAsync(Guid id, PaymentRequest r, CancellationToken ct)
    {
        var t = await db.CreditTransactions.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Transaction");
        var amount = Math.Round(r.Amount, 2);
        var outstanding = t.Amount - t.SettledAmount;
        if (outstanding <= 0) throw new ConflictException("This transaction is already fully settled.");
        if (amount > outstanding) throw new ValidationAppException("amount", $"Payment exceeds the outstanding balance of ₹{outstanding:N2}.");
        if (r.PaymentDate < t.TransactionDate) throw new ValidationAppException("paymentDate", "Payment date cannot be before the transaction date.");
        var p = new CreditPayment { TransactionId = t.Id, Amount = amount, PaymentDate = r.PaymentDate, Method = r.Method, Note = TextUtil.Clean(r.Note, 500), CreatedAt = clock.GetUtcNow().UtcDateTime };
        t.Payments.Add(p); // EF tracks the new payment through the navigation
        Recalculate(t);
        await db.SaveChangesAsync(ct);
        if (t.Status == CreditStatus.Settled) await db.CreditReminders.Where(x => x.TransactionId == t.Id && x.SentAt == null).ExecuteDeleteAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "CreditTransaction", t.Id.ToString(), "payment recorded"), ct);
        return await GetOneAsync(id, ct);
    }

    public async Task<CreditDto> DeletePaymentAsync(Guid id, Guid paymentId, CancellationToken ct)
    {
        var t = await db.CreditTransactions.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Transaction");
        var p = t.Payments.FirstOrDefault(x => x.Id == paymentId) ?? throw new NotFoundException("Payment");
        t.Payments.Remove(p);
        db.CreditPayments.Remove(p);
        Recalculate(t);
        await db.SaveChangesAsync(ct);
        return await GetOneAsync(id, ct);
    }

    public static void Recalculate(CreditTransaction t)
    {
        t.SettledAmount = t.Payments.Sum(p => p.Amount);
        t.Status = t.SettledAmount <= 0 ? CreditStatus.Open : t.SettledAmount >= t.Amount ? CreditStatus.Settled : CreditStatus.PartiallySettled;
    }

    public async Task<FileDto> AddAttachmentAsync(Guid id, UploadInput input, CancellationToken ct)
    {
        if (!await db.CreditTransactions.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Transaction");
        var f = await files.UploadAsync(input, "udhar", ct);
        db.CreditAttachments.Add(new CreditAttachment { TransactionId = id, StoredFileId = f.Id, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(f);
    }

    public async Task<CreditSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var all = await db.CreditTransactions.AsNoTracking().Include(t => t.Payments).ToListAsync(ct);
        var given = all.Where(t => t.Direction == CreditDirection.Given).ToList();
        var borrowed = all.Where(t => t.Direction == CreditDirection.Borrowed).ToList();
        var overdue = all.Where(t => t.Status != CreditStatus.Settled && t.ExpectedReturnDate != null && t.ExpectedReturnDate < Today).ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var upcoming = all.Where(t => t.Status != CreditStatus.Settled && ((t.NextReminderAt != null && t.NextReminderAt >= now && t.NextReminderAt <= now.AddDays(30))
                || (t.ExpectedReturnDate != null && t.ExpectedReturnDate >= Today && t.ExpectedReturnDate <= Today.AddDays(30))))
            .OrderBy(t => t.NextReminderAt ?? t.ExpectedReturnDate!.Value.ToDateTime(TimeOnly.MinValue)).Take(10).Select(Map).ToList();
        var people = all.Where(t => t.Status != CreditStatus.Settled).GroupBy(t => new { Key = t.ContactId?.ToString() ?? t.PersonName.ToLowerInvariant(), t.ContactId })
            .Select(g => new PersonBalance(g.First().PersonName, g.Key.ContactId,
                g.Where(t => t.Direction == CreditDirection.Given).Sum(t => t.Amount - t.SettledAmount),
                g.Where(t => t.Direction == CreditDirection.Borrowed).Sum(t => t.Amount - t.SettledAmount), 0))
            .Select(p => p with { Net = p.Receivable - p.Payable }).OrderByDescending(p => Math.Abs(p.Net)).Take(50).ToList();
        var recv = given.Sum(t => t.Amount - t.SettledAmount);
        var pay = borrowed.Sum(t => t.Amount - t.SettledAmount);
        return new CreditSummaryDto(recv, pay, given.Sum(t => t.SettledAmount), borrowed.Sum(t => t.SettledAmount), given.Sum(t => t.Amount), borrowed.Sum(t => t.Amount),
            recv - pay, overdue.Count, overdue.Sum(t => t.Amount - t.SettledAmount), upcoming, people);
    }

    /// <summary>Person-wise statement with running balance (positive = they owe you).</summary>
    public async Task<(byte[] Data, string ContentType, string FileName)> StatementAsync(Guid? contactId, string? personName, string format, CancellationToken ct)
    {
        var q = db.CreditTransactions.AsNoTracking().Include(t => t.Payments).AsQueryable();
        if (contactId is { } cid) q = q.Where(t => t.ContactId == cid);
        else if (!string.IsNullOrWhiteSpace(personName)) q = q.Where(t => t.PersonName.ToLower() == personName.Trim().ToLower());
        else throw new ValidationAppException("contactId", "Choose a person.");
        var txs = await q.ToListAsync(ct);
        if (txs.Count == 0) throw new NotFoundException("Transactions for this person");
        var lines = new List<(DateOnly Date, string Desc, decimal Debit, decimal Credit)>();
        foreach (var t in txs)
        {
            var given = t.Direction == CreditDirection.Given;
            lines.Add((t.TransactionDate, (given ? "Money given" : "Money borrowed") + (t.Description != null ? $" — {t.Description}" : ""), given ? t.Amount : 0, given ? 0 : t.Amount));
            foreach (var p in t.Payments)
                lines.Add((p.PaymentDate, (given ? "Repayment received" : "Repayment made") + $" ({p.Method})" + (p.Note != null ? $" — {p.Note}" : ""), given ? 0 : p.Amount, given ? p.Amount : 0));
        }
        decimal running = 0;
        var rows = lines.OrderBy(l => l.Date).Select(l =>
        {
            running += l.Debit - l.Credit;
            return (IReadOnlyList<string?>)[l.Date.ToString("dd-MM-yyyy"), l.Desc, l.Debit == 0 ? "" : l.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                l.Credit == 0 ? "" : l.Credit.ToString("0.00", CultureInfo.InvariantCulture), running.ToString("0.00", CultureInfo.InvariantCulture)];
        }).ToList();
        var name = txs[0].PersonName;
        var table = new TableExport($"Udhar Statement — {name}", ["Date", "Particulars", "You gave / repaid (₹)", "You received / borrowed (₹)", "Balance (₹)"], rows,
            running >= 0 ? $"Closing balance: {name} owes you ₹{running:N2}" : $"Closing balance: you owe {name} ₹{-running:N2}");
        var safe = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return format == "pdf" ? (export.ToPdf(table), "application/pdf", $"udhar-statement-{safe}.pdf")
            : (export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"udhar-statement-{safe}.xlsx");
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportAsync(CreditQuery q, string format, CancellationToken ct)
    {
        var items = await Filter(q).Take(50000).ToListAsync(ct);
        var table = new TableExport("Udhar Ledger", ["Date", "Type", "Person", "Mobile", "Amount", "Settled", "Outstanding", "Return By", "Status", "Description"],
            items.Select(t => (IReadOnlyList<string?>)[t.TransactionDate.ToString("dd-MM-yyyy"), t.Direction == CreditDirection.Given ? "Given" : "Borrowed", t.PersonName, t.ContactNumber,
                t.Amount.ToString("0.00", CultureInfo.InvariantCulture), t.SettledAmount.ToString("0.00", CultureInfo.InvariantCulture), (t.Amount - t.SettledAmount).ToString("0.00", CultureInfo.InvariantCulture),
                t.ExpectedReturnDate?.ToString("dd-MM-yyyy"), t.Status.ToString(), t.Description]).ToList());
        return format == "pdf" ? (export.ToPdf(table), "application/pdf", "udhar-ledger.pdf")
            : (export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "udhar-ledger.xlsx");
    }
}
