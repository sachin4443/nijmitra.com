using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Auth;

public sealed class AuthService(
    IAppDbContext db, IPasswordHasherService hasher, ITokenService tokens, ITotpService totp, IFieldEncryptor enc,
    ISettingsService settings, INotificationService notifications, IAuditService audit, ICurrentUser current,
    IUserAgentParser uaParser, IOptions<AppOptions> app, TimeProvider clock)
{
    private const string MfaPurpose = "mfa-login";
    private const string MfaSetupPurpose = "mfa-setup";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ registration
    public async Task<RegistrationConfigDto> GetRegistrationConfigAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var smsActive = await ChannelActiveAsync(NotificationChannel.Sms, ct);
        var emailActive = await ChannelActiveAsync(NotificationChannel.Email, ct);
        return new RegistrationConfigDto(s.AllowRegistration, s.RequireMobileOtp && smsActive, emailActive, s.EnforceTwoFactor, s.ApplicationName);
    }

    public async Task SendRegistrationOtpAsync(string mobileInput, CancellationToken ct)
    {
        var mobile = ValidateMobile(mobileInput);
        if (!await ChannelActiveAsync(NotificationChannel.Sms, ct))
            throw new ValidationAppException("mobile", "SMS service is not configured yet. Mobile OTP is currently not required.");
        if (await db.Users.AnyAsync(u => u.MobileNumber == mobile, ct))
            throw new ConflictException("An account with this mobile number already exists.");
        await IssueOtpAsync(OtpPurpose.RegistrationMobile, mobile, null, NotificationEvents.RegistrationOtp, ct);
    }

    public async Task<Guid> RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (!s.AllowRegistration) throw new ForbiddenException("New registrations are currently closed.");
        if (!r.AcceptTerms) throw new ValidationAppException("acceptTerms", "You must accept the Terms of Use and Privacy Policy.");
        var mobile = ValidateMobile(r.Mobile);
        ValidatePassword(r.Password, r.ConfirmPassword);
        ValidatePin(r.Pin);
        if (await db.Users.AnyAsync(u => u.MobileNumber == mobile, ct)) throw new ConflictException("An account with this mobile number already exists.");

        var mobileVerified = false;
        if (s.RequireMobileOtp && await ChannelActiveAsync(NotificationChannel.Sms, ct))
        {
            await ConsumeOtpAsync(OtpPurpose.RegistrationMobile, mobile, r.MobileOtp, ct);
            mobileVerified = true;
        }

        var seq = await NextSequenceAsync(ct);
        var user = new User
        {
            SequenceNumber = seq, PublicId = $"NM{seq}", FullName = r.FullName.Trim(), MobileNumber = mobile, MobileVerified = mobileVerified,
            PasswordHash = hasher.Hash(r.Password), PinHash = hasher.Hash(r.Pin), PasswordChangedAt = Now
        };
        db.Users.Add(user);
        UserBootstrapper.Seed(db, user, Now);
        await db.SaveChangesAsync(ct);
        await UserBootstrapper.EnsureBootstrapAdminAsync(db, user, app.Value.BootstrapSuperAdminMobiles, ct);
        await audit.LogAsync(new AuditEntry("registration", "User", user.Id.ToString(), UserId: user.Id), ct);
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.Welcome, user.Id, $"Welcome to {s.ApplicationName}!",
            "Your account is ready. Secure it further by enabling two-step verification from Settings → Security.", "/settings/security",
            DedupeKey: $"welcome:{user.Id}"), ct);
        return user.Id;
    }

    private async Task<long> NextSequenceAsync(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>("SELECT nextval('user_public_seq') AS \"Value\"").SingleAsync(ct);

    // ------------------------------------------------------------------ login
    public async Task<LoginResult> LoginAsync(LoginRequest r, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var mobile = TextUtil.NormalizeMobile(r.Mobile ?? "");
        var user = await db.Users.FirstOrDefaultAsync(u => u.MobileNumber == mobile, ct);
        if (user == null)
        {
            hasher.Verify("AQAAAAIAAz6AAAAAEGpL0z1Xk0w6kq3wW1sAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==", r.Password ?? "", out _); // timing equalisation
            await audit.LogLoginAsync(null, mobile, "login", false, "unknown_account", null, ct);
            throw new UnauthorizedAppException("Invalid mobile number or password.", "invalid_credentials");
        }
        await EnsureCanSignInAsync(user, s, ct);
        if (!hasher.Verify(user.PasswordHash, r.Password ?? "", out var rehash))
        {
            await RegisterFailureAsync(user, s, "invalid_password", ct);
            throw new UnauthorizedAppException("Invalid mobile number or password.", "invalid_credentials");
        }
        if (rehash) user.PasswordHash = hasher.Hash(r.Password!);
        if (s.MaintenanceMode && !await IsAdminAsync(user.Id, ct)) throw new ForbiddenException(s.MaintenanceMessage, "maintenance");
        await UserBootstrapper.EnsureBootstrapAdminAsync(db, user, app.Value.BootstrapSuperAdminMobiles, ct);

        if (user.TwoFactorEnabled)
        {
            user.FailedLoginCount = 0;
            await db.SaveChangesAsync(ct);
            await audit.LogLoginAsync(user.Id, mobile, "login_password_ok", true, null, null, ct);
            return new LoginResult(LoginResult.TwoFactorRequired, tokens.CreateChallengeToken(user.Id, MfaPurpose, user.SecurityStamp, TimeSpan.FromMinutes(5)));
        }
        if (s.EnforceTwoFactor)
        {
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginResult.TwoFactorSetupRequired, tokens.CreateChallengeToken(user.Id, MfaSetupPurpose, user.SecurityStamp, TimeSpan.FromMinutes(10)),
                Message: "Your administrator requires two-step verification. Please set up an authenticator app to continue.");
        }
        return new LoginResult(LoginResult.Success, Tokens: await CompleteSignInAsync(user, r.DeviceKey, null, null, ct));
    }

    public async Task<LoginResult> VerifyTwoFactorLoginAsync(TwoFactorLoginRequest r, string? deviceKey, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var user = await UserFromChallengeAsync(r.ChallengeToken, MfaPurpose, ct);
        await EnsureCanSignInAsync(user, s, ct);
        var ok = r.IsRecoveryCode ? await UseRecoveryCodeAsync(user.Id, r.Code, ct) : await VerifyTotpAsync(user.Id, r.Code, ct);
        if (!ok)
        {
            await RegisterFailureAsync(user, s, r.IsRecoveryCode ? "invalid_recovery_code" : "invalid_totp", ct);
            throw new UnauthorizedAppException("The verification code is invalid or has expired.", "invalid_code");
        }
        if (r.IsRecoveryCode)
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, user.Id, "Recovery code used",
                "A recovery code was used to sign in to your account. Generate new codes from Settings → Security if this was not you."), ct);
        return new LoginResult(LoginResult.Success, Tokens: await CompleteSignInAsync(user, deviceKey, null, null, ct));
    }

    public async Task<TwoFactorSetupDto> ChallengeSetupAsync(string challengeToken, CancellationToken ct)
    {
        var user = await UserFromChallengeAsync(challengeToken, MfaSetupPurpose, ct);
        return await CreateTotpSetupAsync(user, ct);
    }

    public async Task<LoginResult> ChallengeEnableAsync(ChallengeEnableRequest r, string? deviceKey, CancellationToken ct)
    {
        var user = await UserFromChallengeAsync(r.ChallengeToken, MfaSetupPurpose, ct);
        var codes = await ConfirmTotpAsync(user, r.Code, ct);
        return new LoginResult(LoginResult.Success, Tokens: await CompleteSignInAsync(user, deviceKey, null, null, ct), RecoveryCodes: codes);
    }

    /// <summary>Creates a session + refresh token. Also used for admin impersonation (impersonatorId set).</summary>
    public async Task<AuthTokens> CompleteSignInAsync(User user, string? deviceKey, Guid? impersonatorId, Guid? parentSessionId, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var info = uaParser.Parse(current.UserAgent);
        UserDevice? device = null;
        if (impersonatorId == null && !string.IsNullOrWhiteSpace(deviceKey) && deviceKey.Length <= 100)
        {
            device = await db.UserDevices.FirstOrDefaultAsync(d => d.UserId == user.Id && d.DeviceKey == deviceKey, ct);
            var isNew = device == null;
            device ??= new UserDevice { UserId = user.Id, DeviceKey = deviceKey, FirstSeenAt = Now };
            device.LastSeenAt = Now; device.Browser = info.Browser; device.OperatingSystem = info.OperatingSystem;
            device.DeviceType = info.Device; device.LastIpAddress = current.IpAddress;
            device.Name ??= $"{info.Browser ?? "Browser"} on {info.OperatingSystem ?? "Unknown OS"}";
            if (isNew) db.UserDevices.Add(device);
        }
        var session = new UserSession
        {
            UserId = user.Id, DeviceId = device?.Id, CreatedAt = Now, LastActivityAt = Now,
            AbsoluteExpiresAt = Now.AddHours(Math.Clamp(s.SessionAbsoluteHours, 1, 720)), IpAddress = current.IpAddress,
            UserAgent = current.UserAgent is { Length: > 500 } ua ? ua[..500] : current.UserAgent,
            Browser = info.Browser, OperatingSystem = info.OperatingSystem, DeviceName = info.Device,
            ImpersonatedByUserId = impersonatorId, ParentSessionId = parentSessionId
        };
        db.UserSessions.Add(session);
        var refresh = tokens.CreateOpaqueToken();
        db.RefreshTokens.Add(new RefreshToken { SessionId = session.Id, TokenHash = enc.Hash(refresh), CreatedAt = Now, ExpiresAt = session.AbsoluteExpiresAt });
        if (impersonatorId == null)
        {
            user.LastLoginAt = Now; user.FailedLoginCount = 0; user.LockoutEndUtc = null;
        }
        await db.SaveChangesAsync(ct);
        var (roles, perms) = await RolesAndPermissionsAsync(user.Id, ct);
        var access = tokens.CreateAccessToken(user, session, roles, perms);

        if (impersonatorId == null)
        {
            await audit.LogLoginAsync(user.Id, user.MobileNumber, "login_success", true, null, session.Id, ct);
            var sec = await db.UserSecuritySettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
            if (sec?.LoginAlertsEnabled != false)
                await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.LoginAlert, user.Id, "New sign-in to your account",
                    $"Signed in from {info.Browser ?? "a browser"} on {info.OperatingSystem ?? "unknown OS"} (IP {current.IpAddress ?? "unknown"}) at {Now:dd MMM yyyy HH:mm} UTC.",
                    "/settings/security", new Dictionary<string, string> { ["Browser"] = info.Browser ?? "", ["IpAddress"] = current.IpAddress ?? "", ["EventDate"] = Now.ToString("dd MMM yyyy"), ["EventTime"] = Now.ToString("HH:mm") + " UTC" },
                    DedupeKey: $"login:{session.Id}"), ct);
        }
        return new AuthTokens(access.Token, access.ExpiresAt, refresh, session.AbsoluteExpiresAt, session.Id);
    }

    public async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> RolesAndPermissionsAsync(Guid userId, CancellationToken ct)
    {
        var roles = await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.Role!.Name).ToListAsync(ct);
        var perms = await db.UserRoles.Where(r => r.UserId == userId)
            .SelectMany(r => r.Role!.RolePermissions.Select(p => p.Permission!.Key)).Distinct().ToListAsync(ct);
        return (roles, perms);
    }

    // ------------------------------------------------------------------ refresh / logout
    public async Task<AuthTokens> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new UnauthorizedAppException("Session has ended. Please sign in again.", "session_expired");
        var hash = enc.Hash(refreshToken);
        var token = await db.RefreshTokens.Include(t => t.Session).ThenInclude(s => s!.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                    ?? throw new UnauthorizedAppException("Session has ended. Please sign in again.", "session_expired");
        var session = token.Session!;
        if (token.RevokedAt != null)
        {
            // Reuse of a rotated token ⇒ likely theft. Kill the whole session.
            if (session.RevokedAt == null) { session.RevokedAt = Now; session.RevokedReason = "refresh_token_reuse"; await db.SaveChangesAsync(ct); }
            await audit.LogLoginAsync(session.UserId, null, "refresh_token_reuse", false, "token_reuse_detected", session.Id, ct);
            throw new UnauthorizedAppException("Session has ended. Please sign in again.", "session_expired");
        }
        var s = await settings.GetAsync(ct);
        if (!session.IsActive(Now) || token.ExpiresAt <= Now)
            throw new UnauthorizedAppException("Session has ended. Please sign in again.", "session_expired");
        if ((Now - session.LastActivityAt).TotalSeconds > s.SessionIdleTimeoutSeconds + 5)
        {
            await ExpireSessionAsync(session, "idle_timeout", ct);
            throw new UnauthorizedAppException("You were signed out after a period of inactivity.", "session_expired");
        }
        var user = session.User!;
        if (user.Status != UserStatus.Active)
        {
            await ExpireSessionAsync(session, "account_" + user.Status.ToString().ToLowerInvariant(), ct);
            throw new UnauthorizedAppException("Your account is not active.", "account_inactive");
        }
        token.RevokedAt = Now;
        var next = tokens.CreateOpaqueToken();
        var nextEntity = new RefreshToken { SessionId = session.Id, TokenHash = enc.Hash(next), CreatedAt = Now, ExpiresAt = session.AbsoluteExpiresAt };
        token.ReplacedByTokenId = nextEntity.Id;
        db.RefreshTokens.Add(nextEntity);
        await db.SaveChangesAsync(ct);
        var (roles, perms) = await RolesAndPermissionsAsync(user.Id, ct);
        var access = tokens.CreateAccessToken(user, session, roles, perms);
        return new AuthTokens(access.Token, access.ExpiresAt, next, session.AbsoluteExpiresAt, session.Id);
    }

    public async Task LogoutAsync(Guid? sessionId, string? refreshToken, string reason, CancellationToken ct)
    {
        UserSession? session = null;
        if (sessionId is { } sid) session = await db.UserSessions.FirstOrDefaultAsync(x => x.Id == sid, ct);
        else if (!string.IsNullOrEmpty(refreshToken))
        {
            var hash = enc.Hash(refreshToken);
            session = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => t.Session).FirstOrDefaultAsync(ct);
        }
        if (session == null || session.RevokedAt != null) return;
        await ExpireSessionAsync(session, reason, ct);
    }

    public async Task ExpireSessionAsync(UserSession session, string reason, CancellationToken ct)
    {
        session.RevokedAt = Now; session.RevokedReason = reason;
        await db.RefreshTokens.Where(t => t.SessionId == session.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, Now), ct);
        await db.SaveChangesAsync(ct);
        var evt = reason switch { "idle_timeout" => "auto_logout", "user_logout" => "logout", _ => "session_revoked" };
        await audit.LogLoginAsync(session.UserId, null, evt, true, reason, session.Id, ct);
    }

    // ------------------------------------------------------------------ sessions & devices
    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        return await db.UserSessions.AsNoTracking().Where(x => x.UserId == uid && x.RevokedAt == null && x.AbsoluteExpiresAt > Now)
            .OrderByDescending(x => x.LastActivityAt)
            .Select(x => new SessionDto(x.Id, x.CreatedAt, x.LastActivityAt, x.IpAddress, x.Browser, x.OperatingSystem, x.DeviceName, x.Id == current.SessionId, x.ImpersonatedByUserId != null))
            .ToListAsync(ct);
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var session = await db.UserSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == uid, ct) ?? throw new NotFoundException("Session");
        await ExpireSessionAsync(session, "revoked_by_user", ct);
    }

    public async Task<int> RevokeOtherSessionsAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var others = await db.UserSessions.Where(x => x.UserId == uid && x.RevokedAt == null && x.Id != current.SessionId).ToListAsync(ct);
        foreach (var s in others) await ExpireSessionAsync(s, "logout_other_devices", ct);
        await audit.LogAsync(new AuditEntry("logout_other_devices", Details: $"{others.Count} session(s) revoked"), ct);
        return others.Count;
    }

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        return await db.UserDevices.AsNoTracking().Where(d => d.UserId == uid).OrderByDescending(d => d.LastSeenAt)
            .Select(d => new DeviceDto(d.Id, d.Name, d.Browser, d.OperatingSystem, d.DeviceType, d.FirstSeenAt, d.LastSeenAt, d.LastIpAddress)).ToListAsync(ct);
    }

    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == uid, ct) ?? throw new NotFoundException("Device");
        var sessions = await db.UserSessions.Where(s => s.DeviceId == deviceId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions) await ExpireSessionAsync(s, "device_removed", ct);
        db.UserDevices.Remove(device);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<LoginHistoryDto>> GetLoginHistoryAsync(Guid userId, int page, int pageSize, CancellationToken ct) =>
        await db.LoginAuditLogs.AsNoTracking().Where(l => l.UserId == userId).OrderByDescending(l => l.Timestamp)
            .Select(l => new LoginHistoryDto(l.Id, l.Event, l.Success, l.FailureReason, l.IpAddress, l.Browser, l.OperatingSystem, l.Device, l.Timestamp))
            .ToPagedAsync(page, pageSize, ct);

    // ------------------------------------------------------------------ two-factor management
    public async Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (user.TwoFactorEnabled) throw new ConflictException("Two-step verification is already enabled.");
        return await CreateTotpSetupAsync(user, ct);
    }

    public async Task<IReadOnlyList<string>> EnableTwoFactorAsync(string code, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (user.TwoFactorEnabled) throw new ConflictException("Two-step verification is already enabled.");
        return await ConfirmTotpAsync(user, code, ct);
    }

    public async Task DisableTwoFactorAsync(DisableTwoFactorRequest r, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (s.EnforceTwoFactor) throw new ForbiddenException("Your administrator requires two-step verification for all accounts.");
        var user = await CurrentUserEntityAsync(ct);
        if (!user.TwoFactorEnabled) return;
        if (!hasher.Verify(user.PasswordHash, r.Password, out _)) throw new ValidationAppException("password", "Password is incorrect.");
        if (!await VerifyTotpAsync(user.Id, r.Code, ct)) throw new ValidationAppException("code", "Authenticator code is invalid.");
        user.TwoFactorEnabled = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.UserAuthenticatorSecrets.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.UserRecoveryCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("authenticator_disabled"), ct);
        await SecurityAlertAsync(user.Id, "Two-step verification disabled", "Two-step verification was turned off for your account.", ct);
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(string code, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (!user.TwoFactorEnabled) throw new ValidationAppException("Two-step verification is not enabled.");
        if (!await VerifyTotpAsync(user.Id, code, ct)) throw new ValidationAppException("code", "Authenticator code is invalid.");
        var codes = await IssueRecoveryCodesAsync(user.Id, ct);
        await audit.LogAsync(new AuditEntry("recovery_codes_regenerated"), ct);
        return codes;
    }

    private async Task<TwoFactorSetupDto> CreateTotpSetupAsync(User user, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        await db.UserAuthenticatorSecrets.Where(x => x.UserId == user.Id && !x.IsConfirmed).ExecuteDeleteAsync(ct);
        var secret = totp.GenerateSecret();
        db.UserAuthenticatorSecrets.Add(new UserAuthenticatorSecret { UserId = user.Id, EncryptedSecret = enc.Encrypt(secret), CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        var uri = totp.BuildOtpAuthUri(secret, $"{user.PublicId} ({TextUtil.MaskMobile(user.MobileNumber)})", s.ApplicationName);
        return new TwoFactorSetupDto(string.Join(" ", Enumerable.Range(0, secret.Length / 4).Select(i => secret.Substring(i * 4, 4))), totp.QrCodeDataUri(uri), uri);
    }

    private async Task<IReadOnlyList<string>> ConfirmTotpAsync(User user, string code, CancellationToken ct)
    {
        var pending = await db.UserAuthenticatorSecrets.Where(x => x.UserId == user.Id && !x.IsConfirmed).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct)
                      ?? throw new ValidationAppException("code", "Start the authenticator setup first.");
        if (Now - pending.CreatedAt > TimeSpan.FromMinutes(30)) throw new ValidationAppException("code", "Setup expired. Please scan a new QR code.");
        var step = totp.Verify(enc.Decrypt(pending.EncryptedSecret), code, 0) ?? throw new ValidationAppException("code", "The code is incorrect. Check your phone's time and try again.");
        await db.UserAuthenticatorSecrets.Where(x => x.UserId == user.Id && x.IsConfirmed).ExecuteDeleteAsync(ct);
        pending.IsConfirmed = true; pending.ConfirmedAt = Now; pending.LastUsedTimeStep = step;
        user.TwoFactorEnabled = true;
        var sec = await db.UserSecuritySettings.FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (sec != null) { sec.TwoFactorEnabledAt = Now; sec.UpdatedAt = Now; }
        await db.SaveChangesAsync(ct);
        var codes = await IssueRecoveryCodesAsync(user.Id, ct);
        await audit.LogAsync(new AuditEntry("authenticator_enabled", UserId: user.Id), ct);
        await SecurityAlertAsync(user.Id, "Two-step verification enabled", "Your account is now protected with an authenticator app.", ct);
        return codes;
    }

    private async Task<IReadOnlyList<string>> IssueRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        await db.UserRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codes = Enumerable.Range(0, 10).Select(_ =>
        {
            var chars = RandomNumberGenerator.GetItems<char>(alphabet, 10);
            return new string(chars, 0, 5) + "-" + new string(chars, 5, 5);
        }).ToList();
        foreach (var c in codes) db.UserRecoveryCodes.Add(new UserRecoveryCode { UserId = userId, CodeHash = enc.Hash(c), CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        return codes;
    }

    private async Task<bool> VerifyTotpAsync(Guid userId, string code, CancellationToken ct)
    {
        var secret = await db.UserAuthenticatorSecrets.FirstOrDefaultAsync(x => x.UserId == userId && x.IsConfirmed, ct);
        if (secret == null) return false;
        var step = totp.Verify(enc.Decrypt(secret.EncryptedSecret), code, secret.LastUsedTimeStep);
        if (step == null) return false;
        secret.LastUsedTimeStep = step.Value;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> UseRecoveryCodeAsync(Guid userId, string code, CancellationToken ct)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (normalized.Length == 10) normalized = normalized[..5] + "-" + normalized[5..];
        var hash = enc.Hash(normalized);
        var rc = await db.UserRecoveryCodes.FirstOrDefaultAsync(x => x.UserId == userId && x.CodeHash == hash && x.UsedAt == null, ct);
        if (rc == null) return false;
        rc.UsedAt = Now;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("recovery_code_used", UserId: userId), ct);
        return true;
    }

    // ------------------------------------------------------------------ password / PIN / reauth
    public async Task ChangePasswordAsync(ChangePasswordRequest r, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (!hasher.Verify(user.PasswordHash, r.CurrentPassword, out _))
        {
            await audit.LogAsync(new AuditEntry("password_change", Success: false, Details: "incorrect current password"), ct);
            throw new ValidationAppException("currentPassword", "Current password is incorrect.");
        }
        ValidatePassword(r.NewPassword, r.ConfirmPassword);
        if (hasher.Verify(user.PasswordHash, r.NewPassword, out _)) throw new ValidationAppException("newPassword", "New password must be different from the current password.");
        user.PasswordHash = hasher.Hash(r.NewPassword);
        user.PasswordChangedAt = Now; user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await RevokeOtherSessionsAsync(ct);
        await audit.LogAsync(new AuditEntry("password_change"), ct);
        await SecurityAlertAsync(user.Id, "Password changed", "Your account password was changed and other devices were signed out.", ct);
    }

    public async Task ChangePinAsync(ChangePinRequest r, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (!hasher.Verify(user.PasswordHash, r.CurrentPassword, out _)) throw new ValidationAppException("currentPassword", "Password is incorrect.");
        ValidatePin(r.NewPin);
        user.PinHash = hasher.Hash(r.NewPin); user.FailedPinCount = 0;
        var sec = await db.UserSecuritySettings.FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (sec != null) { sec.PinChangedAt = Now; sec.UpdatedAt = Now; }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("pin_change"), ct);
        await SecurityAlertAsync(user.Id, "Security PIN changed", "Your personal security PIN was changed.", ct);
    }

    /// <summary>Confirms identity for sensitive operations (valid for 5 minutes on this session).</summary>
    public async Task ReauthenticateAsync(ReauthRequest r, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        if (user.LockoutEndUtc > Now) throw new ForbiddenException("Too many attempts. Please try again later.", "locked_out");
        bool ok;
        if (!string.IsNullOrEmpty(r.Pin)) ok = user.PinHash != null && hasher.Verify(user.PinHash, r.Pin, out _);
        else if (!string.IsNullOrEmpty(r.Password)) ok = hasher.Verify(user.PasswordHash, r.Password, out _);
        else throw new ValidationAppException("pin", "Enter your PIN or password.");
        if (!ok)
        {
            user.FailedPinCount++;
            if (user.FailedPinCount >= 5) { user.LockoutEndUtc = Now.AddMinutes(15); user.FailedPinCount = 0; }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(new AuditEntry("reauthentication", Success: false), ct);
            throw new ValidationAppException(string.IsNullOrEmpty(r.Pin) ? "password" : "pin", "Verification failed.");
        }
        user.FailedPinCount = 0;
        var session = await db.UserSessions.FirstAsync(x => x.Id == current.SessionId, ct);
        session.ReauthenticatedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task EnsureRecentlyReauthenticatedAsync(CancellationToken ct)
    {
        var sid = current.SessionId ?? throw new UnauthorizedAppException("Authentication is required.");
        var at = await db.UserSessions.Where(x => x.Id == sid).Select(x => x.ReauthenticatedAt).FirstOrDefaultAsync(ct);
        if (at == null || Now - at.Value > TimeSpan.FromMinutes(5)) throw new ReauthenticationRequiredException();
    }

    // ------------------------------------------------------------------ password reset
    public async Task<string> RequestPasswordResetAsync(string mobileInput, CancellationToken ct)
    {
        var mobile = TextUtil.NormalizeMobile(mobileInput);
        var smsActive = await ChannelActiveAsync(NotificationChannel.Sms, ct);
        var emailActive = await ChannelActiveAsync(NotificationChannel.Email, ct);
        if (!smsActive && !emailActive)
            throw new ValidationAppException("mobile", "Self-service password reset becomes available once the administrator configures SMS or email delivery. Please contact support to reset your password.");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.MobileNumber == mobile && u.Status == UserStatus.Active, ct);
        // Same response whether or not the account exists (no account enumeration).
        if (user != null && (smsActive || (emailActive && user.Email != null)))
            await IssueOtpAsync(OtpPurpose.PasswordReset, mobile, user.Id, NotificationEvents.PasswordReset, ct, userEmail: user.Email);
        return "If an account exists for this number, a verification code has been sent.";
    }

    public async Task ResetPasswordAsync(PasswordResetConfirm r, CancellationToken ct)
    {
        var mobile = TextUtil.NormalizeMobile(r.Mobile);
        ValidatePassword(r.NewPassword, r.ConfirmPassword);
        await ConsumeOtpAsync(OtpPurpose.PasswordReset, mobile, r.Otp, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.MobileNumber == mobile, ct) ?? throw new ValidationAppException("otp", "Invalid code.");
        user.PasswordHash = hasher.Hash(r.NewPassword);
        user.PasswordChangedAt = Now; user.FailedLoginCount = 0; user.LockoutEndUtc = null; user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        foreach (var s in await db.UserSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(ct))
            await ExpireSessionAsync(s, "password_reset", ct);
        await audit.LogAsync(new AuditEntry("password_reset", UserId: user.Id), ct);
        await SecurityAlertAsync(user.Id, "Password reset", "Your password was reset using a verification code. All devices were signed out.", ct);
    }

    // ------------------------------------------------------------------ email
    public async Task<string> UpdateEmailAsync(string emailInput, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        var email = emailInput.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id, ct)) throw new ConflictException("This email address is already used by another account.");
        var emailActive = await ChannelActiveAsync(NotificationChannel.Email, ct);
        if (!emailActive)
        {
            user.Email = email; user.EmailVerified = false; user.PendingEmail = null;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(new AuditEntry("email_change", Details: "saved unverified (email service not configured)"), ct);
            return "Email saved. Verification will start automatically once the email service is configured by the administrator.";
        }
        user.PendingEmail = email;
        await db.SaveChangesAsync(ct);
        await IssueOtpAsync(OtpPurpose.EmailVerification, email, user.Id, NotificationEvents.EmailVerification, ct, userEmail: email);
        await audit.LogAsync(new AuditEntry("email_change_requested"), ct);
        return "A verification code has been sent to your new email address.";
    }

    public async Task VerifyEmailAsync(string code, CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        var target = user.PendingEmail ?? user.Email ?? throw new ValidationAppException("code", "No email address is awaiting verification.");
        await ConsumeOtpAsync(OtpPurpose.EmailVerification, target, code, ct);
        if (await db.Users.AnyAsync(u => u.Email == target && u.Id != user.Id, ct)) throw new ConflictException("This email address is already used by another account.");
        var old = user.Email;
        user.Email = target; user.EmailVerified = true; user.PendingEmail = null;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("email_verified"), ct);
        if (old != null && old != target)
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, user.Id, "Email address changed",
                "Your account email address was changed.", OverrideEmail: old), ct);
    }

    public async Task<string> ResendEmailVerificationAsync(CancellationToken ct)
    {
        var user = await CurrentUserEntityAsync(ct);
        var target = user.PendingEmail ?? (user.EmailVerified ? null : user.Email) ?? throw new ValidationAppException("No email address is awaiting verification.");
        if (!await ChannelActiveAsync(NotificationChannel.Email, ct)) throw new ValidationAppException("Email service is not configured yet.");
        await IssueOtpAsync(OtpPurpose.EmailVerification, target, user.Id, NotificationEvents.EmailVerification, ct, userEmail: target);
        return "Verification code sent.";
    }

    // ------------------------------------------------------------------ helpers
    private async Task IssueOtpAsync(OtpPurpose purpose, string target, Guid? userId, string evt, CancellationToken ct, string? userEmail = null)
    {
        var recent = await db.OtpCodes.CountAsync(o => o.Target == target && o.Purpose == purpose && o.CreatedAt > Now.AddMinutes(-15), ct);
        if (recent >= 5) throw new ValidationAppException("Too many codes requested. Please wait 15 minutes.");
        var last = await db.OtpCodes.Where(o => o.Target == target && o.Purpose == purpose).OrderByDescending(o => o.CreatedAt).Select(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (last != default && Now - last < TimeSpan.FromSeconds(45)) throw new ValidationAppException("Please wait a few seconds before requesting another code.");
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = new OtpCode { UserId = userId, Purpose = purpose, Target = target, CodeHash = enc.Hash($"{purpose}:{target}:{code}"), CreatedAt = Now, ExpiresAt = Now.AddMinutes(10) };
        db.OtpCodes.Add(otp);
        await db.SaveChangesAsync(ct);
        var isEmail = purpose == OtpPurpose.EmailVerification;
        await notifications.DispatchAsync(new NotificationRequest(evt, isEmail ? null : userId,
            purpose switch { OtpPurpose.PasswordReset => "Password reset code", OtpPurpose.EmailVerification => "Verify your email address", _ => "Your verification code" },
            $"Your verification code is {code}. It expires in 10 minutes. Never share it with anyone.",
            Variables: new Dictionary<string, string> { ["OTP"] = code, ["ExpiryMinutes"] = "10" },
            DedupeKey: $"otp:{otp.Id}",
            OverrideEmail: isEmail ? target : userEmail,
            OverrideMobile: isEmail ? null : target), ct);
    }

    private async Task ConsumeOtpAsync(OtpPurpose purpose, string target, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationAppException("otp", "Verification code is required.");
        var otp = await db.OtpCodes.Where(o => o.Target == target && o.Purpose == purpose && o.ConsumedAt == null && o.ExpiresAt > Now)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new ValidationAppException("otp", "The code has expired. Request a new one.");
        if (otp.Attempts >= 5) throw new ValidationAppException("otp", "Too many incorrect attempts. Request a new code.");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.CodeHash), Convert.FromHexString(enc.Hash($"{purpose}:{target}:{code.Trim()}"))))
        {
            otp.Attempts++;
            await db.SaveChangesAsync(ct);
            throw new ValidationAppException("otp", "Incorrect verification code.");
        }
        otp.ConsumedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ChannelActiveAsync(NotificationChannel channel, CancellationToken ct) =>
        await db.CommunicationProviders.AnyAsync(p => p.Channel == channel && p.IsActive, ct);

    private async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.AnyAsync(r => r.UserId == userId, ct);

    private async Task<User> UserFromChallengeAsync(string token, string purpose, CancellationToken ct)
    {
        var parsed = tokens.ValidateChallengeToken(token, purpose) ?? throw new UnauthorizedAppException("Verification step expired. Please sign in again.", "challenge_expired");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == parsed.UserId, ct);
        if (user == null || user.SecurityStamp != parsed.Stamp) throw new UnauthorizedAppException("Verification step expired. Please sign in again.", "challenge_expired");
        return user;
    }

    private async Task EnsureCanSignInAsync(User user, PlatformSettings s, CancellationToken ct)
    {
        if (user.LockoutEndUtc > Now)
        {
            await audit.LogLoginAsync(user.Id, user.MobileNumber, "login", false, "locked_out", null, ct);
            var mins = (int)Math.Ceiling((user.LockoutEndUtc.Value - Now).TotalMinutes);
            throw new ForbiddenException($"Too many failed attempts. Your account is locked for {mins} more minute(s).", "locked_out");
        }
        if (user.Status != UserStatus.Active)
        {
            await audit.LogLoginAsync(user.Id, user.MobileNumber, "login", false, "status_" + user.Status, null, ct);
            throw new ForbiddenException(user.Status switch
            {
                UserStatus.Suspended => "Your account has been suspended. Please contact support.",
                UserStatus.PendingDeletion => "This account is scheduled for deletion. Contact support to restore it.",
                _ => "Your account is not active. Please contact support."
            }, "account_inactive");
        }
    }

    private async Task RegisterFailureAsync(User user, PlatformSettings s, string reason, CancellationToken ct)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= Math.Max(3, s.MaxLoginAttempts))
        {
            user.LockoutEndUtc = Now.AddMinutes(Math.Max(1, s.LockoutMinutes));
            user.FailedLoginCount = 0;
            await SecurityAlertAsync(user.Id, "Account temporarily locked", "Your account was locked after several failed sign-in attempts.", ct);
        }
        await db.SaveChangesAsync(ct);
        await audit.LogLoginAsync(user.Id, user.MobileNumber, "login", false, reason, null, ct);
    }

    private Task SecurityAlertAsync(Guid userId, string title, string body, CancellationToken ct) =>
        notifications.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, userId, title, body, "/settings/security"), ct);

    private async Task<User> CurrentUserEntityAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        return await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct) ?? throw new UnauthorizedAppException("Account not found.");
    }

    public static string ValidateMobile(string input)
    {
        var mobile = TextUtil.NormalizeMobile(input ?? "");
        var digits = mobile.TrimStart('+');
        if (digits.Length is < 10 or > 15 || !digits.All(char.IsDigit))
            throw new ValidationAppException("mobile", "Enter a valid mobile number.");
        if (mobile.StartsWith("+91") && (digits.Length != 12 || "6789".IndexOf(digits[2]) < 0))
            throw new ValidationAppException("mobile", "Enter a valid 10-digit Indian mobile number.");
        return mobile;
    }

    public static void ValidatePassword(string password, string confirm)
    {
        if (password != confirm) throw new ValidationAppException("confirmPassword", "Passwords do not match.");
        if (password.Length < 8 || password.Length > 128) throw new ValidationAppException("password", "Password must be 8–128 characters.");
        var classes = new[] { password.Any(char.IsUpper), password.Any(char.IsLower), password.Any(char.IsDigit), password.Any(c => !char.IsLetterOrDigit(c)) }.Count(x => x);
        if (classes < 3) throw new ValidationAppException("password", "Use at least three of: uppercase, lowercase, number, symbol.");
    }

    public static void ValidatePin(string pin)
    {
        if (pin is not { Length: >= 4 and <= 6 } || !pin.All(char.IsDigit)) throw new ValidationAppException("pin", "PIN must be 4 to 6 digits.");
        if (pin.Distinct().Count() == 1 || "0123456789".Contains(pin) || "9876543210".Contains(pin))
            throw new ValidationAppException("pin", "Choose a less predictable PIN.");
    }
}
