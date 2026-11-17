using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Finance;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Journal;

/// <summary>
/// Plans concrete reminder deliveries into reminder_schedules and dispatches due ones through the notification engine.
/// Every slot has a unique DedupeKey (source + occurrence + offset), so re-planning can never produce a duplicate send.
/// Runs from background jobs — it never depends on the user's browser being open.
/// </summary>
public sealed class ReminderPlanner(IAppDbContext db, INotificationService notifications, TimeProvider clock, ILogger<ReminderPlanner> logger)
{
    private static readonly string[] Owner = ["Owner"];
    private static readonly TimeSpan Window = TimeSpan.FromDays(8);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task CancelAsync(ReminderSourceType type, Guid sourceId, CancellationToken ct) =>
        await db.ReminderSchedules.Where(s => s.SourceType == type && s.SourceId == sourceId && s.Status == ScheduleStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ScheduleStatus.Cancelled).SetProperty(x => x.ProcessedAt, Now), ct);

    public async Task ReplanAsync(ReminderSourceType type, Guid sourceId, CancellationToken ct)
    {
        await db.ReminderSchedules.Where(s => s.SourceType == type && s.SourceId == sourceId && s.Status == ScheduleStatus.Pending).ExecuteDeleteAsync(ct);
        switch (type)
        {
            case ReminderSourceType.CalendarEvent:
                var e = await db.CalendarEvents.IgnoreQueryFilters(Owner).AsNoTracking().FirstOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted, ct);
                if (e != null) await PlanEventAsync(e, ct);
                break;
            case ReminderSourceType.Reminder:
                var r = await db.Reminders.IgnoreQueryFilters(Owner).AsNoTracking().FirstOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted, ct);
                if (r != null) await PlanReminderAsync(r, ct);
                break;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Background top-up of the planning window for all users.</summary>
    public async Task<int> PlanAllAsync(CancellationToken ct)
    {
        var horizon = Now.Add(Window);
        var events = await db.CalendarEvents.IgnoreQueryFilters(Owner).AsNoTracking()
            .Where(e => !e.IsDeleted && !e.IsCompleted && e.ReminderOffsets.Count > 0 && e.StartUtc <= horizon.AddDays(31)
                        && (e.Recurrence != EventRecurrence.None ? (e.RecurrenceUntilUtc == null || e.RecurrenceUntilUtc >= Now) : e.StartUtc >= Now.AddMinutes(-5)))
            .ToListAsync(ct);
        foreach (var e in events) await PlanEventAsync(e, ct);
        var reminders = await db.Reminders.IgnoreQueryFilters(Owner).AsNoTracking().Where(r => !r.IsDeleted && !r.IsCompleted && r.RemindAtUtc <= horizon).ToListAsync(ct);
        foreach (var r in reminders) await PlanReminderAsync(r, ct);

        // Recurring bills: generate occurrences, flag overdue, plan "due soon" reminders.
        var today = DateOnly.FromDateTime(Now);
        var recurring = await db.RecurringExpenses.IgnoreQueryFilters(Owner).Where(r => !r.IsDeleted && r.IsActive).ToListAsync(ct);
        foreach (var r in recurring) await FinanceService.GenerateOccurrencesAsync(db, r, today, ct);
        await db.RecurringExpenseOccurrences.Where(o => o.Status == OccurrenceStatus.Pending && o.DueDate < today)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OccurrenceStatus.Overdue), ct);
        var occ = await db.RecurringExpenseOccurrences.AsNoTracking().Include(o => o.RecurringExpense)
            .Where(o => (o.Status == OccurrenceStatus.Pending || o.Status == OccurrenceStatus.Overdue) && o.DueDate <= today.AddDays(31) && !o.RecurringExpense!.IsDeleted && o.RecurringExpense.IsActive)
            .ToListAsync(ct);
        foreach (var o in occ)
        {
            var tz = await UserZoneAsync(o.UserId, ct);
            var fire = LocalToUtc(o.DueDate.AddDays(-o.RecurringExpense!.ReminderDaysBefore), new TimeOnly(9, 0), tz);
            if (fire > horizon) continue;
            await AddAsync(o.UserId, ReminderSourceType.RecurringExpense, o.Id, $"bill:{o.Id}:{fire:yyyyMMdd}", fire, o.DueDate.ToDateTime(TimeOnly.MinValue), ct);
        }

        // Udhar: explicit reminders + expected-return-date reminders.
        var creditRem = await db.CreditReminders.AsNoTracking().Where(r => r.SentAt == null && r.RemindAt <= horizon).ToListAsync(ct);
        foreach (var cr in creditRem) await AddAsync(cr.UserId, ReminderSourceType.CreditReminder, cr.Id, $"credit-rem:{cr.Id}", cr.RemindAt, null, ct);
        var due = await db.CreditTransactions.IgnoreQueryFilters(Owner).AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != CreditStatus.Settled && t.ExpectedReturnDate != null && t.ExpectedReturnDate <= today.AddDays(9)).ToListAsync(ct);
        foreach (var t in due)
        {
            var tz = await UserZoneAsync(t.UserId, ct);
            var fire = LocalToUtc(t.ExpectedReturnDate!.Value.AddDays(-1), new TimeOnly(10, 0), tz);
            await AddAsync(t.UserId, ReminderSourceType.CreditTransaction, t.Id, $"credit-due:{t.Id}:{t.ExpectedReturnDate:yyyyMMdd}", fire, null, ct);
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task PlanEventAsync(CalendarEvent e, CancellationToken ct)
    {
        if (e.IsCompleted || e.ReminderOffsets.Count == 0) return;
        var maxOffset = TimeSpan.FromMinutes(e.ReminderOffsets.Max());
        foreach (var start in CalendarService.Occurrences(e.StartUtc, e.Recurrence, e.RecurrenceUntilUtc, Now, Now.Add(Window).Add(maxOffset)))
            foreach (var off in e.ReminderOffsets)
            {
                var fire = start.AddMinutes(-off);
                if (fire < Now.AddMinutes(-2) || fire > Now.Add(Window)) continue;
                await AddAsync(e.UserId, ReminderSourceType.CalendarEvent, e.Id, $"event:{e.Id}:{start:yyyyMMddHHmm}:{off}", fire, start, ct);
            }
    }

    private async Task PlanReminderAsync(Reminder r, CancellationToken ct)
    {
        if (r.IsCompleted || r.RemindAtUtc < Now.AddMinutes(-2) || r.RemindAtUtc > Now.Add(Window)) return;
        await AddAsync(r.UserId, ReminderSourceType.Reminder, r.Id, $"reminder:{r.Id}:{r.RemindAtUtc:yyyyMMddHHmm}", r.RemindAtUtc, r.RemindAtUtc, ct);
    }

    private async Task AddAsync(Guid userId, ReminderSourceType type, Guid sourceId, string key, DateTime fireAt, DateTime? occurrence, CancellationToken ct)
    {
        if (db.ReminderSchedules.Local.Any(s => s.DedupeKey == key) || await db.ReminderSchedules.AnyAsync(s => s.DedupeKey == key, ct)) return;
        db.ReminderSchedules.Add(new ReminderSchedule { UserId = userId, SourceType = type, SourceId = sourceId, DedupeKey = key, FireAtUtc = fireAt, OccurrenceUtc = occurrence, CreatedAt = Now });
    }

    /// <summary>Sends every due reminder exactly once.</summary>
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var due = await db.ReminderSchedules.Where(s => s.Status == ScheduleStatus.Pending && s.FireAtUtc <= Now).OrderBy(s => s.FireAtUtc).Take(100).ToListAsync(ct);
        var sent = 0;
        foreach (var s in due)
        {
            var claimed = await db.ReminderSchedules.Where(x => x.Id == s.Id && x.Status == ScheduleStatus.Pending)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, ScheduleStatus.Sent).SetProperty(p => p.ProcessedAt, Now), ct);
            if (claimed == 0) continue;
            if (Now - s.FireAtUtc > TimeSpan.FromHours(6)) continue; // stale (server was down) — don't spam old reminders
            try
            {
                var req = await BuildAsync(s, ct);
                if (req != null) { await notifications.DispatchAsync(req, ct); sent++; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder {Key} failed", s.DedupeKey);
                await db.ReminderSchedules.Where(x => x.Id == s.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, ScheduleStatus.Failed), ct);
            }
        }
        return sent;
    }

    private async Task<NotificationRequest?> BuildAsync(ReminderSchedule s, CancellationToken ct)
    {
        var tz = await UserZoneAsync(s.UserId, ct);
        string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, tz).ToString("dd MMM yyyy, hh:mm tt");
        switch (s.SourceType)
        {
            case ReminderSourceType.CalendarEvent:
                var e = await db.CalendarEvents.IgnoreQueryFilters(Owner).AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.SourceId && !x.IsDeleted && !x.IsCompleted, ct);
                if (e == null) return null;
                var when = s.OccurrenceUtc ?? e.StartUtc;
                var evt = e.EventType is CalendarEventType.BillPayment ? NotificationEvents.ExpenseReminder
                    : e.EventType is CalendarEventType.Meeting or CalendarEventType.Appointment ? NotificationEvents.MeetingReminder : NotificationEvents.GeneralReminder;
                var local = TimeZoneInfo.ConvertTimeFromUtc(when, tz);
                return new NotificationRequest(evt, s.UserId, e.Title, $"{e.EventType} at {Local(when)}" + (e.Location != null ? $" · {e.Location}" : ""), "/calendar",
                    new Dictionary<string, string> { ["EventDate"] = local.ToString("dd MMM yyyy"), ["EventTime"] = local.ToString("hh:mm tt"), ["Location"] = e.Location ?? "" }, s.DedupeKey);
            case ReminderSourceType.Reminder:
                var r = await db.Reminders.IgnoreQueryFilters(Owner).FirstOrDefaultAsync(x => x.Id == s.SourceId && !x.IsDeleted && !x.IsCompleted, ct);
                if (r == null) return null;
                var req = new NotificationRequest(NotificationEvents.GeneralReminder, s.UserId, r.Title, r.Notes ?? $"Reminder for {Local(r.RemindAtUtc)}", "/reminders", DedupeKey: s.DedupeKey);
                if (r.Repeat != EventRecurrence.None)
                {
                    r.RemindAtUtc = r.Repeat switch { EventRecurrence.Daily => r.RemindAtUtc.AddDays(1), EventRecurrence.Weekly => r.RemindAtUtc.AddDays(7), EventRecurrence.Monthly => r.RemindAtUtc.AddMonths(1), _ => r.RemindAtUtc.AddYears(1) };
                    await db.SaveChangesAsync(ct);
                    await PlanReminderAsync(r, ct);
                    await db.SaveChangesAsync(ct);
                }
                return req;
            case ReminderSourceType.RecurringExpense:
                var o = await db.RecurringExpenseOccurrences.AsNoTracking().Include(x => x.RecurringExpense).FirstOrDefaultAsync(x => x.Id == s.SourceId, ct);
                if (o == null || o.Status is OccurrenceStatus.Paid or OccurrenceStatus.Skipped) return null;
                return new NotificationRequest(NotificationEvents.ExpenseReminder, s.UserId, $"{o.RecurringExpense!.Name} due {o.DueDate:dd MMM}",
                    $"₹{o.Amount:N2} for {o.RecurringExpense.Name} is due on {o.DueDate:dd MMM yyyy}.", "/finance/recurring", DedupeKey: s.DedupeKey);
            case ReminderSourceType.CreditReminder:
                var cr = await db.CreditReminders.FirstOrDefaultAsync(x => x.Id == s.SourceId && x.SentAt == null, ct);
                if (cr == null) return null;
                cr.SentAt = Now;
                await db.SaveChangesAsync(ct);
                var tx = await db.CreditTransactions.IgnoreQueryFilters(Owner).AsNoTracking().FirstOrDefaultAsync(x => x.Id == cr.TransactionId && !x.IsDeleted && x.Status != CreditStatus.Settled, ct);
                if (tx == null) return null;
                return new NotificationRequest(NotificationEvents.UdharReminder, s.UserId, $"Udhar reminder: {tx.PersonName}", cr.Note ?? $"Outstanding ₹{tx.Amount - tx.SettledAmount:N2}", "/udhar", DedupeKey: s.DedupeKey);
            case ReminderSourceType.CreditTransaction:
                var t = await db.CreditTransactions.IgnoreQueryFilters(Owner).AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.SourceId && !x.IsDeleted && x.Status != CreditStatus.Settled, ct);
                if (t == null) return null;
                var text = t.Direction == CreditDirection.Given
                    ? $"{t.PersonName} is expected to return ₹{t.Amount - t.SettledAmount:N2} by {t.ExpectedReturnDate:dd MMM yyyy}."
                    : $"You are due to repay ₹{t.Amount - t.SettledAmount:N2} to {t.PersonName} by {t.ExpectedReturnDate:dd MMM yyyy}.";
                return new NotificationRequest(NotificationEvents.UdharReminder, s.UserId, $"Udhar due: {t.PersonName}", text, "/udhar", DedupeKey: s.DedupeKey);
        }
        return null;
    }

    private readonly Dictionary<Guid, TimeZoneInfo> _zones = [];

    private async Task<TimeZoneInfo> UserZoneAsync(Guid userId, CancellationToken ct)
    {
        if (_zones.TryGetValue(userId, out var z)) return z;
        var id = await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.TimeZone).FirstOrDefaultAsync(ct) ?? "Asia/Kolkata";
        try { z = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { z = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        return _zones[userId] = z;
    }

    private static DateTime LocalToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), tz);
}
