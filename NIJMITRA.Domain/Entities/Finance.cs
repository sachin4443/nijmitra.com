using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class ExpenseCategory : OwnedEntity
{
    public string Name { get; set; } = "";
    public CategoryType Type { get; set; } = CategoryType.Expense;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsDefault { get; set; }
}

public class Expense : OwnedEntity
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public Guid? ReceiptFileId { get; set; }
    public Guid? RecurringOccurrenceId { get; set; }
    public Guid? ContactId { get; set; }
}

public class IncomeRecord : OwnedEntity
{
    public string Title { get; set; } = "";
    public string Source { get; set; } = "Salary";
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public Guid? ContactId { get; set; }
}

public class MonthlyBudget : OwnedEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalAmount { get; set; }
    public int AlertThresholdPercent { get; set; } = 90;
    public DateTime? LastAlertSentAt { get; set; }
    public ICollection<BudgetCategory> Categories { get; set; } = [];
}

public class BudgetCategory : Entity
{
    public Guid MonthlyBudgetId { get; set; }
    public MonthlyBudget? MonthlyBudget { get; set; }
    public Guid CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public decimal Amount { get; set; }
}

public class RecurringExpense : OwnedEntity
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid CategoryId { get; set; }
    public ExpenseCategory? Category { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public int? CustomIntervalDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int ReminderDaysBefore { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<RecurringExpenseOccurrence> Occurrences { get; set; } = [];
}

public class RecurringExpenseOccurrence : Entity
{
    public Guid RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }
    public Guid UserId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public OccurrenceStatus Status { get; set; } = OccurrenceStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public Guid? ExpenseId { get; set; }
}
