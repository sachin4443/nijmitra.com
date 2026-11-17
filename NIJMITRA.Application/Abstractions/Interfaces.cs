using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Abstractions;

/// <summary>Identity of the caller, derived exclusively from validated server-side claims.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
    Guid? ImpersonatorId { get; }
    bool IsAuthenticated => UserId.HasValue;
    bool IsImpersonating => ImpersonatorId.HasValue;
    string? IpAddress { get; }
    string? UserAgent { get; }
    Guid RequireUserId() => UserId ?? throw new UnauthorizedAppException("Authentication is required.");
}

public interface IPasswordHasherService
{
    string Hash(string secret);
    bool Verify(string hash, string secret, out bool needsRehash);
}

public interface IFieldEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    /// <summary>Deterministic keyed hash (HMAC-SHA256) for lookups of secret values such as tokens.</summary>
    string Hash(string value);
}

public interface ITotpService
{
    string GenerateSecret();
    string BuildOtpAuthUri(string secret, string accountName, string issuer);
    string QrCodeDataUri(string content);
    /// <summary>Returns the matched time-step, or null when the code is invalid.</summary>
    long? Verify(string secret, string code, long lastUsedStep);
}

public record StoredBlob(string StorageKey, long Size, string Sha256);

/// <summary>Private file storage. Storage keys are opaque and never sent to clients.</summary>
public interface IFileStorage
{
    Task<StoredBlob> SaveAsync(Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);
}

public record ScanResult(bool Clean, string? Threat, bool Scanned);

public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<PlatformSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(PlatformSettings settings, Guid adminId, CancellationToken ct = default);
}

/// <summary>Pushes real-time events to connected browsers (SignalR).</summary>
public interface IRealtimeNotifier
{
    Task ToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default);
    Task ToUsersAsync(IEnumerable<Guid> userIds, string method, object payload, CancellationToken ct = default);
}

public record AuditEntry(string Event, string? EntityType = null, string? EntityId = null, string? Details = null, bool Success = true, Guid? UserId = null);

public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);
    Task LogLoginAsync(Guid? userId, string? identifier, string evt, bool success, string? reason, Guid? sessionId, CancellationToken ct = default);
    Task LogAdminAsync(Guid adminId, string action, Guid? targetUserId, string? details, CancellationToken ct = default);
}

public record ClientInfo(string? Browser, string? OperatingSystem, string? Device);

public interface IUserAgentParser
{
    ClientInfo Parse(string? userAgent);
}

public record NotificationRequest(
    string EventType,
    Guid? UserId,
    string Title,
    string? Body = null,
    string? Link = null,
    IDictionary<string, string>? Variables = null,
    string? DedupeKey = null,
    string? OverrideEmail = null,
    string? OverrideMobile = null,
    bool InAppOnly = false);

public record DispatchSummary(bool InAppCreated, IReadOnlyList<NotificationChannel> QueuedChannels, IReadOnlyList<string> SkippedReasons);

/// <summary>Central notification engine: applies policy → event config → user preference → mandatory rules.</summary>
public interface INotificationService
{
    Task<DispatchSummary> DispatchAsync(NotificationRequest request, CancellationToken ct = default);
}

public record TableExport(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string?>> Rows, string? Subtitle = null);

public interface IExportService
{
    byte[] ToExcel(TableExport table);
    byte[] ToCsv(TableExport table);
    byte[] ToPdf(TableExport table);
    IReadOnlyList<Dictionary<string, string?>> ReadTabular(Stream content, string extension);
}

public interface IHtmlSanitizerService
{
    string Sanitize(string html);
}

public record AccessTokenResult(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(Domain.Entities.User user, Domain.Entities.UserSession session, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
    string CreateOpaqueToken(int bytes = 48);
    string CreateChallengeToken(Guid userId, string purpose, string securityStamp, TimeSpan lifetime);
    (Guid UserId, string Stamp)? ValidateChallengeToken(string token, string purpose);
}
