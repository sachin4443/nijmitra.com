using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Account;

public record UpdateProfileRequest(
    [Required, StringLength(150, MinimumLength = 2)] string FullName, [StringLength(500)] string? Address, DateOnly? DateOfBirth,
    [StringLength(100)] string? City, [StringLength(100)] string? State, [StringLength(100)] string? Country,
    [StringLength(60)] string? TimeZone, [StringLength(10)] string? Language);
public record ThemeRequest([Required] string Theme);
public record DashboardLayoutRequest([Required] string LayoutJson);
public record PreferencesDto(bool EmailEnabled, bool SmsEnabled, bool PushEnabled, bool MeetingReminders, bool ExpenseReminders,
    bool UdharReminders, bool GeneralReminders, bool FriendRequests, bool ChatMessages, bool LoginAlerts, bool Marketing);
public record PushSubscribeRequest([Required, Url] string Endpoint, [Required] string P256dh, [Required] string Auth);
public record DeletionRequestDto(Guid Id, DateTime RequestedAt, DateTime ScheduledFor, string Status);
public record DeleteAccountRequest([Required] string Password, [StringLength(500)] string? Reason);

public sealed class AccountService(IAppDbContext db, ICurrentUser current, AuthService auth, FileService files, ISettingsService settings,
    IAuditService audit, IFieldEncryptor enc, IFileStorage storage, TimeProvider clock, IPasswordHasherService hasher)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<MeDto> GetMeAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var user = await db.Users.AsNoTracking().Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == uid, ct) ?? throw new NotFoundException("Account");
        var (roles, perms) = current.IsImpersonating ? ([], []) : await auth.RolesAndPermissionsAsync(uid, ct);
        string? impName = null;
        if (current.ImpersonatorId is { } imp) impName = await db.Users.Where(u => u.Id == imp).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var s = await settings.GetAsync(ct);
        var p = user.Profile ?? new UserProfile();
        return new MeDto(user.Id, user.PublicId, user.FullName, user.MobileNumber, user.Email, user.EmailVerified, user.PendingEmail,
            user.TwoFactorEnabled, user.PinHash != null, user.MustChangePassword, roles, perms, current.IsImpersonating, impName,
            new ProfileDto(p.AvatarFileId, p.Address, p.DateOfBirth, p.City, p.State, p.Country, p.TimeZone, p.Language, p.Theme.ToString().ToLowerInvariant()),
            new SessionPolicyDto(Math.Max(30, s.SessionIdleTimeoutSeconds), Math.Clamp(s.SessionWarningSeconds, 5, Math.Max(5, s.SessionIdleTimeoutSeconds - 5))));
    }

    public async Task UpdateProfileAsync(UpdateProfileRequest r, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var user = await db.Users.Include(u => u.Profile).FirstAsync(u => u.Id == uid, ct);
        user.FullName = r.FullName.Trim();
        var p = user.Profile!;
        p.Address = TextUtil.Clean(r.Address, 500); p.DateOfBirth = r.DateOfBirth; p.City = TextUtil.Clean(r.City, 100);
        p.State = TextUtil.Clean(r.State, 100); p.Country = TextUtil.Clean(r.Country, 100);
        if (!string.IsNullOrWhiteSpace(r.TimeZone))
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone); p.TimeZone = r.TimeZone; }
            catch { throw new ValidationAppException("timeZone", "Unknown time zone."); }
        }
        if (r.Language is "en" or "hi") p.Language = r.Language;
        p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("profile_change"), ct);
    }

    public async Task SetThemeAsync(string theme, CancellationToken ct)
    {
        if (!Enum.TryParse<ThemePreference>(theme, true, out var t)) throw new ValidationAppException("theme", "Theme must be system, light or dark.");
        var uid = current.RequireUserId();
        var p = await db.UserProfiles.FirstAsync(x => x.UserId == uid, ct);
        p.Theme = t; p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetDashboardLayoutAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        return await db.UserProfiles.Where(x => x.UserId == uid).Select(x => x.DashboardLayoutJson).FirstOrDefaultAsync(ct);
    }

    public async Task SaveDashboardLayoutAsync(string json, CancellationToken ct)
    {
        if (json.Length > 4000) throw new ValidationAppException("layoutJson", "Layout is too large.");
        try { JsonDocument.Parse(json); } catch { throw new ValidationAppException("layoutJson", "Invalid layout."); }
        var uid = current.RequireUserId();
        var p = await db.UserProfiles.FirstAsync(x => x.UserId == uid, ct);
        p.DashboardLayoutJson = json; p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> UploadAvatarAsync(UploadInput input, CancellationToken ct)
    {
        var file = await files.UploadAsync(input, "avatar", ct, FileService.ImageExtensions);
        var uid = current.RequireUserId();
        var p = await db.UserProfiles.FirstAsync(x => x.UserId == uid, ct);
        if (p.AvatarFileId is { } old) await files.DeleteOwnAsync(old, ct);
        p.AvatarFileId = file.Id; p.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return file.Id;
    }

    public async Task<FileStreamResultDto> GetAvatarAsync(Guid userId, CancellationToken ct)
    {
        // Avatars are visible to the owner and to accepted friends only.
        var uid = current.RequireUserId();
        if (userId != uid)
        {
            var (a, b) = uid.CompareTo(userId) < 0 ? (uid, userId) : (userId, uid);
            if (!await db.FriendConnections.AnyAsync(f => f.UserAId == a && f.UserBId == b, ct)) throw new NotFoundException("Avatar");
        }
        var fileId = await db.UserProfiles.Where(x => x.UserId == userId).Select(x => x.AvatarFileId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Avatar");
        return await files.OpenAuthorizedAsync(fileId, ct);
    }

    public async Task<PreferencesDto> GetPreferencesAsync(CancellationToken ct)
    {
        var p = await GetOrCreatePrefsAsync(ct);
        return new PreferencesDto(p.EmailEnabled, p.SmsEnabled, p.PushEnabled, p.MeetingReminders, p.ExpenseReminders, p.UdharReminders,
            p.GeneralReminders, p.FriendRequests, p.ChatMessages, p.LoginAlerts, p.Marketing);
    }

    public async Task SavePreferencesAsync(PreferencesDto d, CancellationToken ct)
    {
        var p = await GetOrCreatePrefsAsync(ct);
        p.EmailEnabled = d.EmailEnabled; p.SmsEnabled = d.SmsEnabled; p.PushEnabled = d.PushEnabled; p.MeetingReminders = d.MeetingReminders;
        p.ExpenseReminders = d.ExpenseReminders; p.UdharReminders = d.UdharReminders; p.GeneralReminders = d.GeneralReminders;
        p.FriendRequests = d.FriendRequests; p.ChatMessages = d.ChatMessages; p.LoginAlerts = d.LoginAlerts; p.Marketing = d.Marketing; p.UpdatedAt = Now;
        var sec = await db.UserSecuritySettings.FirstOrDefaultAsync(x => x.UserId == p.UserId, ct);
        if (sec != null) sec.LoginAlertsEnabled = d.LoginAlerts;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("notification_preferences_change"), ct);
    }

    private async Task<NotificationPreference> GetOrCreatePrefsAsync(CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == uid, ct);
        if (p != null) return p;
        p = new NotificationPreference { UserId = uid, UpdatedAt = Now };
        db.NotificationPreferences.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task SubscribePushAsync(PushSubscribeRequest r, CancellationToken ct)
    {
        if (!r.Endpoint.StartsWith("https://")) throw new ValidationAppException("endpoint", "Invalid push endpoint.");
        var uid = current.RequireUserId();
        var existing = await db.PushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Endpoint == r.Endpoint, ct);
        if (existing != null && existing.UserId != uid) { db.PushSubscriptions.Remove(existing); await db.SaveChangesAsync(ct); existing = null; }
        if (existing == null)
            db.PushSubscriptions.Add(new PushSubscription { UserId = uid, Endpoint = r.Endpoint, P256dh = r.P256dh, Auth = r.Auth, UserAgent = current.UserAgent, CreatedAt = Now });
        else { existing.P256dh = r.P256dh; existing.Auth = r.Auth; }
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribePushAsync(string endpoint, CancellationToken ct)
    {
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync(ct);
    }

    // ---------------------------------------------------------------- data export & deletion
    /// <summary>
    /// Builds a ZIP with the user's records as JSON plus their uploaded files. Password-vault items are exported
    /// as ciphertext only (decryptable solely with the user's master password).
    /// </summary>
    public async Task<byte[]> ExportDataAsync(CancellationToken ct)
    {
        await auth.EnsureRecentlyReauthenticatedAsync(ct);
        var uid = current.RequireUserId();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            async Task Add(string name, object data)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var w = e.Open();
                await JsonSerializer.SerializeAsync(w, data, json, ct);
            }
            var user = await db.Users.AsNoTracking().Include(u => u.Profile).Where(u => u.Id == uid)
                .Select(u => new { u.PublicId, u.FullName, u.MobileNumber, u.Email, u.EmailVerified, u.CreatedAt, u.Profile }).FirstAsync(ct);
            await Add("profile.json", user);
            await Add("finance/expense-categories.json", await db.ExpenseCategories.AsNoTracking().ToListAsync(ct));
            await Add("finance/expenses.json", await db.Expenses.AsNoTracking().ToListAsync(ct));
            await Add("finance/income.json", await db.IncomeRecords.AsNoTracking().ToListAsync(ct));
            await Add("finance/budgets.json", await db.MonthlyBudgets.AsNoTracking().Include(b => b.Categories).ToListAsync(ct));
            await Add("finance/recurring.json", await db.RecurringExpenses.AsNoTracking().Include(r => r.Occurrences).ToListAsync(ct));
            await Add("documents/folders.json", await db.DocumentFolders.AsNoTracking().ToListAsync(ct));
            await Add("documents/documents.json", await db.Documents.AsNoTracking().Include(d => d.Versions).Include(d => d.Tags).ToListAsync(ct));
            await Add("vault/vault-encrypted.json", new
            {
                note = "Encrypted with your master password. Import this file in NIJMITRA Password Manager to restore.",
                vault = await db.PasswordVaults.AsNoTracking().FirstOrDefaultAsync(ct),
                entries = await db.PasswordEntries.AsNoTracking().ToListAsync(ct),
                notes = await db.SecureNotes.AsNoTracking().ToListAsync(ct)
            });
            await Add("registers/custom-tables.json", await db.CustomTables.AsNoTracking().Include(t => t.Columns).ToListAsync(ct));
            await Add("registers/custom-records.json", await db.CustomTableRecords.AsNoTracking().Include(r => r.Values).ToListAsync(ct));
            await Add("registers/product-registers.json", await db.ProductRegisters.AsNoTracking().Include(r => r.CustomFields).ToListAsync(ct));
            await Add("registers/products.json", await db.Products.AsNoTracking().ToListAsync(ct));
            await Add("contacts/groups.json", await db.ContactGroups.AsNoTracking().ToListAsync(ct));
            await Add("contacts/contacts.json", await db.Contacts.AsNoTracking().ToListAsync(ct));
            await Add("udhar/transactions.json", await db.CreditTransactions.AsNoTracking().Include(t => t.Payments).ToListAsync(ct));
            var diary = await db.DiaryEntries.AsNoTracking().Include(d => d.Tags).ToListAsync(ct);
            await Add("diary/entries.json", diary.Select(d => new { d.Id, d.Title, d.EntryDate, d.EntryTime, d.Mood, Tags = d.Tags.Select(t => t.Tag), Content = enc.Decrypt(d.EncryptedContent) }));
            await Add("calendar/events.json", await db.CalendarEvents.AsNoTracking().ToListAsync(ct));
            await Add("calendar/reminders.json", await db.Reminders.AsNoTracking().ToListAsync(ct));
            foreach (var f in await db.StoredFiles.AsNoTracking().Where(f => f.Purpose != "chat").ToListAsync(ct))
            {
                if (!await storage.ExistsAsync(f.StorageKey, ct)) continue;
                var e = zip.CreateEntry($"files/{f.Purpose}/{f.Id:N}_{f.OriginalName}", CompressionLevel.Fastest);
                await using var w = e.Open();
                await using var r = await storage.OpenReadAsync(f.StorageKey, ct);
                await r.CopyToAsync(w, ct);
            }
        }
        await audit.LogAsync(new AuditEntry("data_export"), ct);
        return ms.ToArray();
    }

    public async Task<DeletionRequestDto> RequestDeletionAsync(DeleteAccountRequest r, CancellationToken ct)
    {
        var uid = current.RequireUserId();
        var user = await db.Users.FirstAsync(u => u.Id == uid, ct);
        if (!hasher.Verify(user.PasswordHash, r.Password, out _)) throw new ValidationAppException("password", "Password is incorrect.");
        await auth.EnsureRecentlyReauthenticatedAsync(ct);
        var s = await settings.GetAsync(ct);
        var req = new AccountDeletionRequest { UserId = uid, Reason = TextUtil.Clean(r.Reason, 500), RequestedAt = Now, ScheduledFor = Now.AddDays(Math.Max(1, s.AccountDeletionGraceDays)) };
        db.AccountDeletionRequests.Add(req);
        user.Status = UserStatus.PendingDeletion;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("account_deletion_requested", Details: $"scheduled {req.ScheduledFor:yyyy-MM-dd}"), ct);
        foreach (var sess in await db.UserSessions.Where(x => x.UserId == uid && x.RevokedAt == null).ToListAsync(ct))
            await auth.ExpireSessionAsync(sess, "account_deletion_requested", ct);
        return new DeletionRequestDto(req.Id, req.RequestedAt, req.ScheduledFor, req.Status.ToString());
    }
}
