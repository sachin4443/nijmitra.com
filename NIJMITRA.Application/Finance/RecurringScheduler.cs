using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Finance;

public static class RecurringScheduler
{
    public static DateOnly Next(DateOnly date, RecurrenceFrequency f, int? customDays, DateOnly anchor) => f switch
    {
        RecurrenceFrequency.Daily => date.AddDays(1),
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Monthly => AddMonthsAnchored(date, 1, anchor),
        RecurrenceFrequency.Quarterly => AddMonthsAnchored(date, 3, anchor),
        RecurrenceFrequency.Yearly => AddMonthsAnchored(date, 12, anchor),
        _ => date.AddDays(Math.Max(1, customDays ?? 30))
    };

    /// <summary>Keeps the original day-of-month (e.g. 31st → 30th/28th → back to 31st).</summary>
    private static DateOnly AddMonthsAnchored(DateOnly date, int months, DateOnly anchor)
    {
        var m = date.AddMonths(months);
        var day = Math.Min(anchor.Day, DateTime.DaysInMonth(m.Year, m.Month));
        return new DateOnly(m.Year, m.Month, day);
    }

    /// <summary>Due dates from start up to horizon (inclusive), capped for safety.</summary>
    public static IEnumerable<DateOnly> DueDates(RecurringExpense r, DateOnly horizon)
    {
        var d = r.StartDate;
        for (var i = 0; i < 20000 && d <= horizon && (r.EndDate == null || d <= r.EndDate); i++)
        {
            yield return d;
            d = Next(d, r.Frequency, r.CustomIntervalDays, r.StartDate);
        }
    }
}
