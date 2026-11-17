using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Journal;

public record EventDto(Guid Id, string Title, string? Description, CalendarEventType EventType, DateTime StartUtc, DateTime EndUtc, bool AllDay, string? Location,
    EventPriority Priority, IReadOnlyList<int> ReminderOffsets, EventRecurrence Recurrence, DateTime? RecurrenceUntilUtc, bool IsCompleted, Guid? ContactId,
    IReadOnlyList<FileDto> Attachments);
public record EventOccurrence(Guid EventId, string Title, CalendarEventType EventType, DateTime StartUtc, DateTime EndUtc, bool AllDay, string? Location, EventPriority Priority, bool IsCompleted, bool IsRecurring);
public record EventRequest([Required, StringLength(200, MinimumLength = 1)] string Title, [StringLength(4000)] string? Description, CalendarEventType EventType,
    DateTime StartUtc, DateTime EndUtc, bool AllDay, [StringLength(300)] string? Location, EventPriority Priority, List<int>? ReminderOffsets,
    EventRecurrence Recurrence, DateTime? RecurrenceUntilUtc, Guid? ContactId);
public record ReminderDto(Guid Id, string Title, string? Notes, string Category, DateTime RemindAtUtc, EventRecurrence Repeat, bool IsCompleted);
public record ReminderRequest([Required, StringLength(200, MinimumLength = 1)] string Title, [StringLength(2000)] string? Notes, [StringLength(40)] string? Category,
    DateTime RemindAtUtc, EventRecurrence Repeat);
public record AgendaItem(string Kind, Guid Id, string Title, DateTime At, string? Detail, string? Link);

public sealed class CalendarService(IAppDbContext db, FileService files, ReminderPlanner planner, IAuditService audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static EventDto Map(CalendarEvent e) => new(e.Id, e.Title, e.Description, e.EventType, e.StartUtc, e.EndUtc, e.AllDay, e.Location, e.Priority, e.ReminderOffsets,
        e.Recurrence, e.RecurrenceUntilUtc, e.IsCompleted, e.ContactId, e.Attachments.Where(a => a.StoredFile != null).Select(a => FileService.ToDto(a.StoredFile!)).ToList());

    public async Task<IReadOnlyList<EventOccurrence>> GetRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc || (toUtc - fromUtc).TotalDays > 400) throw new ValidationAppException("to", "Choose a range of up to 400 days.");
        var events = await db.CalendarEvents.AsNoTracking().Where(e => (e.Recurrence == EventRecurrence.None && e.StartUtc < toUtc && e.EndUtc >= fromUtc)
            || (e.Recurrence != EventRecurrence.None && e.StartUtc < toUtc && (e.RecurrenceUntilUtc == null || e.RecurrenceUntilUtc >= fromUtc))).ToListAsync(ct);
        var result = new List<EventOccurrence>();
        foreach (var e in events)
            foreach (var start in Occurrences(e.StartUtc, e.Recurrence, e.RecurrenceUntilUtc, fromUtc.Add(-(e.EndUtc - e.StartUtc)), toUtc))
                result.Add(new EventOccurrence(e.Id, e.Title, e.EventType, start, start + (e.EndUtc - e.StartUtc), e.AllDay, e.Location, e.Priority, e.IsCompleted, e.Recurrence != EventRecurrence.None));
        return result.OrderBy(r => r.StartUtc).ToList();
    }

    /// <summary>Expands a recurring series into occurrence start times within [from, to).</summary>
    public static IEnumerable<DateTime> Occurrences(DateTime start, EventRecurrence rec, DateTime? until, DateTime from, DateTime to)
    {
        if (rec == EventRecurrence.None) { if (start < to && start >= from.AddDays(-1)) yield return start; yield break; }
        var cur = start;
        for (var i = 0; i < 5000 && cur < to; i++)
        {
            if (until != null && cur > until) yield break;
            if (cur >= from) yield return cur;
            cur = rec switch
            {
                EventRecurrence.Daily => start.AddDays(i + 1),
                EventRecurrence.Weekly => start.AddDays(7 * (i + 1)),
                EventRecurrence.Monthly => start.AddMonths(i + 1),
                _ => start.AddYears(i + 1)
            };
        }
    }

    public async Task<EventDto> GetAsync(Guid id, CancellationToken ct) =>
        Map(await db.CalendarEvents.AsNoTracking().Include(e => e.Attachments).ThenInclude(a => a.StoredFile).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Event"));

    public async Task<EventDto> SaveAsync(Guid? id, EventRequest r, CancellationToken ct)
    {
        if (r.EndUtc < r.StartUtc) throw new ValidationAppException("endUtc", "End time must be after the start time.");
        if ((r.EndUtc - r.StartUtc).TotalDays > 31) throw new ValidationAppException("endUtc", "An event can last at most 31 days.");
        if (r.RecurrenceUntilUtc is { } u && u < r.StartUtc) throw new ValidationAppException("recurrenceUntilUtc", "Repeat-until must be after the start.");
        var offsets = (r.ReminderOffsets ?? []).Where(o => o is >= 0 and <= 60 * 24 * 30).Distinct().OrderBy(o => o).Take(5).ToList();
        if (r.ContactId is { } c && !await db.Contacts.AnyAsync(x => x.Id == c, ct)) throw new ValidationAppException("contactId", "Contact not found.");
        CalendarEvent e;
        if (id is { } existing) e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Event");
        else { e = new CalendarEvent(); db.CalendarEvents.Add(e); }
        e.Title = r.Title.Trim(); e.Description = TextUtil.Clean(r.Description, 4000); e.EventType = r.EventType; e.StartUtc = r.StartUtc; e.EndUtc = r.EndUtc; e.AllDay = r.AllDay;
        e.Location = TextUtil.Clean(r.Location, 300); e.Priority = r.Priority; e.ReminderOffsets = offsets; e.Recurrence = r.Recurrence; e.RecurrenceUntilUtc = r.RecurrenceUntilUtc;
        e.ContactId = r.ContactId;
        await db.SaveChangesAsync(ct);
        await planner.ReplanAsync(ReminderSourceType.CalendarEvent, e.Id, ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "CalendarEvent", e.Id.ToString()), ct);
        return await GetAsync(e.Id, ct);
    }

    public async Task SetCompletedAsync(Guid id, bool completed, CancellationToken ct)
    {
        var e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Event");
        e.IsCompleted = completed;
        await db.SaveChangesAsync(ct);
        await planner.ReplanAsync(ReminderSourceType.CalendarEvent, e.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Event");
        db.CalendarEvents.Remove(e);
        await db.SaveChangesAsync(ct);
        await planner.CancelAsync(ReminderSourceType.CalendarEvent, id, ct);
        await audit.LogAsync(new AuditEntry("record_delete", "CalendarEvent", id.ToString()), ct);
    }

    public async Task<FileDto> AddAttachmentAsync(Guid id, UploadInput input, CancellationToken ct)
    {
        if (!await db.CalendarEvents.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Event");
        var f = await files.UploadAsync(input, "calendar", ct);
        db.CalendarEventAttachments.Add(new CalendarEventAttachment { EventId = id, StoredFileId = f.Id, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(f);
    }

    // ---------------------------------------------------------------- standalone reminders
    public async Task<IReadOnlyList<ReminderDto>> GetRemindersAsync(bool includeCompleted, CancellationToken ct) =>
        await db.Reminders.AsNoTracking().Where(r => includeCompleted || !r.IsCompleted).OrderBy(r => r.IsCompleted).ThenBy(r => r.RemindAtUtc)
            .Select(r => new ReminderDto(r.Id, r.Title, r.Notes, r.Category, r.RemindAtUtc, r.Repeat, r.IsCompleted)).ToListAsync(ct);

    public async Task<ReminderDto> SaveReminderAsync(Guid? id, ReminderRequest r, CancellationToken ct)
    {
        Reminder e;
        if (id is { } existing) e = await db.Reminders.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Reminder");
        else { e = new Reminder(); db.Reminders.Add(e); }
        e.Title = r.Title.Trim(); e.Notes = TextUtil.Clean(r.Notes, 2000); e.Category = TextUtil.Clean(r.Category, 40) ?? "General"; e.RemindAtUtc = r.RemindAtUtc; e.Repeat = r.Repeat;
        e.IsCompleted = false;
        await db.SaveChangesAsync(ct);
        await planner.ReplanAsync(ReminderSourceType.Reminder, e.Id, ct);
        return new ReminderDto(e.Id, e.Title, e.Notes, e.Category, e.RemindAtUtc, e.Repeat, e.IsCompleted);
    }

    public async Task CompleteReminderAsync(Guid id, bool completed, CancellationToken ct)
    {
        var e = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Reminder");
        e.IsCompleted = completed;
        await db.SaveChangesAsync(ct);
        await planner.ReplanAsync(ReminderSourceType.Reminder, id, ct);
    }

    public async Task DeleteReminderAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Reminder");
        db.Reminders.Remove(e);
        await db.SaveChangesAsync(ct);
        await planner.CancelAsync(ReminderSourceType.Reminder, id, ct);
    }

    /// <summary>Unified upcoming list (events, reminders, bills, udhar) for the dashboard & reminders page.</summary>
    public async Task<IReadOnlyList<AgendaItem>> GetUpcomingAsync(int days, CancellationToken ct)
    {
        var to = Now.AddDays(Math.Clamp(days, 1, 90));
        var items = (await GetRangeAsync(Now.AddHours(-1), to, ct)).Where(o => !o.IsCompleted)
            .Select(o => new AgendaItem(o.EventType == CalendarEventType.Meeting ? "meeting" : o.EventType.ToString().ToLowerInvariant(), o.EventId, o.Title, o.StartUtc, o.Location, "/calendar")).ToList();
        items.AddRange(await db.Reminders.AsNoTracking().Where(r => !r.IsCompleted && r.RemindAtUtc >= Now.AddDays(-1) && r.RemindAtUtc <= to)
            .Select(r => new AgendaItem("reminder", r.Id, r.Title, r.RemindAtUtc, r.Notes, "/reminders")).ToListAsync(ct));
        var today = DateOnly.FromDateTime(Now); var toDate = DateOnly.FromDateTime(to);
        items.AddRange(await db.RecurringExpenseOccurrences.AsNoTracking()
            .Join(db.RecurringExpenses, o => o.RecurringExpenseId, r => r.Id, (o, r) => new { o, r })
            .Where(x => (x.o.Status == OccurrenceStatus.Pending || x.o.Status == OccurrenceStatus.Overdue) && x.o.DueDate <= toDate)
            .Select(x => new AgendaItem(x.o.Status == OccurrenceStatus.Overdue ? "bill-overdue" : "bill", x.o.Id, x.r.Name, x.o.DueDate.ToDateTime(new TimeOnly(9, 0)), "₹" + x.o.Amount.ToString(), "/finance/recurring"))
            .ToListAsync(ct));
        items.AddRange(await db.CreditTransactions.AsNoTracking().Where(t => t.Status != CreditStatus.Settled && t.ExpectedReturnDate != null && t.ExpectedReturnDate <= toDate)
            .Select(t => new AgendaItem(t.Direction == CreditDirection.Given ? "udhar-collect" : "udhar-repay", t.Id, t.PersonName, t.ExpectedReturnDate!.Value.ToDateTime(new TimeOnly(10, 0)),
                "₹" + (t.Amount - t.SettledAmount).ToString(), "/udhar")).ToListAsync(ct));
        return items.OrderBy(i => i.At).Take(100).ToList();
    }
}
