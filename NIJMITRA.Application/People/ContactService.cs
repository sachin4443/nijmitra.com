using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.People;

public record GroupDto(Guid Id, string Name, string? Color, int ContactCount);
public record GroupRequest([Required, StringLength(60, MinimumLength = 1)] string Name, string? Color);
public record ContactDto(Guid Id, string Name, string? Mobile, string? AlternateMobile, string? Email, string? Address, string? Company, string? Category,
    Guid? GroupId, string? GroupName, string? Notes, Guid? AvatarFileId, IReadOnlyList<FileDto> Attachments, DateTime UpdatedAt);
public record ContactRequest([Required, StringLength(150, MinimumLength = 1)] string Name, [StringLength(20)] string? Mobile, [StringLength(20)] string? AlternateMobile,
    [EmailAddress, StringLength(256)] string? Email, [StringLength(500)] string? Address, [StringLength(150)] string? Company, [StringLength(60)] string? Category,
    Guid? GroupId, [StringLength(4000)] string? Notes);
public record ContactQuery(string? Search, Guid? GroupId, string? Category, int Page = 1, int PageSize = 30);
public record ContactActivity(string Type, Guid Id, string Title, decimal? Amount, string? Direction, DateOnly Date);
public record ContactReminder(Guid Id, string Title, DateTime At, string Source);
public record ContactSummaryDto(ContactDto Contact, decimal MoneyGiven, decimal MoneyReceived, decimal MoneyBorrowed, decimal MoneyRepaid,
    decimal Receivable, decimal Payable, decimal NetBalance, IReadOnlyList<ContactActivity> Transactions, IReadOnlyList<ContactReminder> Reminders);

public sealed class ContactService(IAppDbContext db, FileService files, IExportService export, IAuditService audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<GroupDto>> GetGroupsAsync(CancellationToken ct) =>
        await db.ContactGroups.AsNoTracking().OrderBy(g => g.Name).Select(g => new GroupDto(g.Id, g.Name, g.Color, db.Contacts.Count(c => c.GroupId == g.Id))).ToListAsync(ct);

    public async Task<GroupDto> SaveGroupAsync(Guid? id, GroupRequest r, CancellationToken ct)
    {
        ContactGroup g;
        if (id is { } e) g = await db.ContactGroups.FirstOrDefaultAsync(x => x.Id == e, ct) ?? throw new NotFoundException("Group");
        else
        {
            if (await db.ContactGroups.AnyAsync(x => x.Name.ToLower() == r.Name.Trim().ToLower(), ct)) throw new ConflictException("A group with this name already exists.");
            g = new ContactGroup(); db.ContactGroups.Add(g);
        }
        g.Name = r.Name.Trim(); g.Color = r.Color;
        await db.SaveChangesAsync(ct);
        return new GroupDto(g.Id, g.Name, g.Color, 0);
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken ct)
    {
        var g = await db.ContactGroups.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Group");
        await db.Contacts.Where(c => c.GroupId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.GroupId, (Guid?)null), ct);
        db.ContactGroups.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<Contact> Filter(ContactQuery q)
    {
        var query = db.Contacts.AsNoTracking();
        if (q.GroupId is { } g) query = query.Where(c => c.GroupId == g);
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(c => c.Category == q.Category);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(c => EF.Functions.ILike(c.Name, like) || (c.Mobile != null && EF.Functions.ILike(c.Mobile, like)) || (c.Email != null && EF.Functions.ILike(c.Email, like))
                || (c.Company != null && EF.Functions.ILike(c.Company, like)) || (c.AlternateMobile != null && EF.Functions.ILike(c.AlternateMobile, like)));
        }
        return query.OrderBy(c => c.Name);
    }

    private static ContactDto Map(Contact c) => new(c.Id, c.Name, c.Mobile, c.AlternateMobile, c.Email, c.Address, c.Company, c.Category, c.GroupId, c.Group?.Name, c.Notes,
        c.AvatarFileId, c.Attachments.Where(a => a.StoredFile != null).Select(a => FileService.ToDto(a.StoredFile!)).ToList(), c.UpdatedAt);

    public async Task<PagedResult<ContactDto>> GetContactsAsync(ContactQuery q, CancellationToken ct)
    {
        var page = await Filter(q).Include(c => c.Group).ToPagedAsync(q.Page, q.PageSize, ct);
        return new PagedResult<ContactDto>(page.Items.Select(Map).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<ContactDto> GetAsync(Guid id, CancellationToken ct) =>
        Map(await db.Contacts.AsNoTracking().Include(c => c.Group).Include(c => c.Attachments).ThenInclude(a => a.StoredFile)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Contact"));

    public async Task<ContactDto> SaveAsync(Guid? id, ContactRequest r, CancellationToken ct)
    {
        if (r.GroupId is { } g && !await db.ContactGroups.AnyAsync(x => x.Id == g, ct)) throw new ValidationAppException("groupId", "Group not found.");
        Contact c;
        if (id is { } e) c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == e, ct) ?? throw new NotFoundException("Contact");
        else { c = new Contact(); db.Contacts.Add(c); }
        c.Name = r.Name.Trim(); c.Mobile = TextUtil.Clean(r.Mobile, 20); c.AlternateMobile = TextUtil.Clean(r.AlternateMobile, 20); c.Email = TextUtil.Clean(r.Email, 256)?.ToLowerInvariant();
        c.Address = TextUtil.Clean(r.Address, 500); c.Company = TextUtil.Clean(r.Company, 150); c.Category = TextUtil.Clean(r.Category, 60); c.GroupId = r.GroupId; c.Notes = TextUtil.Clean(r.Notes, 4000);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "Contact", c.Id.ToString()), ct);
        return await GetAsync(c.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Contact");
        db.Contacts.Remove(c);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "Contact", id.ToString()), ct);
    }

    public async Task<Guid> SetAvatarAsync(Guid id, UploadInput input, CancellationToken ct)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Contact");
        var f = await files.UploadAsync(input, "contact-avatar", ct, FileService.ImageExtensions);
        if (c.AvatarFileId is { } old) await files.DeleteOwnAsync(old, ct);
        c.AvatarFileId = f.Id;
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    public async Task<FileDto> AddAttachmentAsync(Guid id, UploadInput input, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Contact");
        var f = await files.UploadAsync(input, "contact", ct);
        db.ContactAttachments.Add(new ContactAttachment { ContactId = id, StoredFileId = f.Id, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(f);
    }

    public async Task RemoveAttachmentAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        if (!await db.Contacts.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Contact");
        var a = await db.ContactAttachments.FirstOrDefaultAsync(x => x.ContactId == id && x.StoredFileId == fileId, ct) ?? throw new NotFoundException("Attachment");
        db.ContactAttachments.Remove(a);
        await db.SaveChangesAsync(ct);
        await files.DeleteOwnAsync(fileId, ct);
    }

    /// <summary>Contact-wise view: information, money given/received, balances, transactions, reminders and documents.</summary>
    public async Task<ContactSummaryDto> GetSummaryAsync(Guid id, CancellationToken ct)
    {
        var contact = await GetAsync(id, ct);
        var credits = await db.CreditTransactions.AsNoTracking().Where(t => t.ContactId == id).Include(t => t.Payments).ToListAsync(ct);
        var given = credits.Where(t => t.Direction == CreditDirection.Given).ToList();
        var borrowed = credits.Where(t => t.Direction == CreditDirection.Borrowed).ToList();
        var activity = new List<ContactActivity>();
        foreach (var t in credits)
        {
            activity.Add(new("udhar", t.Id, t.Description ?? (t.Direction == CreditDirection.Given ? "Money given" : "Money borrowed"), t.Amount, t.Direction.ToString(), t.TransactionDate));
            activity.AddRange(t.Payments.Select(p => new ContactActivity("udhar-payment", t.Id, t.Direction == CreditDirection.Given ? "Repayment received" : "Repayment made", p.Amount,
                t.Direction == CreditDirection.Given ? "Received" : "Paid", p.PaymentDate)));
        }
        activity.AddRange(await db.Expenses.AsNoTracking().Where(e => e.ContactId == id).Select(e => new ContactActivity("expense", e.Id, e.Name, e.Amount, "Paid", e.Date)).ToListAsync(ct));
        activity.AddRange(await db.IncomeRecords.AsNoTracking().Where(e => e.ContactId == id).Select(e => new ContactActivity("income", e.Id, e.Title, e.Amount, "Received", e.Date)).ToListAsync(ct));
        var now = clock.GetUtcNow().UtcDateTime;
        var reminders = await db.CalendarEvents.AsNoTracking().Where(e => e.ContactId == id && e.StartUtc >= now.AddDays(-1)).OrderBy(e => e.StartUtc).Take(20)
            .Select(e => new ContactReminder(e.Id, e.Title, e.StartUtc, "calendar")).ToListAsync(ct);
        var creditIds = credits.Select(c => c.Id).ToList();
        reminders.AddRange(await db.CreditReminders.AsNoTracking().Where(r => creditIds.Contains(r.TransactionId) && r.SentAt == null).OrderBy(r => r.RemindAt).Take(20)
            .Select(r => new ContactReminder(r.TransactionId, r.Note ?? "Udhar reminder", r.RemindAt, "udhar")).ToListAsync(ct));
        var receivable = given.Sum(t => t.Amount - t.SettledAmount);
        var payable = borrowed.Sum(t => t.Amount - t.SettledAmount);
        return new ContactSummaryDto(contact, given.Sum(t => t.Amount), given.Sum(t => t.SettledAmount), borrowed.Sum(t => t.Amount), borrowed.Sum(t => t.SettledAmount),
            receivable, payable, receivable - payable, activity.OrderByDescending(a => a.Date).Take(200).ToList(), reminders.OrderBy(r => r.At).ToList());
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportAsync(ContactQuery q, string format, CancellationToken ct)
    {
        var items = await Filter(q).Include(c => c.Group).Take(50000).ToListAsync(ct);
        var table = new TableExport("Contacts", ["Name", "Mobile", "Alternate Mobile", "Email", "Company", "Group", "Category", "Address"],
            items.Select(c => (IReadOnlyList<string?>)[c.Name, c.Mobile, c.AlternateMobile, c.Email, c.Company, c.Group?.Name, c.Category, c.Address]).ToList());
        return format == "pdf" ? (export.ToPdf(table), "application/pdf", "contacts.pdf")
            : format == "csv" ? (export.ToCsv(table), "text/csv", "contacts.csv")
            : (export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "contacts.xlsx");
    }
}
