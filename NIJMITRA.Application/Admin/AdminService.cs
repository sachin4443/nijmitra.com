using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Admin;

public record CountPoint(string Label, int Success, int Failed);
public record ChannelStat(string Channel, int Sent, int Failed, int Pending);
public record AdminDashboardDto(int TotalUsers, int ActiveUsers, int InactiveUsers, int SuspendedUsers, int PendingDeletion, int NewRegistrations7d, int NewRegistrations30d,
    int TwoFactorUsers, int OnlineNow, IReadOnlyList<CountPoint> LoginActivity, long StorageBytes, int StoredFiles, int NotificationsToday,
    IReadOnlyList<ChannelStat> Delivery, int QueueBacklog, int DeadLetters, bool DatabaseHealthy, string ServerTimeUtc, int OpenReports);
public record AdminUserRow(Guid Id, string PublicId, string FullName, string MobileNumber, string? Email, UserStatus Status, bool TwoFactorEnabled,
    bool IsLocked, IReadOnlyList<string> Roles, DateTime CreatedAt, DateTime? LastLoginAt);
public record AdminUserQuery(string? Search, UserStatus? Status, string? Role, bool? TwoFactor, int Page = 1, int PageSize = 25);
public record AdminUserDetail(AdminUserRow User, string? City, string? State, string? Country, bool MobileVerified, bool EmailVerified, string? StatusReason,
    int FailedLoginCount, DateTime? LockoutEndUtc, DateTime? PasswordChangedAt, int ActiveSessions, int Devices, long StorageBytes, int FileCount,
    IReadOnlyDictionary<string, int> RecordCounts);
public record StatusChangeRequest(UserStatus Status, [StringLength(500)] string? Reason, bool NotifyUser = true);
public record RolesRequest(List<string> Roles);
public record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);
public record RoleRequest([Required, StringLength(50, MinimumLength = 2)] string Name, [StringLength(200)] string? Description, List<string> Permissions);
public record PermissionDto(string Key, string? Description);
public record AuditRow(Guid Id, DateTime Timestamp, Guid? UserId, string? UserName, string Event, string? EntityType, string? Details, bool Success,
    string? IpAddress, string? Browser, string? OperatingSystem, string? Device, Guid? SessionId);
public record AuditQuery(string Kind, Guid? UserId, string? Event, bool? Success, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50);
public record ReportRow(Guid Id, string ReporterName, string ReportedName, Guid ReportedUserId, string Reason, string? Details, ReportStatus Status, DateTime CreatedAt);

/// <summary>
/// Administration. By design no admin endpoint returns vault contents, diary text, chat messages or document files —
/// only account metadata, counts and security status.
/// </summary>
public sealed class AdminService(IAppDbContext db, ICurrentUser current, IAuditService audit, IPasswordHasherService hasher, INotificationService notifications,
    AuthService auth, ISettingsService settings, FileService files, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Guid AdminId => current.RequireUserId();
    private static readonly string[] Owner = ["Owner"];

    public async Task<AdminDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        var since14 = Now.Date.AddDays(-13);
        var logins = await db.LoginAuditLogs.AsNoTracking().Where(l => l.Timestamp >= since14 && (l.Event == "login_success" || l.Event == "login"))
            .GroupBy(l => new { l.Timestamp.Date, l.Success }).Select(g => new { g.Key.Date, g.Key.Success, C = g.Count() }).ToListAsync(ct);
        var activity = Enumerable.Range(0, 14).Select(i => since14.AddDays(i)).Select(d => new CountPoint(d.ToString("dd MMM"),
            logins.Where(l => l.Date == d && l.Success).Sum(l => l.C), logins.Where(l => l.Date == d && !l.Success).Sum(l => l.C))).ToList();
        var logs = await db.CommunicationLogs.AsNoTracking().Where(l => l.CreatedAt >= Now.AddDays(-30)).GroupBy(l => new { l.Channel, l.Status })
            .Select(g => new { g.Key.Channel, g.Key.Status, C = g.Count() }).ToListAsync(ct);
        var pending = await db.NotificationQueue.AsNoTracking().Where(q => q.Status == QueueStatus.Pending || q.Status == QueueStatus.Processing)
            .GroupBy(q => q.Channel).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var delivery = new[] { NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.Push, NotificationChannel.WhatsApp }.Select(c => new ChannelStat(c.ToString(),
            logs.Where(l => l.Channel == c && l.Status == QueueStatus.Sent).Sum(l => l.C), logs.Where(l => l.Channel == c && l.Status is QueueStatus.Failed or QueueStatus.Dead).Sum(l => l.C),
            pending.Where(p => p.Key == c).Sum(p => p.C))).ToList();
        bool dbOk;
        try { dbOk = await db.Database.CanConnectAsync(ct); } catch { dbOk = false; }
        return new AdminDashboardDto(
            await users.CountAsync(ct), await users.CountAsync(u => u.Status == UserStatus.Active && u.LastLoginAt >= Now.AddDays(-30), ct),
            await users.CountAsync(u => u.Status == UserStatus.Inactive, ct), await users.CountAsync(u => u.Status == UserStatus.Suspended, ct),
            await users.CountAsync(u => u.Status == UserStatus.PendingDeletion, ct), await users.CountAsync(u => u.CreatedAt >= Now.AddDays(-7), ct),
            await users.CountAsync(u => u.CreatedAt >= Now.AddDays(-30), ct), await users.CountAsync(u => u.TwoFactorEnabled, ct),
            await db.UserPresences.CountAsync(p => p.IsOnline && p.LastHeartbeatAt > Now.AddSeconds(-75), ct), activity,
            await db.StoredFiles.IgnoreQueryFilters().Where(f => !f.IsDeleted).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0,
            await db.StoredFiles.IgnoreQueryFilters().CountAsync(f => !f.IsDeleted, ct),
            await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.CreatedAt >= Now.Date, ct), delivery,
            await db.NotificationQueue.CountAsync(q => q.Status == QueueStatus.Pending, ct), await db.NotificationQueue.CountAsync(q => q.Status == QueueStatus.Dead, ct),
            dbOk, Now.ToString("u"), await db.UserReports.CountAsync(r => r.Status == ReportStatus.Open, ct));
    }

    // ---------------------------------------------------------------- users
    public async Task<PagedResult<AdminUserRow>> UsersAsync(AdminUserQuery q, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(u => EF.Functions.ILike(u.FullName, like) || EF.Functions.ILike(u.MobileNumber, like) || EF.Functions.ILike(u.PublicId, like) || (u.Email != null && EF.Functions.ILike(u.Email, like)));
        }
        if (q.Status is { } s) query = query.Where(u => u.Status == s);
        if (q.TwoFactor is { } tf) query = query.Where(u => u.TwoFactorEnabled == tf);
        if (!string.IsNullOrWhiteSpace(q.Role)) query = q.Role == "none" ? query.Where(u => !u.UserRoles.Any()) : query.Where(u => u.UserRoles.Any(r => r.Role!.Name == q.Role));
        var now = Now;
        return await query.OrderByDescending(u => u.CreatedAt).Select(u => new AdminUserRow(u.Id, u.PublicId, u.FullName, u.MobileNumber, u.Email, u.Status, u.TwoFactorEnabled,
            u.LockoutEndUtc > now, u.UserRoles.Select(r => r.Role!.Name).ToList(), u.CreatedAt, u.LastLoginAt)).ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<AdminUserDetail> UserAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).Include(x => x.UserRoles).ThenInclude(r => r.Role).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User");
        var row = new AdminUserRow(u.Id, u.PublicId, u.FullName, u.MobileNumber, u.Email, u.Status, u.TwoFactorEnabled, u.LockoutEndUtc > Now, u.UserRoles.Select(r => r.Role!.Name).ToList(), u.CreatedAt, u.LastLoginAt);
        // Counts only — never content.
        var counts = new Dictionary<string, int>
        {
            ["Expenses"] = await db.Expenses.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Income records"] = await db.IncomeRecords.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Documents"] = await db.Documents.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Vault entries"] = await db.PasswordEntries.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Custom tables"] = await db.CustomTables.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Contacts"] = await db.Contacts.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Udhar records"] = await db.CreditTransactions.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Diary entries"] = await db.DiaryEntries.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
            ["Calendar events"] = await db.CalendarEvents.IgnoreQueryFilters(Owner).CountAsync(x => x.UserId == id && !x.IsDeleted, ct),
        };
        return new AdminUserDetail(row, u.Profile?.City, u.Profile?.State, u.Profile?.Country, u.MobileVerified, u.EmailVerified, u.StatusReason, u.FailedLoginCount,
            u.LockoutEndUtc, u.PasswordChangedAt, await db.UserSessions.CountAsync(s => s.UserId == id && s.RevokedAt == null && s.AbsoluteExpiresAt > Now, ct),
            await db.UserDevices.CountAsync(d => d.UserId == id, ct),
            await db.StoredFiles.IgnoreQueryFilters().Where(f => f.UserId == id && !f.IsDeleted).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0,
            await db.StoredFiles.IgnoreQueryFilters().CountAsync(f => f.UserId == id && !f.IsDeleted, ct), counts);
    }

    private async Task<User> TargetAsync(Guid id, CancellationToken ct, bool forbidSelf = true)
    {
        if (forbidSelf && id == AdminId) throw new ValidationAppException("You cannot perform this action on your own account.");
        var u = await db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User");
        // Only a Super Admin may act on another administrator.
        if (u.UserRoles.Count > 0 && !await IsSuperAdminAsync(AdminId, ct)) throw new ForbiddenException("Only a Super Admin can manage administrator accounts.");
        return u;
    }

    private Task<bool> IsSuperAdminAsync(Guid id, CancellationToken ct) => db.UserRoles.AnyAsync(r => r.UserId == id && r.Role!.Name == RoleNames.SuperAdmin, ct);

    public async Task ChangeStatusAsync(Guid id, StatusChangeRequest r, CancellationToken ct)
    {
        if (r.Status is UserStatus.Deleted) throw new ValidationAppException("status", "Use the account deletion process instead.");
        var u = await TargetAsync(id, ct);
        var old = u.Status;
        u.Status = r.Status; u.StatusReason = TextUtil.Clean(r.Reason, 500);
        if (r.Status == UserStatus.Active) { u.LockoutEndUtc = null; u.FailedLoginCount = 0; }
        await db.SaveChangesAsync(ct);
        if (r.Status != UserStatus.Active)
            foreach (var s in await db.UserSessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) await auth.ExpireSessionAsync(s, "admin_" + r.Status.ToString().ToLowerInvariant(), ct);
        if (r.Status == UserStatus.Active && old == UserStatus.PendingDeletion)
            await db.AccountDeletionRequests.Where(x => x.UserId == id && x.Status == DeletionRequestStatus.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeletionRequestStatus.Cancelled), ct);
        await audit.LogAdminAsync(AdminId, "user_status_" + r.Status.ToString().ToLowerInvariant(), id, r.Reason, ct);
        if (r.NotifyUser && r.Status is UserStatus.Suspended or UserStatus.Active or UserStatus.Inactive)
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.AccountSuspension, id,
                r.Status == UserStatus.Active ? "Your account is active" : "Your account has been " + r.Status.ToString().ToLowerInvariant(),
                r.Status == UserStatus.Active ? "Your NIJMITRA account has been re-activated." : $"Your NIJMITRA account was {r.Status.ToString().ToLowerInvariant()}." + (r.Reason != null ? $" Reason: {r.Reason}" : "")), ct);
    }

    public async Task UnlockAsync(Guid id, CancellationToken ct)
    {
        var u = await TargetAsync(id, ct, false);
        u.LockoutEndUtc = null; u.FailedLoginCount = 0; u.FailedPinCount = 0;
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "user_unlocked", id, null, ct);
    }

    /// <summary>Sets a random temporary password the admin hands over; the user must change it after signing in.</summary>
    public async Task<string> ResetPasswordAsync(Guid id, CancellationToken ct)
    {
        var u = await TargetAsync(id, ct);
        const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var temp = new string(RandomNumberGenerator.GetItems<char>(alpha, 10)) + "@" + RandomNumberGenerator.GetInt32(10, 99);
        u.PasswordHash = hasher.Hash(temp); u.MustChangePassword = true; u.PasswordChangedAt = Now; u.SecurityStamp = Guid.NewGuid().ToString("N");
        u.LockoutEndUtc = null; u.FailedLoginCount = 0;
        await db.SaveChangesAsync(ct);
        foreach (var s in await db.UserSessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) await auth.ExpireSessionAsync(s, "admin_password_reset", ct);
        await audit.LogAdminAsync(AdminId, "user_password_reset", id, "temporary password issued", ct);
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, id, "Password reset by support",
            "An administrator reset your password. Sign in with the temporary password you were given and choose a new one."), ct);
        return temp;
    }

    public async Task ResetTwoFactorAsync(Guid id, CancellationToken ct)
    {
        var u = await TargetAsync(id, ct);
        u.TwoFactorEnabled = false; u.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.UserAuthenticatorSecrets.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        await db.UserRecoveryCodes.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "user_2fa_reset", id, null, ct);
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, id, "Two-step verification reset",
            "An administrator reset your two-step verification. Please set it up again from Settings → Security."), ct);
    }

    public async Task<int> RevokeSessionsAsync(Guid id, CancellationToken ct)
    {
        await TargetAsync(id, ct, false);
        var sessions = await db.UserSessions.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions) await auth.ExpireSessionAsync(s, "admin_revoked", ct);
        await audit.LogAdminAsync(AdminId, "user_sessions_revoked", id, $"{sessions.Count} session(s)", ct);
        return sessions.Count;
    }

    public async Task SetRolesAsync(Guid id, RolesRequest r, CancellationToken ct)
    {
        if (!await IsSuperAdminAsync(AdminId, ct)) throw new ForbiddenException("Only a Super Admin can assign roles.");
        if (id == AdminId && !r.Roles.Contains(RoleNames.SuperAdmin)) throw new ValidationAppException("You cannot remove your own Super Admin role.");
        var roles = await db.Roles.Where(x => r.Roles.Contains(x.Name)).ToListAsync(ct);
        await db.UserRoles.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        foreach (var role in roles) db.UserRoles.Add(new UserRole { UserId = id, RoleId = role.Id, AssignedAt = Now });
        await db.SaveChangesAsync(ct);
        foreach (var s in await db.UserSessions.Where(x => x.UserId == id && x.RevokedAt == null && x.Id != current.SessionId).ToListAsync(ct)) await auth.ExpireSessionAsync(s, "roles_changed", ct);
        await audit.LogAdminAsync(AdminId, "user_roles_changed", id, string.Join(",", roles.Select(x => x.Name)), ct);
    }

    public async Task<PagedResult<Auth.LoginHistoryDto>> LoginHistoryAsync(Guid id, int page, int size, CancellationToken ct) => await auth.GetLoginHistoryAsync(id, page, size, ct);

    /// <summary>Super-Admin "sign in as user". Audited, user notified, admin rights stripped; vault, chat and security settings stay blocked.</summary>
    public async Task<AuthTokens> ImpersonateAsync(Guid id, CancellationToken ct)
    {
        if (current.IsImpersonating) throw new ValidationAppException("Stop the current impersonation first.");
        var u = await TargetAsync(id, ct);
        if (u.UserRoles.Count > 0) throw new ForbiddenException("Administrator accounts cannot be impersonated.");
        if (u.Status != UserStatus.Active) throw new ValidationAppException("Only active accounts can be accessed.");
        var tokens = await auth.CompleteSignInAsync(u, null, AdminId, current.SessionId, ct);
        await audit.LogAdminAsync(AdminId, "user_impersonation_started", id, $"session {tokens.SessionId}", ct);
        if ((await settings.GetAsync(ct)).NotifyUserOnAdminAccess)
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.AdminAccess, id, "Support accessed your account",
                $"A NIJMITRA administrator signed in to your account on {Now:dd MMM yyyy HH:mm} UTC for support purposes. Your password vault remained locked."), ct);
        return tokens;
    }

    // ---------------------------------------------------------------- roles
    public async Task<IReadOnlyList<RoleDto>> RolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem,
            r.RolePermissions.Select(p => p.Permission!.Key).ToList(), db.UserRoles.Count(ur => ur.RoleId == r.Id))).ToListAsync(ct);

    public async Task<IReadOnlyList<PermissionDto>> PermissionsAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking().OrderBy(p => p.Key).Select(p => new PermissionDto(p.Key, p.Description)).ToListAsync(ct);

    public async Task<RoleDto> SaveRoleAsync(Guid? id, RoleRequest r, CancellationToken ct)
    {
        var perms = await db.Permissions.Where(p => r.Permissions.Contains(p.Key)).ToListAsync(ct);
        Role role;
        if (id is { } existing)
        {
            role = await db.Roles.Include(x => x.RolePermissions).FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Role");
            if (role.Name == RoleNames.SuperAdmin) throw new ValidationAppException("The Super Admin role always has every permission.");
            db.RolePermissions.RemoveRange(role.RolePermissions);
            role.RolePermissions.Clear();
            if (!role.IsSystem) role.Name = r.Name.Trim();
        }
        else
        {
            if (await db.Roles.AnyAsync(x => x.Name == r.Name.Trim(), ct)) throw new ConflictException("Role already exists.");
            role = new Role { Name = r.Name.Trim() }; db.Roles.Add(role);
        }
        role.Description = TextUtil.Clean(r.Description, 200);
        foreach (var p in perms) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = p.Id });
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, id == null ? "role_created" : "role_updated", null, $"{role.Name}: {string.Join(",", perms.Select(p => p.Key))}", ct);
        return (await RolesAsync(ct)).First(x => x.Id == role.Id);
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Role");
        if (role.IsSystem) throw new ValidationAppException("Built-in roles cannot be deleted.");
        await db.UserRoles.Where(x => x.RoleId == id).ExecuteDeleteAsync(ct);
        await db.RolePermissions.Where(x => x.RoleId == id).ExecuteDeleteAsync(ct);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "role_deleted", null, role.Name, ct);
    }

    // ---------------------------------------------------------------- settings & branding
    public Task<PlatformSettings> GetSettingsAsync(CancellationToken ct) => settings.GetAsync(ct);

    public async Task<PlatformSettings> SaveSettingsAsync(PlatformSettings s, CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        if (s.SessionIdleTimeoutSeconds is < 30 or > 86400) throw new ValidationAppException("sessionIdleTimeoutSeconds", "Idle timeout must be between 30 seconds and 24 hours.");
        if (s.SessionWarningSeconds < 5 || s.SessionWarningSeconds >= s.SessionIdleTimeoutSeconds) throw new ValidationAppException("sessionWarningSeconds", "Warning must be at least 5 s and shorter than the timeout.");
        if (s.MaxUploadMb is < 1 or > 100) throw new ValidationAppException("maxUploadMb", "Upload limit must be 1–100 MB.");
        if (s.MaxLoginAttempts is < 3 or > 20) throw new ValidationAppException("maxLoginAttempts", "Login attempts must be 3–20.");
        var allowedAll = FileService.MimeTypes.Keys.Where(k => k is not ".svg" and not ".ico").ToHashSet();
        s.AllowedExtensions = s.AllowedExtensions.Select(e => e.Trim().ToLowerInvariant()).Select(e => e.StartsWith('.') ? e : "." + e).Where(allowedAll.Contains).Distinct().ToList();
        if (s.AllowedExtensions.Count == 0) throw new ValidationAppException("allowedExtensions", "Allow at least one file type.");
        var smsActive = await db.CommunicationProviders.AnyAsync(p => p.Channel == NotificationChannel.Sms && p.IsActive, ct);
        var emailActive = await db.CommunicationProviders.AnyAsync(p => p.Channel == NotificationChannel.Email && p.IsActive, ct);
        if (s.RequireMobileOtp && !smsActive) throw new ValidationAppException("requireMobileOtp", "Configure and activate an SMS provider before requiring mobile OTP.");
        if (s.RequireEmailVerification && !emailActive) throw new ValidationAppException("requireEmailVerification", "Configure and activate an email provider before requiring email verification.");
        s.Website = NormalizeWebsite(s.Website ?? new WebsiteSettings());
        s.LogoFileId = current.LogoFileId; s.FaviconFileId = current.FaviconFileId; // changed via the branding endpoints only
        await settings.SaveAsync(s, AdminId, ct);
        await audit.LogAdminAsync(AdminId, "settings_updated", null, $"idle={s.SessionIdleTimeoutSeconds}s; otp={s.RequireMobileOtp}; email={s.RequireEmailVerification}; 2fa={s.EnforceTwoFactor}; maintenance={s.MaintenanceMode}", ct);
        return await settings.GetAsync(ct);
    }

    private static readonly string[] SocialPlatforms = ["facebook", "instagram", "x", "youtube", "linkedin", "whatsapp", "telegram", "website"];

    /// <summary>Validates footer content: plain text only, safe link targets (http/https, mailto, tel, site-relative or #section).</summary>
    internal static WebsiteSettings NormalizeWebsite(WebsiteSettings w)
    {
        static string? T(string? v, int max) => TextUtil.Clean(v, max);
        static string Url(string? u, string field)
        {
            u = (u ?? "").Trim();
            var ok = u.StartsWith('#') || (u.StartsWith('/') && !u.StartsWith("//"))
                || u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || u.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || (Uri.TryCreate(u, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp));
            if (!ok || u.Length > 500 || u.Any(char.IsWhiteSpace)) throw new ValidationAppException(field, $"\"{u}\" is not a valid link. Use https://…, mailto:, tel:, /page or #section.");
            return u;
        }
        if (w.Columns.Count > 6) throw new ValidationAppException("website.columns", "At most 6 footer columns.");
        var cols = new List<FooterColumn>();
        foreach (var c in w.Columns)
        {
            var title = T(c.TitleEn, 60);
            if (title == null) throw new ValidationAppException("website.columns", "Every footer column needs an English title.");
            if (c.Links.Count > 12) throw new ValidationAppException("website.columns", $"Column \"{title}\" has more than 12 links.");
            var links = c.Links.Select(l => new FooterLink
            {
                LabelEn = T(l.LabelEn, 80) ?? throw new ValidationAppException("website.columns", $"A link in \"{title}\" has no English label."),
                LabelHi = T(l.LabelHi, 80), Url = Url(l.Url, "website.columns"),
            }).ToList();
            cols.Add(new FooterColumn { TitleEn = title, TitleHi = T(c.TitleHi, 60), Links = links });
        }
        var social = w.SocialLinks.Take(10).Select(x => new SocialLink
        {
            Platform = SocialPlatforms.Contains(x.Platform?.Trim().ToLowerInvariant()) ? x.Platform!.Trim().ToLowerInvariant() : throw new ValidationAppException("website.socialLinks", "Unknown social platform."),
            Url = Url(x.Url, "website.socialLinks"),
        }).ToList();
        var email = T(w.ContactEmail, 120);
        if (email != null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new ValidationAppException("website.contactEmail", "Enter a valid contact email.");
        var play = T(w.PlayStoreUrl, 300);
        if (play != null && !play.StartsWith("https://play.google.com/", StringComparison.OrdinalIgnoreCase)) throw new ValidationAppException("website.playStoreUrl", "Play Store link must start with https://play.google.com/");
        var pkg = (w.AndroidPackageName ?? "").Trim();
        if (pkg.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(pkg, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")) throw new ValidationAppException("website.androidPackageName", "Enter a valid Android package name, e.g. com.nijmitra.app");
        var prints = new List<string>();
        foreach (var raw in w.AndroidSha256Fingerprints ?? [])
        {
            var hex = new string((raw ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (hex.Length == 0) continue;
            if (hex.Length != 64) throw new ValidationAppException("website.androidSha256Fingerprints", $"\"{raw}\" is not a SHA-256 fingerprint (64 hex characters).");
            prints.Add(string.Join(':', Enumerable.Range(0, 32).Select(i => hex.Substring(i * 2, 2))));
        }
        return new WebsiteSettings
        {
            AndroidPackageName = pkg, AndroidSha256Fingerprints = prints.Distinct().Take(10).ToList(),
            AboutEn = T(w.AboutEn, 500) ?? "", AboutHi = T(w.AboutHi, 500), CopyrightName = T(w.CopyrightName, 80) ?? "NijMitra",
            CompanyLine = T(w.CompanyLine, 120), ContactEmail = email, ContactPhone = T(w.ContactPhone, 30), Address = T(w.Address, 250),
            PlayStoreUrl = play, Columns = cols, SocialLinks = social,
        };
    }

    public async Task<PlatformSettings> UploadBrandingAsync(string kind, UploadInput input, CancellationToken ct)
    {
        var allowed = kind == "favicon" ? new[] { ".png", ".ico" } : new[] { ".png", ".jpg", ".jpeg", ".webp" };
        var f = await files.UploadAsync(input, "branding", ct, allowed);
        var s = await settings.GetAsync(ct);
        if (kind == "favicon") s.FaviconFileId = f.Id.ToString(); else s.LogoFileId = f.Id.ToString();
        await settings.SaveAsync(s, AdminId, ct);
        await audit.LogAdminAsync(AdminId, "branding_updated", null, kind, ct);
        return s;
    }

    public async Task<PlatformSettings> ResetBrandingAsync(string kind, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (kind == "favicon") s.FaviconFileId = null; else s.LogoFileId = null;
        await settings.SaveAsync(s, AdminId, ct);
        return s;
    }

    // ---------------------------------------------------------------- audit & reports
    public async Task<PagedResult<AuditRow>> AuditAsync(AuditQuery q, CancellationToken ct)
    {
        var names = db.Users.AsNoTracking();
        switch (q.Kind)
        {
            case "login":
                var lq = db.LoginAuditLogs.AsNoTracking().AsQueryable();
                if (q.UserId is { } lu) lq = lq.Where(l => l.UserId == lu);
                if (!string.IsNullOrWhiteSpace(q.Event)) lq = lq.Where(l => l.Event == q.Event);
                if (q.Success is { } ls) lq = lq.Where(l => l.Success == ls);
                if (q.From is { } lf) lq = lq.Where(l => l.Timestamp >= lf);
                if (q.To is { } lt) lq = lq.Where(l => l.Timestamp <= lt);
                return await lq.OrderByDescending(l => l.Timestamp).Select(l => new AuditRow(l.Id, l.Timestamp, l.UserId, names.Where(u => u.Id == l.UserId).Select(u => u.FullName).FirstOrDefault() ?? l.MaskedIdentifier,
                    l.Event, null, l.FailureReason, l.Success, l.IpAddress, l.Browser, l.OperatingSystem, l.Device, l.SessionId)).ToPagedAsync(q.Page, q.PageSize, ct);
            case "admin":
                var aq = db.AdminAuditLogs.AsNoTracking().AsQueryable();
                if (q.UserId is { } au) aq = aq.Where(l => l.TargetUserId == au || l.AdminUserId == au);
                if (!string.IsNullOrWhiteSpace(q.Event)) aq = aq.Where(l => l.Action == q.Event);
                if (q.From is { } af) aq = aq.Where(l => l.Timestamp >= af);
                if (q.To is { } at) aq = aq.Where(l => l.Timestamp <= at);
                return await aq.OrderByDescending(l => l.Timestamp).Select(l => new AuditRow(l.Id, l.Timestamp, l.AdminUserId, names.Where(u => u.Id == l.AdminUserId).Select(u => u.FullName).FirstOrDefault(),
                    l.Action, l.TargetUserId != null ? "User" : null, (l.TargetUserId != null ? "Target: " + names.Where(u => u.Id == l.TargetUserId).Select(u => u.PublicId).FirstOrDefault() + ". " : "") + l.Details,
                    true, l.IpAddress, null, null, null, null)).ToPagedAsync(q.Page, q.PageSize, ct);
            default:
                var gq = db.AuditLogs.AsNoTracking().AsQueryable();
                if (q.UserId is { } gu) gq = gq.Where(l => l.UserId == gu);
                if (!string.IsNullOrWhiteSpace(q.Event)) gq = gq.Where(l => l.Event == q.Event);
                if (q.Success is { } gs) gq = gq.Where(l => l.Success == gs);
                if (q.From is { } gf) gq = gq.Where(l => l.Timestamp >= gf);
                if (q.To is { } gt) gq = gq.Where(l => l.Timestamp <= gt);
                return await gq.OrderByDescending(l => l.Timestamp).Select(l => new AuditRow(l.Id, l.Timestamp, l.UserId, names.Where(u => u.Id == l.UserId).Select(u => u.FullName).FirstOrDefault(),
                    l.Event, l.EntityType, l.ImpersonatedBy != null ? "[admin session] " + l.Details : l.Details, l.Success, l.IpAddress, l.Browser, l.OperatingSystem, l.Device, l.SessionId)).ToPagedAsync(q.Page, q.PageSize, ct);
        }
    }

    public async Task<PagedResult<ReportRow>> ReportsAsync(ReportStatus? status, int page, int size, CancellationToken ct) =>
        await db.UserReports.AsNoTracking().Where(r => status == null || r.Status == status).OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReportRow(r.Id, db.Users.Where(u => u.Id == r.ReporterId).Select(u => u.FullName + " (" + u.PublicId + ")").First(),
                db.Users.Where(u => u.Id == r.ReportedUserId).Select(u => u.FullName + " (" + u.PublicId + ")").First(), r.ReportedUserId, r.Reason, r.Details, r.Status, r.CreatedAt))
            .ToPagedAsync(page, size, ct);

    public async Task UpdateReportAsync(Guid id, ReportStatus status, CancellationToken ct)
    {
        var r = await db.UserReports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report");
        r.Status = status; r.ReviewedAt = Now; r.ReviewedBy = AdminId;
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "report_" + status.ToString().ToLowerInvariant(), r.ReportedUserId, null, ct);
    }
}
