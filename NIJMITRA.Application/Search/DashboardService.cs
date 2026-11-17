using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Journal;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Search;

public record RecentTxn(Guid Id, string Kind, string Title, string? Category, decimal Amount, DateOnly Date);
public record RecentDoc(Guid Id, string Name, string Category, string Extension, DateTime UpdatedAt);
public record RecentDiary(Guid Id, string Title, DateOnly Date, string? Mood);
public record RecentChat(Guid ConversationId, string FriendName, Guid FriendId, string? LastMessage, DateTime? At, int Unread);
public record MonthPoint(string Label, decimal Income, decimal Expense);
public record CategoryPoint(string Name, string? Color, decimal Amount);
public record DashboardDto(
    decimal TotalIncome, decimal TotalExpenses, decimal MonthlyBudget, decimal RemainingBudget, decimal BudgetUtilization,
    decimal MoneyToReceive, decimal MoneyToPay, int OverdueBills, int UnreadNotifications,
    IReadOnlyList<AgendaItem> UpcomingReminders, IReadOnlyList<AgendaItem> UpcomingMeetings,
    IReadOnlyList<RecentTxn> RecentTransactions, IReadOnlyList<RecentDoc> RecentDocuments, IReadOnlyList<RecentDiary> RecentDiary,
    IReadOnlyList<RecentChat> RecentMessages, IReadOnlyList<MonthPoint> Trend, IReadOnlyList<CategoryPoint> CategoryBreakdown, string? LayoutJson);

/// <summary>All dashboard figures are computed live from the user's records — no placeholder statistics.</summary>
public sealed class DashboardService(IAppDbContext db, ICurrentUser current, CalendarService calendar, TimeProvider clock)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var income = await db.IncomeRecords.Where(i => i.Date >= monthStart && i.Date <= monthEnd).SumAsync(i => (decimal?)i.Amount, ct) ?? 0;
        var expense = await db.Expenses.Where(e => e.Date >= monthStart && e.Date <= monthEnd).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
        var budget = await db.MonthlyBudgets.Where(b => b.Year == today.Year && b.Month == today.Month).Select(b => (decimal?)b.TotalAmount).FirstOrDefaultAsync(ct) ?? 0;
        var receivable = await db.CreditTransactions.Where(t => t.Direction == CreditDirection.Given && t.Status != CreditStatus.Settled).SumAsync(t => (decimal?)(t.Amount - t.SettledAmount), ct) ?? 0;
        var payable = await db.CreditTransactions.Where(t => t.Direction == CreditDirection.Borrowed && t.Status != CreditStatus.Settled).SumAsync(t => (decimal?)(t.Amount - t.SettledAmount), ct) ?? 0;
        var overdueBills = await db.RecurringExpenseOccurrences.CountAsync(o => o.UserId == uid && o.Status == OccurrenceStatus.Overdue && !o.RecurringExpense!.IsDeleted, ct);
        var unread = await db.Notifications.CountAsync(n => !n.IsRead, ct);

        var upcoming = await calendar.GetUpcomingAsync(14, ct);
        var meetings = upcoming.Where(u => u.Kind is "meeting" or "appointment").Take(6).ToList();
        var reminders = upcoming.Where(u => u.Kind is not ("meeting" or "appointment")).Take(8).ToList();

        var recentExp = await db.Expenses.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Take(6)
            .Select(e => new RecentTxn(e.Id, "expense", e.Name, e.Category!.Name, e.Amount, e.Date)).ToListAsync(ct);
        var recentInc = await db.IncomeRecords.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Take(6)
            .Select(e => new RecentTxn(e.Id, "income", e.Title, e.Source, e.Amount, e.Date)).ToListAsync(ct);
        var docs = await db.Documents.OrderByDescending(d => d.UpdatedAt).Take(5).Select(d => new RecentDoc(d.Id, d.Name, d.Category, d.Extension, d.UpdatedAt)).ToListAsync(ct);
        var diary = await db.DiaryEntries.OrderByDescending(d => d.EntryDate).ThenByDescending(d => d.EntryTime).Take(4).Select(d => new RecentDiary(d.Id, d.Title, d.EntryDate, d.Mood)).ToListAsync(ct);

        var chats = await db.ChatParticipants.Where(p => p.UserId == uid).Select(p => p.Conversation!)
            .Where(c => c.LastMessageAt != null).OrderByDescending(c => c.LastMessageAt).Take(5)
            .Select(c => new
            {
                c.Id, c.LastMessageAt,
                Friend = c.Participants.Where(x => x.UserId != uid).Select(x => new { x.UserId, x.User!.FullName }).FirstOrDefault(),
                Last = db.ChatMessages.Where(m => m.ConversationId == c.Id && m.DeletedAt == null && (m.ExpiresAt == null || m.ExpiresAt > now)).OrderByDescending(m => m.CreatedAt)
                    .Select(m => m.Type == ChatMessageType.Text ? m.Body : "📎 Attachment").FirstOrDefault(),
                MyRead = c.Participants.Where(x => x.UserId == uid).Select(x => x.LastReadAt).FirstOrDefault()
            }).ToListAsync(ct);
        var recentChats = new List<RecentChat>();
        foreach (var c in chats.Where(c => c.Friend != null))
        {
            var unreadCount = await db.ChatMessages.CountAsync(m => m.ConversationId == c.Id && m.SenderId != uid && m.DeletedAt == null && (c.MyRead == null || m.CreatedAt > c.MyRead), ct);
            recentChats.Add(new RecentChat(c.Id, c.Friend!.FullName, c.Friend.UserId, c.Last, c.LastMessageAt, unreadCount));
        }

        var trendStart = monthStart.AddMonths(-5);
        var expByMonth = await db.Expenses.Where(e => e.Date >= trendStart).GroupBy(e => new { e.Date.Year, e.Date.Month }).Select(g => new { g.Key.Year, g.Key.Month, S = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var incByMonth = await db.IncomeRecords.Where(e => e.Date >= trendStart).GroupBy(e => new { e.Date.Year, e.Date.Month }).Select(g => new { g.Key.Year, g.Key.Month, S = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var trend = Enumerable.Range(0, 6).Select(i => trendStart.AddMonths(i)).Select(d => new MonthPoint(d.ToString("MMM"),
            incByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.S ?? 0, expByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.S ?? 0)).ToList();
        var catSums = await db.Expenses.Where(e => e.Date >= monthStart && e.Date <= monthEnd).GroupBy(e => e.CategoryId)
            .Select(g => new { CategoryId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var catInfo = await db.ExpenseCategories.IgnoreQueryFilters().Where(c => c.UserId == uid).ToDictionaryAsync(c => c.Id, ct);
        var cats = catSums.OrderByDescending(c => c.Amount).Take(8)
            .Select(c => new CategoryPoint(catInfo.TryGetValue(c.CategoryId, out var ci) ? ci.Name : "Other", catInfo.GetValueOrDefault(c.CategoryId)?.Color, c.Amount)).ToList();
        var layout = await db.UserProfiles.Where(p => p.UserId == uid).Select(p => p.DashboardLayoutJson).FirstOrDefaultAsync(ct);

        return new DashboardDto(income, expense, budget, budget - expense, budget == 0 ? 0 : Math.Round(expense / budget * 100, 1), receivable, payable, overdueBills, unread,
            reminders, meetings, recentExp.Concat(recentInc).OrderByDescending(t => t.Date).Take(8).ToList(), docs, diary, recentChats, trend, cats, layout);
    }
}
