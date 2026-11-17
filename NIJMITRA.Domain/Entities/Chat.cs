using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class FriendInvitation : Entity
{
    public Guid InviterUserId { get; set; }
    public string Code { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int MaxUses { get; set; } = 1;
    public int UsedCount { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class FriendRequest : Entity
{
    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }
    public Guid ToUserId { get; set; }
    public User? ToUser { get; set; }
    public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}

/// <summary>Undirected friendship. UserAId is always the smaller GUID so the pair is unique.</summary>
public class FriendConnection : Entity
{
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public Guid ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChatConversation : Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    /// <summary>When set, new messages expire this many seconds after being sent.</summary>
    public int? DisappearingSeconds { get; set; }
    public ICollection<ChatParticipant> Participants { get; set; } = [];
}

public class ChatParticipant : Entity
{
    public Guid ConversationId { get; set; }
    public ChatConversation? Conversation { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastReadAt { get; set; }
    public DateTime? LastDeliveredAt { get; set; }
}

public class ChatMessage : Entity
{
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public ChatMessageType Type { get; set; } = ChatMessageType.Text;
    public string? Body { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public ICollection<ChatAttachment> Attachments { get; set; } = [];
}

public class ChatAttachment : Entity
{
    public Guid MessageId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
}

public class UserPresence
{
    public Guid UserId { get; set; }
    public bool IsOnline { get; set; }
    public int ConnectionCount { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
}

public class BlockedUser : Entity
{
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserReport : Entity
{
    public Guid ReporterId { get; set; }
    public Guid ReportedUserId { get; set; }
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
}
