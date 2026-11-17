using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class ContactGroup : OwnedEntity
{
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public class Contact : OwnedEntity
{
    public string Name { get; set; } = "";
    public string? Mobile { get; set; }
    public string? AlternateMobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Company { get; set; }
    public string? Category { get; set; }
    public Guid? GroupId { get; set; }
    public ContactGroup? Group { get; set; }
    public string? Notes { get; set; }
    public Guid? AvatarFileId { get; set; }
    public ICollection<ContactAttachment> Attachments { get; set; } = [];
}

public class ContactAttachment : Entity
{
    public Guid ContactId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreditTransaction : OwnedEntity
{
    public CreditDirection Direction { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public string PersonName { get; set; } = "";
    public string? ContactNumber { get; set; }
    public decimal Amount { get; set; }
    public decimal SettledAmount { get; set; }
    public DateOnly TransactionDate { get; set; }
    public DateOnly? ExpectedReturnDate { get; set; }
    public DateTime? NextReminderAt { get; set; }
    public string? Description { get; set; }
    public CreditStatus Status { get; set; } = CreditStatus.Open;
    public ICollection<CreditPayment> Payments { get; set; } = [];
    public ICollection<CreditAttachment> Attachments { get; set; } = [];
    public decimal Outstanding => Amount - SettledAmount;
}

public class CreditPayment : Entity
{
    public Guid TransactionId { get; set; }
    public CreditTransaction? Transaction { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreditReminder : Entity
{
    public Guid TransactionId { get; set; }
    public Guid UserId { get; set; }
    public DateTime RemindAt { get; set; }
    public string? Note { get; set; }
    public DateTime? SentAt { get; set; }
}

public class CreditAttachment : Entity
{
    public Guid TransactionId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public DateTime CreatedAt { get; set; }
}
