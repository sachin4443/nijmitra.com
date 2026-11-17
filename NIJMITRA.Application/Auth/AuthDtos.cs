using System.ComponentModel.DataAnnotations;

namespace NIJMITRA.Application.Auth;

public record RegistrationConfigDto(bool AllowRegistration, bool RequireMobileOtp, bool EmailVerificationAvailable, bool EnforceTwoFactor, string ApplicationName);

public record SendOtpRequest([Required, StringLength(20, MinimumLength = 10)] string Mobile);

public record RegisterRequest(
    [Required, StringLength(150, MinimumLength = 2)] string FullName,
    [Required, StringLength(20, MinimumLength = 10)] string Mobile,
    string? MobileOtp,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    [Required] string ConfirmPassword,
    [Required, StringLength(6, MinimumLength = 4)] string Pin,
    bool AcceptTerms);

public record LoginRequest([Required] string Mobile, [Required] string Password, string? DeviceKey);
public record TwoFactorLoginRequest([Required] string ChallengeToken, [Required] string Code, bool IsRecoveryCode = false);
public record ChallengeRequest([Required] string ChallengeToken);
public record ChallengeEnableRequest([Required] string ChallengeToken, [Required] string Code);

public record AuthTokens(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, Guid SessionId);

public record LoginResult(string Status, string? ChallengeToken = null, AuthTokens? Tokens = null, IReadOnlyList<string>? RecoveryCodes = null, string? Message = null)
{
    public const string Success = "success";
    public const string TwoFactorRequired = "two_factor_required";
    public const string TwoFactorSetupRequired = "two_factor_setup_required";
}

public record TwoFactorSetupDto(string Secret, string QrCodeDataUri, string OtpAuthUri);
public record CodeRequest([Required] string Code);
public record DisableTwoFactorRequest([Required] string Password, [Required] string Code);
public record RecoveryCodesDto(IReadOnlyList<string> Codes);

public record ChangePasswordRequest([Required] string CurrentPassword, [Required, StringLength(128, MinimumLength = 8)] string NewPassword, [Required] string ConfirmPassword);
public record ChangePinRequest([Required] string CurrentPassword, [Required, StringLength(6, MinimumLength = 4)] string NewPin);
public record ReauthRequest(string? Pin, string? Password);
public record PasswordResetRequest([Required] string Mobile);
public record PasswordResetConfirm([Required] string Mobile, [Required] string Otp, [Required, StringLength(128, MinimumLength = 8)] string NewPassword, [Required] string ConfirmPassword);
public record EmailUpdateRequest([Required, EmailAddress, StringLength(256)] string Email);
public record EmailVerifyRequest([Required] string Code);

public record MeDto(
    Guid Id, string PublicId, string FullName, string MobileNumber, string? Email, bool EmailVerified, string? PendingEmail,
    bool TwoFactorEnabled, bool HasPin, bool MustChangePassword, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions,
    bool IsImpersonated, string? ImpersonatorName, ProfileDto Profile, SessionPolicyDto SessionPolicy);

public record ProfileDto(Guid? AvatarFileId, string? Address, DateOnly? DateOfBirth, string? City, string? State, string? Country,
    string TimeZone, string Language, string Theme);

public record SessionPolicyDto(int IdleTimeoutSeconds, int WarningSeconds);

public record SessionDto(Guid Id, DateTime CreatedAt, DateTime LastActivityAt, string? IpAddress, string? Browser, string? OperatingSystem, string? DeviceName, bool IsCurrent, bool IsImpersonation);
public record DeviceDto(Guid Id, string? Name, string? Browser, string? OperatingSystem, string? DeviceType, DateTime FirstSeenAt, DateTime LastSeenAt, string? LastIpAddress);
public record LoginHistoryDto(Guid Id, string Event, bool Success, string? FailureReason, string? IpAddress, string? Browser, string? OperatingSystem, string? Device, DateTime Timestamp);
