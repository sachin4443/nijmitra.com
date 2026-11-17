using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class DiaryEntry : OwnedEntity
{
    public string Title { get; set; } = "";
    /// <summary>Sanitised rich-text HTML, encrypted at rest with AES-256-GCM.</summary>
    public string EncryptedContent { get; set; } = "";
    public DateOnly EntryDate { get; set; }
    public TimeOnly EntryTime { get; set; }
    public string? Mood { get; set; }
    public ICollection<DiaryTag> Tags { get; set; } = [];
    public ICollection<DiaryAttachment> Attachments { get; set; } = [];
}

public class DiaryTag : Entity
{
    public Guid EntryId { get; set; }
    public string Tag { get; set; } = "";
}

public class DiaryAttachment : Entity
{
    public Guid EntryId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public bool IsPhoto { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CalendarEvent : OwnedEntity
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public CalendarEventType EventType { get; set; } = CalendarEventType.Meeting;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool AllDay { get; set; }
    public string? Location { get; set; }
    public EventPriority Priority { get; set; } = EventPriority.Normal;
    /// <summary>Minutes before start at which reminders fire (0 = at event time).</summary>
    public List<int> ReminderOffsets { get; set; } = [];
    public EventRecurrence Recurrence { get; set; }
    public DateTime? RecurrenceUntilUtc { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? ContactId { get; set; }
    public ICollection<CalendarEventAttachment> Attachments { get; set; } = [];
}

public class CalendarEventAttachment : Entity
{
    public Guid EventId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Reminder : OwnedEntity
{
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public string Category { get; set; } = "General";
    public DateTime RemindAtUtc { get; set; }
    public EventRecurrence Repeat { get; set; }
    public bool IsCompleted { get; set; }
}

/// <summary>
/// One concrete delivery slot for a reminder. DedupeKey is unique, which guarantees that the
/// same reminder occurrence is never planned (and therefore never sent) twice.
/// </summary>
public class ReminderSchedule : Entity
{
    public Guid UserId { get; set; }
    public ReminderSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public string DedupeKey { get; set; } = "";
    public DateTime FireAtUtc { get; set; }
    public DateTime? OccurrenceUtc { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
