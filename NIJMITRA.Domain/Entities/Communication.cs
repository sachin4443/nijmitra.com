using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class Notification : Entity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public string EventType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class NotificationPreference : IOwnedEntity
{
    public Guid UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
    public bool MeetingReminders { get; set; } = true;
    public bool ExpenseReminders { get; set; } = true;
    public bool UdharReminders { get; set; } = true;
    public bool GeneralReminders { get; set; } = true;
    public bool FriendRequests { get; set; } = true;
    public bool ChatMessages { get; set; } = true;
    public bool LoginAlerts { get; set; } = true;
    public bool Marketing { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Platform-level control matrix: which channels an event may use.</summary>
public class NotificationPolicy
{
    public string EventType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    public bool PushEnabled { get; set; }
    public bool WhatsAppEnabled { get; set; }
    /// <summary>Security-critical events (OTP, recovery) cannot be fully disabled.</summary>
    public bool IsSecurityCritical { get; set; }
    public bool UserCanOptOut { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class NotificationTemplate : Entity
{
    public string EventType { get; set; } = "";
    public NotificationChannel Channel { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    /// <summary>Provider-side template id, e.g. DLT template id for Indian SMS.</summary>
    public string? ProviderTemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Outbox for external deliveries. Body is encrypted at rest and wiped after sending.</summary>
public class NotificationQueueItem : Entity
{
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public NotificationChannel Channel { get; set; }
    public string EncryptedRecipient { get; set; } = "";
    public string MaskedRecipient { get; set; } = "";
    public string? Subject { get; set; }
    public string? EncryptedBody { get; set; }
    public string? EncryptedVariables { get; set; }
    public string DedupeKey { get; set; } = "";
    public QueueStatus Status { get; set; } = QueueStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 4;
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Guid? ProviderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public class PushSubscription : Entity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
}

/// <summary>
/// An email / SMS / push / WhatsApp provider configured by an administrator.
/// Non-secret settings live in SettingsJson; secrets are AES-GCM encrypted in EncryptedSecretsJson
/// and are never returned to the browser.
/// </summary>
public class CommunicationProvider : AuditableEntity
{
    public NotificationChannel Channel { get; set; }
    public ProviderType ProviderType { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int Priority { get; set; } = 100;
    public string SettingsJson { get; set; } = "{}";
    public string? EncryptedSecretsJson { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestSucceeded { get; set; }
    public string? LastTestMessage { get; set; }
}

public class CommunicationLog : Entity
{
    public Guid? UserId { get; set; }
    public Guid? QueueItemId { get; set; }
    public NotificationChannel Channel { get; set; }
    public string EventType { get; set; } = "";
    public string MaskedRecipient { get; set; } = "";
    public string? ProviderName { get; set; }
    public QueueStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public int Attempt { get; set; }
    public bool WillRetry { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Announcement : Entity
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public AnnouncementTarget Target { get; set; }
    public string? TargetValue { get; set; }
    public bool SendInApp { get; set; } = true;
    public bool SendEmail { get; set; }
    public bool SendPush { get; set; }
    public bool SendSms { get; set; }
    public int RecipientCount { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
