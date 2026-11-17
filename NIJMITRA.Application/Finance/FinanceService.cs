using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Finance;

public sealed class FinanceService(IAppDbContext db, ICurrentUser current, IAuditService audit, INotificationService notifications,
    IExportService export, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // ---------------------------------------------------------------- categories
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CategoryType? type, CancellationToken ct) =>
        await db.ExpenseCategories.AsNoTracking().Where(c => type == null || c.Type == type).OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Type, c.Icon, c.Color, c.IsDefault)).ToListAsync(ct);

    public async Task<CategoryDto> CreateCategoryAsync(CategoryRequest r, CancellationToken ct)
    {
        var name = r.Name.Trim();
        if (await db.ExpenseCategories.AnyAsync(c => c.Type == r.Type && c.Name.ToLower() == name.ToLower(), ct)) throw new ConflictException("A category with this name already exists.");
        var c = new ExpenseCategory { Name = name, Icon = r.Icon, Color = ValidColor(r.Color), Type = r.Type };
        db.ExpenseCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Type, c.Icon, c.Color, c.IsDefault);
    }

    public async Task UpdateCategoryAsync(Guid id, CategoryRequest r, CancellationToken ct)
    {
        var c = await db.ExpenseCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category");
        c.Name = r.Name.Trim(); c.Icon = r.Icon; c.Color = ValidColor(r.Color);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ExpenseCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category");
        if (await db.Expenses.AnyAsync(e => e.CategoryId == id, ct) || await db.RecurringExpenses.AnyAsync(e => e.CategoryId == id, ct))
            throw new ConflictException("This category is used by expenses. Move them to another category first.");
        db.ExpenseCategories.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    private static string? ValidColor(string? c) => c is { Length: 7 } && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit) ? c : null;

    // ---------------------------------------------------------------- expenses
    private IQueryable<Expense> FilterExpenses(ExpenseQuery q)
    {
        var query = db.Expenses.AsNoTracking();
        if (q.From is { } f) query = query.Where(e => e.Date >= f);
        if (q.To is { } t) query = query.Where(e => e.Date <= t);
        if (q.CategoryId is { } c) query = query.Where(e => e.CategoryId == c);
        if (q.PaymentMethod is { } p) query = query.Where(e => e.PaymentMethod == p);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(e => EF.Functions.ILike(e.Name, like) || (e.Description != null && EF.Functions.ILike(e.Description, like)) || (e.Notes != null && EF.Functions.ILike(e.Notes, like)));
        }
        return query;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Expense, ExpenseDto>> ExpenseProjection = e =>
        new ExpenseDto(e.Id, e.Name, e.Amount, e.CategoryId, e.Category!.Name, e.Category.Color, e.PaymentMethod, e.Date, e.Description, e.Notes, e.ReceiptFileId, e.ContactId, e.CreatedAt);

    public async Task<PagedResult<ExpenseDto>> GetExpensesAsync(ExpenseQuery q, CancellationToken ct) =>
        await FilterExpenses(q).OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Select(ExpenseProjection).ToPagedAsync(q.Page, q.PageSize, ct);

    public async Task<decimal> SumExpensesAsync(ExpenseQuery q, CancellationToken ct) => await FilterExpenses(q).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

    public async Task<ExpenseDto> GetExpenseAsync(Guid id, CancellationToken ct) =>
        await db.Expenses.AsNoTracking().Where(e => e.Id == id).Select(ExpenseProjection).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Expense");

    public async Task<ExpenseDto> CreateExpenseAsync(ExpenseRequest r, CancellationToken ct)
    {
        await ValidateRefsAsync(r.CategoryId, r.ReceiptFileId, r.ContactId, ct);
        var e = new Expense();
        Apply(e, r);
        db.Expenses.Add(e);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_create", "Expense", e.Id.ToString()), ct);
        await CheckBudgetAsync(e.Date, ct);
        return await GetExpenseAsync(e.Id, ct);
    }

    public async Task<ExpenseDto> UpdateExpenseAsync(Guid id, ExpenseRequest r, CancellationToken ct)
    {
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense");
        await ValidateRefsAsync(r.CategoryId, r.ReceiptFileId, r.ContactId, ct);
        Apply(e, r);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "Expense", e.Id.ToString()), ct);
        await CheckBudgetAsync(e.Date, ct);
        return await GetExpenseAsync(e.Id, ct);
    }

    public async Task DeleteExpenseAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense");
        db.Expenses.Remove(e);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "Expense", id.ToString()), ct);
    }

    private static void Apply(Expense e, ExpenseRequest r)
    {
        e.Name = r.Name.Trim(); e.Amount = Math.Round(r.Amount, 2); e.CategoryId = r.CategoryId; e.PaymentMethod = r.PaymentMethod; e.Date = r.Date;
        e.Description = TextUtil.Clean(r.Description, 1000); e.Notes = TextUtil.Clean(r.Notes, 2000); e.ReceiptFileId = r.ReceiptFileId; e.ContactId = r.ContactId;
    }

    private async Task ValidateRefsAsync(Guid categoryId, Guid? fileId, Guid? contactId, CancellationToken ct)
    {
        if (!await db.ExpenseCategories.AnyAsync(c => c.Id == categoryId, ct)) throw new ValidationAppException("categoryId", "Select a valid category.");
        if (fileId is { } f && !await db.StoredFiles.AnyAsync(x => x.Id == f, ct)) throw new ValidationAppException("receiptFileId", "Receipt file not found.");
        if (contactId is { } c && !await db.Contacts.AnyAsync(x => x.Id == c, ct)) throw new ValidationAppException("contactId", "Contact not found.");
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportExpensesAsync(ExpenseQuery q, string format, CancellationToken ct)
    {
        var rows = await FilterExpenses(q).OrderByDescending(e => e.Date).Take(20000).Select(ExpenseProjection).ToListAsync(ct);
        var table = new TableExport("Expenses", ["Date", "Expense", "Category", "Payment Method", "Amount (₹)", "Description", "Notes"],
            rows.Select(e => (IReadOnlyList<string?>)[e.Date.ToString("yyyy-MM-dd"), e.Name, e.CategoryName, e.PaymentMethod.ToString(), e.Amount.ToString("0.00", CultureInfo.InvariantCulture), e.Description, e.Notes]).ToList(),
            $"{q.From?.ToString("dd MMM yyyy") ?? "All time"} – {q.To?.ToString("dd MMM yyyy") ?? "today"} · Total ₹{rows.Sum(r => r.Amount):N2}");
        return Render(table, format, "expenses");
    }

    internal (byte[], string, string) Render(TableExport table, string format, string baseName) => format.ToLowerInvariant() switch
    {
        "pdf" => (export.ToPdf(table), "application/pdf", $"{baseName}.pdf"),
        "csv" => (export.ToCsv(table), "text/csv", $"{baseName}.csv"),
        _ => (export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{baseName}.xlsx")
    };

    // ---------------------------------------------------------------- income
    private IQueryable<IncomeRecord> FilterIncome(IncomeQuery q)
    {
        var query = db.IncomeRecords.AsNoTracking();
        if (q.From is { } f) query = query.Where(e => e.Date >= f);
        if (q.To is { } t) query = query.Where(e => e.Date <= t);
        if (!string.IsNullOrWhiteSpace(q.Source)) query = query.Where(e => e.Source == q.Source);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var like = TextUtil.LikePattern(q.Search); query = query.Where(e => EF.Functions.ILike(e.Title, like) || (e.Description != null && EF.Functions.ILike(e.Description, like))); }
        return query;
    }

    public async Task<PagedResult<IncomeDto>> GetIncomeAsync(IncomeQuery q, CancellationToken ct) =>
        await FilterIncome(q).OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
            .Select(e => new IncomeDto(e.Id, e.Title, e.Source, e.Amount, e.Date, e.Description, e.Notes, e.ContactId, e.CreatedAt)).ToPagedAsync(q.Page, q.PageSize, ct);

    public async Task<IncomeDto> SaveIncomeAsync(Guid? id, IncomeRequest r, CancellationToken ct)
    {
        IncomeRecord e;
        if (id is { } existing) e = await db.IncomeRecords.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Income record");
        else { e = new IncomeRecord(); db.IncomeRecords.Add(e); }
        if (r.ContactId is { } c && !await db.Contacts.AnyAsync(x => x.Id == c, ct)) throw new ValidationAppException("contactId", "Contact not found.");
        e.Title = r.Title.Trim(); e.Source = r.Source.Trim(); e.Amount = Math.Round(r.Amount, 2); e.Date = r.Date;
        e.Description = TextUtil.Clean(r.Description, 1000); e.Notes = TextUtil.Clean(r.Notes, 2000); e.ContactId = r.ContactId;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "Income", e.Id.ToString()), ct);
        return new IncomeDto(e.Id, e.Title, e.Source, e.Amount, e.Date, e.Description, e.Notes, e.ContactId, e.CreatedAt);
    }

    public async Task DeleteIncomeAsync(Guid id, CancellationToken ct)
    {
        var e = await db.IncomeRecords.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Income record");
        db.IncomeRecords.Remove(e);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "Income", id.ToString()), ct);
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportIncomeAsync(IncomeQuery q, string format, CancellationToken ct)
    {
        var rows = await FilterIncome(q).OrderByDescending(e => e.Date).Take(20000).ToListAsync(ct);
        var table = new TableExport("Income", ["Date", "Title", "Source", "Amount (₹)", "Description"],
            rows.Select(e => (IReadOnlyList<string?>)[e.Date.ToString("yyyy-MM-dd"), e.Title, e.Source, e.Amount.ToString("0.00", CultureInfo.InvariantCulture), e.Description]).ToList(),
            $"Total ₹{rows.Sum(r => r.Amount):N2}");
        return Render(table, format, "income");
    }

    // ---------------------------------------------------------------- budgets
    public async Task<BudgetDto> GetBudgetAsync(int year, int month, CancellationToken ct)
    {
        var budget = await db.MonthlyBudgets.AsNoTracking().Include(b => b.Categories).FirstOrDefaultAsync(b => b.Year == year && b.Month == month, ct);
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var spentByCat = await db.Expenses.AsNoTracking().Where(e => e.Date >= from && e.Date <= to)
            .GroupBy(e => e.CategoryId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var cats = await db.ExpenseCategories.AsNoTracking().Where(c => c.Type == CategoryType.Expense).ToDictionaryAsync(c => c.Id, ct);
        var totalSpent = spentByCat.Values.Sum();
        var lines = new List<BudgetCategoryLine>();
        var budgeted = budget?.Categories.ToDictionary(c => c.CategoryId, c => c.Amount) ?? [];
        foreach (var catId in budgeted.Keys.Union(spentByCat.Keys))
        {
            if (!cats.TryGetValue(catId, out var cat)) continue;
            var b = budgeted.GetValueOrDefault(catId);
            var s = spentByCat.GetValueOrDefault(catId);
            lines.Add(new BudgetCategoryLine(catId, cat.Name, cat.Color, b, s, b - s, b == 0 ? (s > 0 ? 100 : 0) : Math.Round(s / b * 100, 1), b > 0 && s > b));
        }
        var total = budget?.TotalAmount ?? 0;
        return new BudgetDto(budget?.Id, year, month, total, totalSpent, total - totalSpent, total == 0 ? 0 : Math.Round(totalSpent / total * 100, 1),
            budget?.AlertThresholdPercent ?? 90, total > 0 && totalSpent > total, lines.OrderByDescending(l => l.Spent).ToList());
    }

    public async Task<BudgetDto> SaveBudgetAsync(BudgetRequest r, CancellationToken ct)
    {
        var catIds = r.Categories.Select(c => c.CategoryId).Distinct().ToList();
        if (await db.ExpenseCategories.CountAsync(c => catIds.Contains(c.Id), ct) != catIds.Count) throw new ValidationAppException("categories", "Invalid category.");
        if (r.Categories.Sum(c => c.Amount) > r.TotalAmount && r.TotalAmount > 0)
            throw new ValidationAppException("categories", "Category budgets add up to more than the monthly budget.");
        var b = await db.MonthlyBudgets.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Year == r.Year && x.Month == r.Month, ct);
        if (b == null) { b = new MonthlyBudget { Year = r.Year, Month = r.Month }; db.MonthlyBudgets.Add(b); }
        b.TotalAmount = Math.Round(r.TotalAmount, 2); b.AlertThresholdPercent = r.AlertThresholdPercent; b.LastAlertSentAt = null;
        db.BudgetCategories.RemoveRange(b.Categories);
        foreach (var c in r.Categories.Where(c => c.Amount > 0).GroupBy(c => c.CategoryId))
            db.BudgetCategories.Add(new BudgetCategory { MonthlyBudgetId = b.Id, CategoryId = c.Key, Amount = Math.Round(c.Sum(x => x.Amount), 2) });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "MonthlyBudget", b.Id.ToString()), ct);
        return await GetBudgetAsync(r.Year, r.Month, ct);
    }

    public async Task<IReadOnlyList<MonthComparison>> CompareMonthsAsync(int months, CancellationToken ct)
    {
        months = Math.Clamp(months, 2, 24);
        var start = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-(months - 1));
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= start).GroupBy(e => new { e.Date.Year, e.Date.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var income = await db.IncomeRecords.AsNoTracking().Where(e => e.Date >= start).GroupBy(e => new { e.Date.Year, e.Date.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var budgets = await db.MonthlyBudgets.AsNoTracking().Where(b => b.Year * 12 + b.Month >= start.Year * 12 + start.Month).ToListAsync(ct);
        return Enumerable.Range(0, months).Select(i => start.AddMonths(i)).Select(d => new MonthComparison(d.Year, d.Month,
            budgets.FirstOrDefault(b => b.Year == d.Year && b.Month == d.Month)?.TotalAmount ?? 0,
            expenses.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Sum ?? 0,
            income.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Sum ?? 0)).ToList();
    }

    /// <summary>Sends one overspending alert per budget each time the threshold is crossed.</summary>
    private async Task CheckBudgetAsync(DateOnly date, CancellationToken ct)
    {
        var budget = await db.MonthlyBudgets.FirstOrDefaultAsync(b => b.Year == date.Year && b.Month == date.Month, ct);
        if (budget == null || budget.TotalAmount <= 0) return;
        var dto = await GetBudgetAsync(date.Year, date.Month, ct);
        if (dto.UtilizationPercent < budget.AlertThresholdPercent || budget.LastAlertSentAt != null) return;
        budget.LastAlertSentAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        var monthName = new DateTime(date.Year, date.Month, 1).ToString("MMMM yyyy");
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.BudgetAlert, current.RequireUserId(),
            dto.Overspent ? $"Budget exceeded for {monthName}" : $"{dto.UtilizationPercent:0}% of {monthName} budget used",
            $"You have spent ₹{dto.TotalSpent:N2} of your ₹{dto.TotalBudget:N2} budget for {monthName}.", "/finance/budget",
            DedupeKey: $"budget:{budget.Id}:{budget.LastAlertSentAt:yyyyMMddHHmm}"), ct);
    }

    // ---------------------------------------------------------------- recurring
    public async Task<IReadOnlyList<RecurringDto>> GetRecurringAsync(CancellationToken ct)
    {
        var today = Today;
        return await db.RecurringExpenses.AsNoTracking().OrderBy(r => r.Name).Select(r => new RecurringDto(r.Id, r.Name, r.Amount, r.CategoryId, r.Category!.Name, r.PaymentMethod,
            r.Frequency, r.CustomIntervalDays, r.StartDate, r.EndDate, r.ReminderDaysBefore, r.IsActive, r.Notes,
            r.Occurrences.Where(o => o.Status == OccurrenceStatus.Pending || o.Status == OccurrenceStatus.Overdue).OrderBy(o => o.DueDate).Select(o => (DateOnly?)o.DueDate).FirstOrDefault(),
            r.Occurrences.Count(o => o.Status == OccurrenceStatus.Overdue))).ToListAsync(ct);
    }

    public async Task<Guid> SaveRecurringAsync(Guid? id, RecurringRequest r, CancellationToken ct)
    {
        if (r.Frequency == RecurrenceFrequency.Custom && r.CustomIntervalDays is null) throw new ValidationAppException("customIntervalDays", "Enter the repeat interval in days.");
        if (r.EndDate is { } end && end < r.StartDate) throw new ValidationAppException("endDate", "End date must be after the start date.");
        if (!await db.ExpenseCategories.AnyAsync(c => c.Id == r.CategoryId, ct)) throw new ValidationAppException("categoryId", "Select a valid category.");
        RecurringExpense e;
        if (id is { } existing)
        {
            e = await db.RecurringExpenses.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Recurring expense");
            // schedule changed → drop future unpaid occurrences, they'll be regenerated
            await db.RecurringExpenseOccurrences.Where(o => o.RecurringExpenseId == e.Id && o.Status == OccurrenceStatus.Pending && o.DueDate >= Today).ExecuteDeleteAsync(ct);
        }
        else { e = new RecurringExpense(); db.RecurringExpenses.Add(e); }
        e.Name = r.Name.Trim(); e.Amount = Math.Round(r.Amount, 2); e.CategoryId = r.CategoryId; e.PaymentMethod = r.PaymentMethod; e.Frequency = r.Frequency;
        e.CustomIntervalDays = r.Frequency == RecurrenceFrequency.Custom ? r.CustomIntervalDays : null; e.StartDate = r.StartDate; e.EndDate = r.EndDate;
        e.ReminderDaysBefore = r.ReminderDaysBefore; e.IsActive = r.IsActive; e.Notes = TextUtil.Clean(r.Notes, 2000);
        await db.SaveChangesAsync(ct);
        await GenerateOccurrencesAsync(db, e, Today, ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "RecurringExpense", e.Id.ToString()), ct);
        return e.Id;
    }

    public async Task DeleteRecurringAsync(Guid id, CancellationToken ct)
    {
        var e = await db.RecurringExpenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Recurring expense");
        await db.RecurringExpenseOccurrences.Where(o => o.RecurringExpenseId == id && o.Status != OccurrenceStatus.Paid).ExecuteDeleteAsync(ct);
        db.RecurringExpenses.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates missing occurrences (last 31 days → next 62 days) and flags overdue ones. Idempotent (unique index).</summary>
    public static async Task GenerateOccurrencesAsync(IAppDbContext db, RecurringExpense r, DateOnly today, CancellationToken ct)
    {
        if (!r.IsActive) return;
        var lower = today.AddDays(-31);
        var existing = await db.RecurringExpenseOccurrences.Where(o => o.RecurringExpenseId == r.Id && o.DueDate >= lower).Select(o => o.DueDate).ToListAsync(ct);
        foreach (var d in RecurringScheduler.DueDates(r, today.AddDays(62)).Where(d => d >= lower && d >= DateOnly.FromDateTime(r.CreatedAt.AddDays(-31)) && !existing.Contains(d)))
            db.RecurringExpenseOccurrences.Add(new RecurringExpenseOccurrence { RecurringExpenseId = r.Id, UserId = r.UserId, DueDate = d, Amount = r.Amount, Status = d < today ? OccurrenceStatus.Overdue : OccurrenceStatus.Pending });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OccurrenceDto>> GetOccurrencesAsync(OccurrenceStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var q = db.RecurringExpenseOccurrences.AsNoTracking().Where(o => o.UserId == uid && !o.RecurringExpense!.IsDeleted);
        if (status is { } s) q = q.Where(o => o.Status == s);
        if (from is { } f) q = q.Where(o => o.DueDate >= f);
        if (to is { } t) q = q.Where(o => o.DueDate <= t);
        return await q.OrderBy(o => o.DueDate).Take(500)
            .Select(o => new OccurrenceDto(o.Id, o.RecurringExpenseId, o.RecurringExpense!.Name, o.Amount, o.DueDate, o.Status, o.PaidAt, o.ExpenseId)).ToListAsync(ct);
    }

    public async Task<OccurrenceDto> MarkOccurrenceAsync(Guid id, bool paid, DateOnly? paidOn, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var o = await db.RecurringExpenseOccurrences.Include(x => x.RecurringExpense).FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException("Bill");
        if (o.Status == OccurrenceStatus.Paid) throw new ConflictException("This bill is already marked as paid.");
        if (paid)
        {
            var r = o.RecurringExpense!;
            var exp = new Expense { Name = r.Name, Amount = o.Amount, CategoryId = r.CategoryId, PaymentMethod = r.PaymentMethod, Date = paidOn ?? Today,
                Notes = $"Recurring bill due {o.DueDate:dd MMM yyyy}", RecurringOccurrenceId = o.Id };
            db.Expenses.Add(exp);
            o.Status = OccurrenceStatus.Paid; o.PaidAt = clock.GetUtcNow().UtcDateTime; o.ExpenseId = exp.Id;
        }
        else o.Status = OccurrenceStatus.Skipped;
        await db.SaveChangesAsync(ct);
        if (paid) await CheckBudgetAsync(paidOn ?? Today, ct);
        return new OccurrenceDto(o.Id, o.RecurringExpenseId, o.RecurringExpense!.Name, o.Amount, o.DueDate, o.Status, o.PaidAt, o.ExpenseId);
    }

    // ---------------------------------------------------------------- reports
    public async Task<ReportDto> GetReportAsync(string period, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        period = (period ?? "monthly").ToLowerInvariant();
        var today = Today;
        var (f, t) = (from, to) switch
        {
            ({ } a, { } b) => (a, b),
            _ => period switch
            {
                "daily" => (today.AddDays(-29), today),
                "weekly" => (today.AddDays(-7 * 11 - (int)today.DayOfWeek), today),
                "yearly" => (new DateOnly(today.Year - 4, 1, 1), today),
                _ => (new DateOnly(today.Year, today.Month, 1).AddMonths(-11), today)
            }
        };
        if (t < f) throw new ValidationAppException("to", "End date must be after the start date.");
        if (t.DayNumber - f.DayNumber > 3700) throw new ValidationAppException("from", "Range too large (max 10 years).");
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= f && e.Date <= t)
            .Select(e => new { e.Date, e.Amount, Cat = e.Category!.Name, e.Category.Color, e.CategoryId, e.PaymentMethod }).ToListAsync(ct);
        var income = await db.IncomeRecords.AsNoTracking().Where(e => e.Date >= f && e.Date <= t).Select(e => new { e.Date, e.Amount }).ToListAsync(ct);

        Func<DateOnly, string> key = period switch
        {
            "daily" => d => d.ToString("dd MMM"),
            "weekly" => d => "Wk " + d.AddDays(-(int)d.DayOfWeek).ToString("dd MMM"),
            "yearly" => d => d.Year.ToString(),
            _ => d => d.ToString("MMM yyyy")
        };
        var labels = new List<string>();
        for (var d = f; d <= t; d = d.AddDays(1)) { var k = key(d); if (labels.Count == 0 || labels[^1] != k) labels.Add(k); }
        var series = labels.Select(l => new SeriesPoint(l, income.Where(i => key(i.Date) == l).Sum(i => i.Amount), expenses.Where(e => key(e.Date) == l).Sum(e => e.Amount))).ToList();
        var totalExp = expenses.Sum(e => e.Amount);
        var cats = expenses.GroupBy(e => new { e.Cat, e.Color }).Select(g => new CategorySlice(g.Key.Cat, g.Key.Color, g.Sum(x => x.Amount), totalExp == 0 ? 0 : Math.Round(g.Sum(x => x.Amount) / totalExp * 100, 1)))
            .OrderByDescending(c => c.Amount).ToList();
        var pm = expenses.GroupBy(e => e.PaymentMethod).Select(g => new CategorySlice(g.Key.ToString(), null, g.Sum(x => x.Amount), totalExp == 0 ? 0 : Math.Round(g.Sum(x => x.Amount) / totalExp * 100, 1)))
            .OrderByDescending(c => c.Amount).ToList();
        var budget = await GetBudgetAsync(t.Year, t.Month, ct);
        var totalInc = income.Sum(i => i.Amount);
        return new ReportDto(period, f, t, totalInc, totalExp, totalInc - totalExp, series, cats, budget.Categories, pm);
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportReportAsync(string period, DateOnly? from, DateOnly? to, string format, CancellationToken ct)
    {
        var r = await GetReportAsync(period, from, to, ct);
        var rows = new List<IReadOnlyList<string?>>();
        rows.AddRange(r.Series.Select(s => (IReadOnlyList<string?>)[s.Label, s.Income.ToString("0.00", CultureInfo.InvariantCulture), s.Expense.ToString("0.00", CultureInfo.InvariantCulture), (s.Income - s.Expense).ToString("0.00", CultureInfo.InvariantCulture)]));
        rows.Add(["TOTAL", r.TotalIncome.ToString("0.00", CultureInfo.InvariantCulture), r.TotalExpense.ToString("0.00", CultureInfo.InvariantCulture), r.NetSavings.ToString("0.00", CultureInfo.InvariantCulture)]);
        rows.Add(["", "", "", ""]);
        rows.Add(["Category-wise expense", "Amount", "Share %", ""]);
        rows.AddRange(r.Categories.Select(c => (IReadOnlyList<string?>)[c.Category, c.Amount.ToString("0.00", CultureInfo.InvariantCulture), c.Percent.ToString("0.0", CultureInfo.InvariantCulture), ""]));
        var table = new TableExport($"Financial Report ({char.ToUpper(r.Period[0]) + r.Period[1..]})", ["Period", "Income (₹)", "Expense (₹)", "Net (₹)"], rows,
            $"{r.From:dd MMM yyyy} – {r.To:dd MMM yyyy}");
        return Render(table, format, $"financial-report-{r.Period}");
    }
}
