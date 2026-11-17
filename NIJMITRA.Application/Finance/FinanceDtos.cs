using System.ComponentModel.DataAnnotations;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Finance;

public record CategoryDto(Guid Id, string Name, CategoryType Type, string? Icon, string? Color, bool IsDefault);
public record CategoryRequest([Required, StringLength(60, MinimumLength = 1)] string Name, string? Icon, string? Color, CategoryType Type = CategoryType.Expense);

public record ExpenseDto(Guid Id, string Name, decimal Amount, Guid CategoryId, string? CategoryName, string? CategoryColor, PaymentMethod PaymentMethod,
    DateOnly Date, string? Description, string? Notes, Guid? ReceiptFileId, Guid? ContactId, DateTime CreatedAt);
public record ExpenseRequest([Required, StringLength(150, MinimumLength = 1)] string Name, [Range(0.01, 999999999)] decimal Amount, Guid CategoryId,
    PaymentMethod PaymentMethod, DateOnly Date, [StringLength(1000)] string? Description, [StringLength(2000)] string? Notes, Guid? ReceiptFileId, Guid? ContactId);
public record ExpenseQuery(DateOnly? From, DateOnly? To, Guid? CategoryId, PaymentMethod? PaymentMethod, string? Search, int Page = 1, int PageSize = 20);

public record IncomeDto(Guid Id, string Title, string Source, decimal Amount, DateOnly Date, string? Description, string? Notes, Guid? ContactId, DateTime CreatedAt);
public record IncomeRequest([Required, StringLength(150, MinimumLength = 1)] string Title, [Required, StringLength(60)] string Source, [Range(0.01, 999999999)] decimal Amount,
    DateOnly Date, [StringLength(1000)] string? Description, [StringLength(2000)] string? Notes, Guid? ContactId);
public record IncomeQuery(DateOnly? From, DateOnly? To, string? Source, string? Search, int Page = 1, int PageSize = 20);

public record BudgetCategoryLine(Guid CategoryId, string CategoryName, string? Color, decimal Budget, decimal Spent, decimal Remaining, decimal UtilizationPercent, bool Overspent);
public record BudgetDto(Guid? Id, int Year, int Month, decimal TotalBudget, decimal TotalSpent, decimal Remaining, decimal UtilizationPercent, int AlertThresholdPercent,
    bool Overspent, IReadOnlyList<BudgetCategoryLine> Categories);
public record BudgetCategoryInput(Guid CategoryId, [Range(0, 999999999)] decimal Amount);
public record BudgetRequest([Range(2000, 2100)] int Year, [Range(1, 12)] int Month, [Range(0, 999999999)] decimal TotalAmount, [Range(50, 100)] int AlertThresholdPercent, List<BudgetCategoryInput> Categories);
public record MonthComparison(int Year, int Month, decimal Budget, decimal Spent, decimal Income);

public record RecurringDto(Guid Id, string Name, decimal Amount, Guid CategoryId, string? CategoryName, PaymentMethod PaymentMethod, RecurrenceFrequency Frequency,
    int? CustomIntervalDays, DateOnly StartDate, DateOnly? EndDate, int ReminderDaysBefore, bool IsActive, string? Notes, DateOnly? NextDueDate, int OverdueCount);
public record RecurringRequest([Required, StringLength(150, MinimumLength = 1)] string Name, [Range(0.01, 999999999)] decimal Amount, Guid CategoryId, PaymentMethod PaymentMethod,
    RecurrenceFrequency Frequency, [Range(1, 3650)] int? CustomIntervalDays, DateOnly StartDate, DateOnly? EndDate, [Range(0, 60)] int ReminderDaysBefore, bool IsActive, [StringLength(2000)] string? Notes);
public record OccurrenceDto(Guid Id, Guid RecurringExpenseId, string Name, decimal Amount, DateOnly DueDate, OccurrenceStatus Status, DateTime? PaidAt, Guid? ExpenseId);

public record SeriesPoint(string Label, decimal Income, decimal Expense);
public record CategorySlice(string Category, string? Color, decimal Amount, decimal Percent);
public record ReportDto(string Period, DateOnly From, DateOnly To, decimal TotalIncome, decimal TotalExpense, decimal NetSavings,
    IReadOnlyList<SeriesPoint> Series, IReadOnlyList<CategorySlice> Categories, IReadOnlyList<BudgetCategoryLine> BudgetVsActual, IReadOnlyList<CategorySlice> PaymentMethods);
