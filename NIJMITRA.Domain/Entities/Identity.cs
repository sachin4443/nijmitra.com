using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class User : AuditableEntity
{
    /// <summary>Monotonic number used to build the public NIJMITRA ID (e.g. NM100025).</summary>
    public long SequenceNumber { get; set; }
    public string PublicId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string MobileNumber { get; set; } = "";
    public bool MobileVerified { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? PendingEmail { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? PinHash { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? StatusReason { get; set; }
    public int FailedLoginCount { get; set; }
    public int FailedPinCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool MustChangePassword { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? PasswordChangedAt { get; set; }

    public UserProfile? Profile { get; set; }
    public UserSecuritySettings? SecuritySettings { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];
}

public class UserProfile
{
    public Guid UserId { get; set; }
    public Guid? AvatarFileId { get; set; }
    public string? Address { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; } = "India";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string Language { get; set; } = "en";
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public string? DashboardLayoutJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserSecuritySettings
{
    public Guid UserId { get; set; }
    public bool LoginAlertsEnabled { get; set; } = true;
    public int VaultAutoLockMinutes { get; set; } = 5;
    public DateTime? PinChangedAt { get; set; }
    public DateTime? TwoFactorEnabledAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserAuthenticatorSecret : Entity
{
    public Guid UserId { get; set; }
    /// <summary>TOTP secret encrypted with the server data-protection key (AES-256-GCM).</summary>
    public string EncryptedSecret { get; set; } = "";
    public bool IsConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>Last accepted TOTP time-step, used to block code replay.</summary>
    public long LastUsedTimeStep { get; set; }
}

public class UserRecoveryCode : Entity
{
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

public class UserSession : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime AbsoluteExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? ReauthenticatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceName { get; set; }
    /// <summary>Set when a Super Admin is signed in as this user.</summary>
    public Guid? ImpersonatedByUserId { get; set; }
    /// <summary>The administrator's own session that started an impersonation (kept alive alongside it).</summary>
    public Guid? ParentSessionId { get; set; }
    public bool IsActive(DateTime now) => RevokedAt == null && AbsoluteExpiresAt > now;
}

public class RefreshToken : Entity
{
    public Guid SessionId { get; set; }
    public UserSession? Session { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}

public class UserDevice : Entity
{
    public Guid UserId { get; set; }
    public string DeviceKey { get; set; } = "";
    public string? Name { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceType { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? LastIpAddress { get; set; }
}

public class LoginAuditLog : Entity
{
    public Guid? UserId { get; set; }
    public string? MaskedIdentifier { get; set; }
    public string Event { get; set; } = "";
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Device { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>General activity / security audit trail (never contains secrets).</summary>
public class AuditLog : Entity
{
    public Guid? UserId { get; set; }
    public string Event { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public bool Success { get; set; } = true;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Device { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ImpersonatedBy { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AdminAuditLog : Entity
{
    public Guid AdminUserId { get; set; }
    public string Action { get; set; } = "";
    public Guid? TargetUserId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}

public class OtpCode : Entity
{
    public Guid? UserId { get; set; }
    public OtpPurpose Purpose { get; set; }
    public string Target { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class Role : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

public class Permission : Entity
{
    public string Key { get; set; } = "";
    public string? Description { get; set; }
}

public class UserRole
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class SystemSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsSecret { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class AccountDeletionRequest : Entity
{
    public Guid UserId { get; set; }
    public string? Reason { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime ScheduledFor { get; set; }
    public DeletionRequestStatus Status { get; set; } = DeletionRequestStatus.Pending;
    public DateTime? CompletedAt { get; set; }
}

public class RecentSearch : Entity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public string Query { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>A physical file in private storage. StorageKey is never exposed to clients.</summary>
public class StoredFile : Entity, IOwnedEntity, ISoftDelete
{
    public Guid UserId { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
