using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Admin;

public record ProviderDto(Guid Id, NotificationChannel Channel, ProviderType ProviderType, string Name, bool IsActive, bool IsDefault, int Priority,
    IReadOnlyDictionary<string, string?> Settings, IReadOnlyList<string> SecretsConfigured, DateTime? LastTestedAt, bool? LastTestSucceeded, string? LastTestMessage, DateTime UpdatedAt);
public record ProviderRequest(NotificationChannel Channel, ProviderType ProviderType, [Required, StringLength(100)] string Name, bool IsActive, bool IsDefault,
    [Range(1, 1000)] int Priority, Dictionary<string, string?> Settings, Dictionary<string, string?>? Secrets);
public record TestSendRequest([Required, StringLength(256)] string Recipient, [StringLength(500)] string? Message);
public record PolicyDto(string EventType, string DisplayName, bool InAppEnabled, bool EmailEnabled, bool SmsEnabled, bool PushEnabled, bool WhatsAppEnabled, bool IsSecurityCritical, bool UserCanOptOut);
public record PolicyUpdate(string EventType, bool InAppEnabled, bool EmailEnabled, bool SmsEnabled, bool PushEnabled, bool WhatsAppEnabled, bool UserCanOptOut);
public record TemplateDto(Guid Id, string EventType, NotificationChannel Channel, string? Subject, string Body, string? ProviderTemplateId, bool IsActive, DateTime UpdatedAt);
public record TemplateRequest(string EventType, NotificationChannel Channel, [StringLength(300)] string? Subject, [Required, StringLength(20000)] string Body, [StringLength(100)] string? ProviderTemplateId, bool IsActive);
public record TemplatePreview(string? Subject, string Body);
public record LogRow(Guid Id, DateTime CreatedAt, string MaskedRecipient, NotificationChannel Channel, string EventType, string? ProviderName, QueueStatus Status, string? FailureReason, int Attempt, bool WillRetry);
public record LogQuery(NotificationChannel? Channel, QueueStatus? Status, string? EventType, string? Search, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50);
public record AnnouncementRequest([Required, StringLength(200, MinimumLength = 3)] string Title, [Required, StringLength(5000)] string Body, AnnouncementTarget Target,
    string? TargetValue, bool SendInApp, bool SendEmail, bool SendPush, bool SendSms);
public record AnnouncementDto(Guid Id, string Title, string Body, AnnouncementTarget Target, string? TargetValue, bool SendInApp, bool SendEmail, bool SendPush, bool SendSms,
    int RecipientCount, string CreatedByName, DateTime CreatedAt);
public record QueueStats(int Pending, int Processing, int Sent, int Failed, int Dead);

public sealed class CommunicationAdminService(IAppDbContext db, ICurrentUser current, IFieldEncryptor enc, IProviderGateway gateway, INotificationService notifications,
    IAuditService audit, ISettingsService settings, IOptions<AppOptions> app, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Guid AdminId => current.RequireUserId();

    public IReadOnlyList<ProviderDefinition> Definitions() => ProviderCatalog.All;

    private ProviderDto Map(CommunicationProvider p)
    {
        var secrets = string.IsNullOrEmpty(p.EncryptedSecretsJson) ? [] : JsonSerializer.Deserialize<Dictionary<string, string?>>(enc.Decrypt(p.EncryptedSecretsJson)) ?? [];
        return new ProviderDto(p.Id, p.Channel, p.ProviderType, p.Name, p.IsActive, p.IsDefault, p.Priority,
            JsonSerializer.Deserialize<Dictionary<string, string?>>(p.SettingsJson) ?? [], secrets.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList(),
            p.LastTestedAt, p.LastTestSucceeded, p.LastTestMessage, p.UpdatedAt);
    }

    public async Task<IReadOnlyList<ProviderDto>> ProvidersAsync(CancellationToken ct) =>
        (await db.CommunicationProviders.AsNoTracking().OrderBy(p => p.Channel).ThenByDescending(p => p.IsDefault).ThenBy(p => p.Priority).ToListAsync(ct)).Select(Map).ToList();

    /// <summary>Creates/updates a provider. Secret fields left blank keep their stored value; secrets are never returned.</summary>
    public async Task<ProviderDto> SaveProviderAsync(Guid? id, ProviderRequest r, CancellationToken ct)
    {
        var def = ProviderCatalog.All.FirstOrDefault(d => d.Type == r.ProviderType) ?? throw new ValidationAppException("providerType", "Unsupported provider.");
        if (def.Channel != r.Channel) throw new ValidationAppException("channel", "Provider does not match the channel.");
        CommunicationProvider p;
        Dictionary<string, string?> secrets = [];
        if (id is { } existing)
        {
            p = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Provider");
            if (p.ProviderType != r.ProviderType) throw new ValidationAppException("providerType", "Provider type cannot be changed; create a new provider instead.");
            if (!string.IsNullOrEmpty(p.EncryptedSecretsJson)) secrets = JsonSerializer.Deserialize<Dictionary<string, string?>>(enc.Decrypt(p.EncryptedSecretsJson)) ?? [];
        }
        else { p = new CommunicationProvider { Channel = r.Channel, ProviderType = r.ProviderType }; db.CommunicationProviders.Add(p); }
        var settingsOut = new Dictionary<string, string?>();
        var errors = new Dictionary<string, string[]>();
        foreach (var f in def.Fields)
        {
            if (f.Secret)
            {
                if (r.Secrets?.TryGetValue(f.Key, out var sv) == true && !string.IsNullOrWhiteSpace(sv)) secrets[f.Key] = sv.Trim();
                if (f.Required && string.IsNullOrEmpty(secrets.GetValueOrDefault(f.Key))) errors[f.Key] = [$"{f.Label} is required."];
            }
            else
            {
                var v = r.Settings.TryGetValue(f.Key, out var val) && !string.IsNullOrWhiteSpace(val) ? val.Trim() : f.Default;
                if (f.Required && string.IsNullOrEmpty(v)) errors[f.Key] = [$"{f.Label} is required."];
                if (f.Options != null && v != null && !f.Options.Contains(v)) errors[f.Key] = [$"{f.Label} must be one of {string.Join(", ", f.Options)}."];
                if (f.Key == "headersJson" && !string.IsNullOrEmpty(v)) { try { JsonSerializer.Deserialize<Dictionary<string, string>>(v); } catch { errors[f.Key] = ["Headers must be a JSON object of strings."]; } }
                if (f.Key == "apiUrl" && v != null && !Uri.TryCreate(v.Replace("{{", "x").Replace("}}", "x"), UriKind.Absolute, out var u)) errors[f.Key] = ["Enter a valid URL."];
                settingsOut[f.Key] = v;
            }
        }
        if (errors.Count > 0) throw new ValidationAppException(errors);
        p.Name = r.Name.Trim(); p.IsActive = r.IsActive; p.Priority = r.Priority; p.SettingsJson = JsonSerializer.Serialize(settingsOut);
        p.EncryptedSecretsJson = secrets.Count == 0 ? null : enc.Encrypt(JsonSerializer.Serialize(secrets));
        if (r.IsDefault)
        {
            await db.CommunicationProviders.Where(x => x.Channel == r.Channel && x.Id != p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
            p.IsDefault = true;
        }
        else p.IsDefault = false;
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, id == null ? "provider_created" : "provider_updated", null, $"{r.Channel}/{r.ProviderType} '{p.Name}' active={p.IsActive}", ct);
        return Map(p);
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken ct)
    {
        var p = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        db.CommunicationProviders.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "provider_deleted", null, p.Name, ct);
        await GuardSecuritySettingsAsync(ct);
    }

    public async Task<ProviderDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var p = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        p.IsActive = active;
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, active ? "provider_enabled" : "provider_disabled", null, p.Name, ct);
        if (!active) await GuardSecuritySettingsAsync(ct);
        return Map(p);
    }

    /// <summary>If the last SMS/email provider is disabled, OTP/verification requirements are switched off so users are never locked out.</summary>
    private async Task GuardSecuritySettingsAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var changed = false;
        if (s.RequireMobileOtp && !await db.CommunicationProviders.AnyAsync(p => p.Channel == NotificationChannel.Sms && p.IsActive, ct)) { s.RequireMobileOtp = false; changed = true; }
        if (s.RequireEmailVerification && !await db.CommunicationProviders.AnyAsync(p => p.Channel == NotificationChannel.Email && p.IsActive, ct)) { s.RequireEmailVerification = false; changed = true; }
        if (changed) await settings.SaveAsync(s, AdminId, ct);
    }

    public async Task<ProviderDto> TestConnectionAsync(Guid id, CancellationToken ct)
    {
        var p = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        ProviderTestResult result;
        try { result = await gateway.TestConnectionAsync(p, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { result = new ProviderTestResult(false, ex.Message); }
        p.LastTestedAt = Now; p.LastTestSucceeded = result.Success; p.LastTestMessage = Scrub(result.Message);
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "provider_tested", null, $"{p.Name}: {(result.Success ? "ok" : "failed")}", ct);
        return Map(p);
    }

    /// <summary>Sends a real test message through this specific provider (bypassing the queue) and logs the result.</summary>
    public async Task<ProviderTestResult> SendTestAsync(Guid id, TestSendRequest r, CancellationToken ct)
    {
        var p = await db.CommunicationProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Provider");
        var s = await settings.GetAsync(ct);
        var text = TextUtil.Clean(r.Message, 500) ?? $"This is a test message from {s.ApplicationName}. Your {p.Channel} provider '{p.Name}' is working.";
        var recipient = r.Recipient.Trim();
        var masked = p.Channel == NotificationChannel.Email ? TextUtil.MaskEmail(recipient) : TextUtil.MaskMobile(recipient);
        var log = new CommunicationLog { Channel = p.Channel, EventType = "provider_test", ProviderName = p.Name, MaskedRecipient = masked, Attempt = 1, CreatedAt = Now };
        try
        {
            switch (p.Channel)
            {
                case NotificationChannel.Email:
                    if (!System.Net.Mail.MailAddress.TryCreate(recipient, out _)) throw new ValidationAppException("recipient", "Enter a valid email address.");
                    var html = TemplateRenderer.WrapEmail($"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>", s.ApplicationName, app.Value.PublicBaseUrl.TrimEnd('/') + "/branding/logo-email.png", s.Tagline);
                    await gateway.SendDirectAsync(p, new OutboundMessage(recipient, $"{s.ApplicationName} test email", html, null, new Dictionary<string, string>(), "provider_test"), ct);
                    break;
                case NotificationChannel.Sms:
                case NotificationChannel.WhatsApp:
                    recipient = Auth.AuthService.ValidateMobile(recipient);
                    var vars = new Dictionary<string, string> { ["OTP"] = "" };
                    await gateway.SendDirectAsync(p, new OutboundMessage(recipient, null, text, null, new Dictionary<string, string>(), "provider_test"), ct);
                    break;
                case NotificationChannel.Push:
                    var subs = await db.PushSubscriptions.IgnoreQueryFilters().Where(x => x.UserId == AdminId).ToListAsync(ct);
                    if (subs.Count == 0) throw new ValidationAppException("recipient", "Enable push notifications on this device first (Settings → Notifications), then try again.");
                    foreach (var sub in subs)
                        await gateway.SendDirectAsync(p, new OutboundMessage(JsonSerializer.Serialize(new Dictionary<string, string> { ["endpoint"] = sub.Endpoint, ["p256dh"] = sub.P256dh, ["auth"] = sub.Auth }),
                            $"{s.ApplicationName} test", text, null, new Dictionary<string, string> { ["Link"] = "/admin/communication" }, "provider_test"), ct);
                    masked = $"{subs.Count} device(s)";
                    log.MaskedRecipient = masked;
                    break;
            }
            log.Status = QueueStatus.Sent;
            db.CommunicationLogs.Add(log);
            await db.SaveChangesAsync(ct);
            await audit.LogAdminAsync(AdminId, "provider_test_send", null, $"{p.Name} → {masked}: ok", ct);
            return new ProviderTestResult(true, $"Test message sent to {masked}.");
        }
        catch (ValidationAppException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Status = QueueStatus.Failed; log.FailureReason = Scrub(ex.Message);
            db.CommunicationLogs.Add(log);
            await db.SaveChangesAsync(ct);
            await audit.LogAdminAsync(AdminId, "provider_test_send", null, $"{p.Name} → {masked}: failed", ct);
            return new ProviderTestResult(false, log.FailureReason);
        }
    }

    public object GenerateVapidKeys()
    {
        var (pub, priv) = gateway.GenerateVapidKeys();
        return new { publicKey = pub, privateKey = priv };
    }

    private static string Scrub(string msg)
    {
        msg = System.Text.RegularExpressions.Regex.Replace(msg ?? "", @"https?://\S+", "[url]");
        return msg.Length > 480 ? msg[..480] : msg;
    }

    // ---------------------------------------------------------------- policies
    public async Task<IReadOnlyList<PolicyDto>> PoliciesAsync(CancellationToken ct) =>
        await db.NotificationPolicies.AsNoTracking().OrderBy(p => p.DisplayName)
            .Select(p => new PolicyDto(p.EventType, p.DisplayName, p.InAppEnabled, p.EmailEnabled, p.SmsEnabled, p.PushEnabled, p.WhatsAppEnabled, p.IsSecurityCritical, p.UserCanOptOut)).ToListAsync(ct);

    /// <summary>Updates the control matrix. Security-critical events must keep at least one of Email / SMS and can't become user-optional.</summary>
    public async Task<IReadOnlyList<PolicyDto>> SavePoliciesAsync(List<PolicyUpdate> updates, CancellationToken ct)
    {
        var all = await db.NotificationPolicies.ToDictionaryAsync(p => p.EventType, ct);
        var errors = new Dictionary<string, string[]>();
        foreach (var u in updates)
        {
            if (!all.TryGetValue(u.EventType, out var p)) continue;
            if (p.IsSecurityCritical && !u.EmailEnabled && !u.SmsEnabled)
            { errors[u.EventType] = [$"{p.DisplayName} is security-critical: keep Email or SMS enabled."]; continue; }
            p.InAppEnabled = p.IsSecurityCritical || u.InAppEnabled; p.EmailEnabled = u.EmailEnabled; p.SmsEnabled = u.SmsEnabled; p.PushEnabled = u.PushEnabled;
            p.WhatsAppEnabled = u.WhatsAppEnabled; p.UserCanOptOut = !p.IsSecurityCritical && u.UserCanOptOut; p.UpdatedAt = Now; p.UpdatedBy = AdminId;
        }
        if (errors.Count > 0) throw new ValidationAppException(errors);
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "notification_policies_updated", null, $"{updates.Count} event(s)", ct);
        return await PoliciesAsync(ct);
    }

    // ---------------------------------------------------------------- templates
    public async Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct) =>
        await db.NotificationTemplates.AsNoTracking().OrderBy(t => t.EventType).ThenBy(t => t.Channel)
            .Select(t => new TemplateDto(t.Id, t.EventType, t.Channel, t.Subject, t.Body, t.ProviderTemplateId, t.IsActive, t.UpdatedAt)).ToListAsync(ct);

    public async Task<TemplateDto> SaveTemplateAsync(Guid? id, TemplateRequest r, CancellationToken ct)
    {
        if (!NotificationEvents.All.Contains(r.EventType)) throw new ValidationAppException("eventType", "Unknown event.");
        if (r.Channel == NotificationChannel.InApp) throw new ValidationAppException("channel", "In-app notifications don't use templates.");
        if (r.Channel == NotificationChannel.Sms && r.Body.Length > 1000) throw new ValidationAppException("body", "SMS templates must be under 1000 characters.");
        NotificationTemplate t;
        if (id is { } e) t = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == e, ct) ?? throw new NotFoundException("Template");
        else
        {
            if (await db.NotificationTemplates.AnyAsync(x => x.EventType == r.EventType && x.Channel == r.Channel, ct)) throw new ConflictException("A template for this event and channel already exists.");
            t = new NotificationTemplate { EventType = r.EventType, Channel = r.Channel }; db.NotificationTemplates.Add(t);
        }
        t.Subject = TextUtil.Clean(r.Subject, 300); t.Body = r.Body; t.ProviderTemplateId = TextUtil.Clean(r.ProviderTemplateId, 100); t.IsActive = r.IsActive; t.UpdatedAt = Now; t.UpdatedBy = AdminId;
        await db.SaveChangesAsync(ct);
        await audit.LogAdminAsync(AdminId, "template_saved", null, $"{t.EventType}/{t.Channel}", ct);
        return new TemplateDto(t.Id, t.EventType, t.Channel, t.Subject, t.Body, t.ProviderTemplateId, t.IsActive, t.UpdatedAt);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct) => await db.NotificationTemplates.Where(t => t.Id == id).ExecuteDeleteAsync(ct);

    public static readonly Dictionary<string, string> SampleVariables = new()
    {
        ["UserName"] = "Rahul Sharma", ["OTP"] = "482913", ["ApplicationName"] = "NIJMITRA", ["EventDate"] = "18 Nov 2026", ["EventTime"] = "10:30 AM",
        ["Title"] = "Team meeting", ["Body"] = "Your meeting starts in 15 minutes.", ["Link"] = "https://nijmitra.com", ["ExpiryMinutes"] = "10",
        ["Browser"] = "Chrome 130", ["IpAddress"] = "203.0.113.10", ["Location"] = "Office"
    };

    public async Task<TemplatePreview> PreviewAsync(TemplateRequest r, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var vars = new Dictionary<string, string>(SampleVariables) { ["ApplicationName"] = s.ApplicationName };
        if (r.Channel == NotificationChannel.Email)
            return new TemplatePreview(TemplateRenderer.Render(r.Subject ?? "", vars, false),
                TemplateRenderer.WrapEmail(TemplateRenderer.Render(r.Body, vars, true), s.ApplicationName, app.Value.PublicBaseUrl.TrimEnd('/') + "/branding/logo-email.png", s.Tagline));
        return new TemplatePreview(TemplateRenderer.Render(r.Subject ?? "", vars, false), TemplateRenderer.Render(r.Body, vars, false));
    }

    public async Task<ProviderTestResult> TestTemplateAsync(TemplateRequest r, string recipient, CancellationToken ct)
    {
        var provider = await db.CommunicationProviders.AsNoTracking().Where(p => p.Channel == r.Channel && p.IsActive).OrderByDescending(p => p.IsDefault).ThenBy(p => p.Priority).FirstOrDefaultAsync(ct)
                       ?? throw new ValidationAppException("channel", $"No active {r.Channel} provider is configured.");
        var preview = await PreviewAsync(r, ct);
        try
        {
            var to = r.Channel == NotificationChannel.Email ? recipient.Trim() : Auth.AuthService.ValidateMobile(recipient);
            await gateway.SendDirectAsync(provider, new OutboundMessage(to, preview.Subject, preview.Body, r.ProviderTemplateId, SampleVariables, r.EventType), ct);
            return new ProviderTestResult(true, "Test sent with sample values.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ValidationAppException)) { return new ProviderTestResult(false, Scrub(ex.Message)); }
    }

    // ---------------------------------------------------------------- logs & queue
    public async Task<PagedResult<LogRow>> LogsAsync(LogQuery q, CancellationToken ct)
    {
        var query = db.CommunicationLogs.AsNoTracking().AsQueryable();
        if (q.Channel is { } c) query = query.Where(l => l.Channel == c);
        if (q.Status is { } s) query = query.Where(l => l.Status == s);
        if (!string.IsNullOrWhiteSpace(q.EventType)) query = query.Where(l => l.EventType == q.EventType);
        if (q.From is { } f) query = query.Where(l => l.CreatedAt >= f);
        if (q.To is { } t) query = query.Where(l => l.CreatedAt <= t);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var like = TextUtil.LikePattern(q.Search); query = query.Where(l => EF.Functions.ILike(l.MaskedRecipient, like) || (l.ProviderName != null && EF.Functions.ILike(l.ProviderName, like)) || (l.FailureReason != null && EF.Functions.ILike(l.FailureReason, like))); }
        return await query.OrderByDescending(l => l.CreatedAt)
            .Select(l => new LogRow(l.Id, l.CreatedAt, l.MaskedRecipient, l.Channel, l.EventType, l.ProviderName, l.Status, l.FailureReason, l.Attempt, l.WillRetry)).ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<QueueStats> QueueStatsAsync(CancellationToken ct)
    {
        var g = await db.NotificationQueue.AsNoTracking().GroupBy(q => q.Status).Select(x => new { x.Key, C = x.Count() }).ToListAsync(ct);
        int C(QueueStatus s) => g.FirstOrDefault(x => x.Key == s)?.C ?? 0;
        return new QueueStats(C(QueueStatus.Pending), C(QueueStatus.Processing), C(QueueStatus.Sent), C(QueueStatus.Failed), C(QueueStatus.Dead));
    }

    // ---------------------------------------------------------------- announcements
    public async Task<AnnouncementDto> SendAnnouncementAsync(AnnouncementRequest r, CancellationToken ct)
    {
        if (!r.SendInApp && !r.SendEmail && !r.SendPush && !r.SendSms) throw new ValidationAppException("channels", "Choose at least one channel.");
        var users = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active);
        switch (r.Target)
        {
            case AnnouncementTarget.ActiveUsers: users = users.Where(u => u.LastLoginAt >= Now.AddDays(-30)); break;
            case AnnouncementTarget.SelectedUsers:
                var ids = (r.TargetValue ?? "").Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToUpperInvariant()).ToList();
                if (ids.Count == 0) throw new ValidationAppException("targetValue", "Enter one or more NIJMITRA IDs (e.g. NM100025).");
                users = users.Where(u => ids.Contains(u.PublicId));
                break;
            case AnnouncementTarget.Role:
                if (string.IsNullOrWhiteSpace(r.TargetValue)) throw new ValidationAppException("targetValue", "Choose a role.");
                users = users.Where(u => u.UserRoles.Any(x => x.Role!.Name == r.TargetValue));
                break;
        }
        var recipients = await users.Select(u => u.Id).ToListAsync(ct);
        if (recipients.Count == 0) throw new ValidationAppException("target", "No users match this audience.");
        if (recipients.Count > 50000) throw new ValidationAppException("target", "Audience too large for a single announcement.");
        var a = new Announcement { Title = r.Title.Trim(), Body = r.Body.Trim(), Target = r.Target, TargetValue = TextUtil.Clean(r.TargetValue, 2000), SendInApp = r.SendInApp,
            SendEmail = r.SendEmail, SendPush = r.SendPush, SendSms = r.SendSms, RecipientCount = recipients.Count, CreatedBy = AdminId, CreatedAt = Now };
        db.Announcements.Add(a);
        await db.SaveChangesAsync(ct);
        // Temporarily scope the announcement policy to the channels the admin picked.
        var policy = await db.NotificationPolicies.FirstOrDefaultAsync(p => p.EventType == NotificationEvents.Announcement, ct);
        var original = policy == null ? null : new { policy.InAppEnabled, policy.EmailEnabled, policy.PushEnabled, policy.SmsEnabled };
        if (policy != null) { policy.InAppEnabled = r.SendInApp; policy.EmailEnabled = r.SendEmail; policy.PushEnabled = r.SendPush; policy.SmsEnabled = r.SendSms; await db.SaveChangesAsync(ct); }
        try
        {
            foreach (var uid in recipients)
                await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.Announcement, uid, a.Title, a.Body, "/notifications", DedupeKey: $"ann:{a.Id}:{uid}"), ct);
        }
        finally
        {
            if (policy != null && original != null) { policy.InAppEnabled = original.InAppEnabled; policy.EmailEnabled = original.EmailEnabled; policy.PushEnabled = original.PushEnabled; policy.SmsEnabled = original.SmsEnabled; await db.SaveChangesAsync(ct); }
        }
        await audit.LogAdminAsync(AdminId, "announcement_sent", null, $"'{a.Title}' to {recipients.Count} user(s)", ct);
        return (await AnnouncementsAsync(1, 1, ct)).Items[0];
    }

    public async Task<PagedResult<AnnouncementDto>> AnnouncementsAsync(int page, int size, CancellationToken ct) =>
        await db.Announcements.AsNoTracking().OrderByDescending(a => a.CreatedAt).Select(a => new AnnouncementDto(a.Id, a.Title, a.Body, a.Target, a.TargetValue, a.SendInApp, a.SendEmail,
            a.SendPush, a.SendSms, a.RecipientCount, db.Users.Where(u => u.Id == a.CreatedBy).Select(u => u.FullName).FirstOrDefault() ?? "", a.CreatedAt)).ToPagedAsync(page, size, ct);
}
