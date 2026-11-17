using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Notifications;

/// <summary>
/// Central notification engine. Effective delivery for every channel is:
/// platform policy (admin matrix) → channel provider availability → user preference → mandatory security rules.
/// Security-critical events ignore user opt-outs. External deliveries are queued in an encrypted outbox.
/// </summary>
public sealed class NotificationService(
    IAppDbContext db, IFieldEncryptor enc, IRealtimeNotifier realtime, ISettingsService settings,
    IOptions<AppOptions> app, TimeProvider clock, ILogger<NotificationService> logger) : INotificationService
{
    public async Task<DispatchSummary> DispatchAsync(NotificationRequest req, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var skipped = new List<string>();
        var queued = new List<NotificationChannel>();
        var policy = await db.NotificationPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.EventType == req.EventType, ct)
                     ?? new NotificationPolicy { EventType = req.EventType, InAppEnabled = true };
        var platform = await settings.GetAsync(ct);

        User? user = null;
        NotificationPreference? pref = null;
        if (req.UserId is { } uid)
        {
            user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
            pref = await db.NotificationPreferences.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == uid, ct);
        }
        var critical = policy.IsSecurityCritical;
        var categoryAllowed = critical || pref == null || CategoryAllowed(req.EventType, pref);

        // ---- In-app ----
        var inApp = false;
        if (user != null && (policy.InAppEnabled || critical) && categoryAllowed)
        {
            var n = new Notification
            {
                UserId = user.Id, EventType = req.EventType, Title = req.Title, Body = req.Body, Link = req.Link, CreatedAt = now
            };
            db.Notifications.Add(n);
            await db.SaveChangesAsync(ct);
            inApp = true;
            try { await realtime.ToUserAsync(user.Id, "notification", new { n.Id, n.Title, n.Body, n.Link, n.EventType, n.CreatedAt }, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "Realtime push failed"); }
        }
        if (req.InAppOnly) return new DispatchSummary(inApp, queued, skipped);
        if (!categoryAllowed) { skipped.Add("user disabled this notification category"); return new DispatchSummary(inApp, queued, skipped); }

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApplicationName"] = platform.ApplicationName,
            ["UserName"] = user?.FullName ?? "User",
            ["Title"] = req.Title,
            ["Body"] = req.Body ?? "",
            ["Link"] = string.IsNullOrEmpty(req.Link) ? app.Value.PublicBaseUrl : app.Value.PublicBaseUrl.TrimEnd('/') + req.Link,
        };
        if (req.Variables != null) foreach (var kv in req.Variables) vars[kv.Key] = kv.Value;

        var activeChannels = await db.CommunicationProviders.AsNoTracking().Where(p => p.IsActive)
            .Select(p => p.Channel).Distinct().ToListAsync(ct);

        foreach (var channel in new[] { NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.Push, NotificationChannel.WhatsApp })
        {
            var enabledByPolicy = channel switch
            {
                NotificationChannel.Email => policy.EmailEnabled,
                NotificationChannel.Sms => policy.SmsEnabled,
                NotificationChannel.Push => policy.PushEnabled,
                NotificationChannel.WhatsApp => policy.WhatsAppEnabled,
                _ => false
            };
            if (!enabledByPolicy) { skipped.Add($"{channel}: disabled by platform policy"); continue; }
            if (!activeChannels.Contains(channel)) { skipped.Add($"{channel}: no active provider configured"); continue; }
            if (!critical && pref != null && !UserChannelAllowed(channel, pref)) { skipped.Add($"{channel}: disabled by user preference"); continue; }

            var recipient = channel switch
            {
                NotificationChannel.Email => req.OverrideEmail ?? (user is { Email: not null } && (user.EmailVerified || !platform.RequireEmailVerification) ? user.Email : null),
                NotificationChannel.Sms or NotificationChannel.WhatsApp => req.OverrideMobile ?? user?.MobileNumber,
                NotificationChannel.Push => user == null ? null : user.Id.ToString(),
                _ => null
            };
            if (string.IsNullOrEmpty(recipient)) { skipped.Add($"{channel}: no recipient address"); continue; }
            if (channel == NotificationChannel.Push && !await db.PushSubscriptions.IgnoreQueryFilters().AnyAsync(s => s.UserId == user!.Id, ct))
            { skipped.Add("Push: user has no subscribed devices"); continue; }

            var template = await db.NotificationTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.EventType == req.EventType && t.Channel == channel && t.IsActive, ct);
            var subject = TemplateRenderer.Render(template?.Subject ?? req.Title, vars, false);
            string body;
            if (channel == NotificationChannel.Email)
            {
                var inner = TemplateRenderer.Render(template?.Body ?? "<p>Hello {{UserName}},</p><p>{{Body}}</p>", vars, true);
                body = TemplateRenderer.WrapEmail(inner, platform.ApplicationName, app.Value.PublicBaseUrl.TrimEnd('/') + "/branding/logo-email.png", platform.Tagline);
            }
            else body = TemplateRenderer.Render(template?.Body ?? (req.Body ?? req.Title), vars, false);

            var dedupeBase = req.DedupeKey ?? Guid.NewGuid().ToString("N");
            var item = new NotificationQueueItem
            {
                UserId = user?.Id, EventType = req.EventType, Channel = channel,
                EncryptedRecipient = enc.Encrypt(recipient),
                MaskedRecipient = channel == NotificationChannel.Email ? TextUtil.MaskEmail(recipient) : channel == NotificationChannel.Push ? "user devices" : TextUtil.MaskMobile(recipient),
                Subject = channel == NotificationChannel.Push ? subject : Truncate(subject, 300),
                EncryptedBody = enc.Encrypt(body),
                EncryptedVariables = enc.Encrypt(System.Text.Json.JsonSerializer.Serialize(new { templateId = template?.ProviderTemplateId, vars })),
                DedupeKey = $"{dedupeBase}:{channel}:{enc.Hash(recipient)[..16]}",
                NextAttemptAt = now, CreatedAt = now,
                MaxAttempts = critical ? 3 : 5
            };
            if (await db.NotificationQueue.AnyAsync(q => q.DedupeKey == item.DedupeKey, ct)) { skipped.Add($"{channel}: duplicate suppressed"); continue; }
            db.NotificationQueue.Add(item);
            try
            {
                await db.SaveChangesAsync(ct);
                queued.Add(channel);
            }
            catch (DbUpdateException)
            {
                db.NotificationQueue.Entry(item).State = EntityState.Detached;
                skipped.Add($"{channel}: duplicate suppressed");
            }
        }
        return new DispatchSummary(inApp, queued, skipped);
    }

    private static bool UserChannelAllowed(NotificationChannel c, NotificationPreference p) => c switch
    {
        NotificationChannel.Email => p.EmailEnabled,
        NotificationChannel.Sms or NotificationChannel.WhatsApp => p.SmsEnabled,
        NotificationChannel.Push => p.PushEnabled,
        _ => true
    };

    private static bool CategoryAllowed(string evt, NotificationPreference p) => evt switch
    {
        NotificationEvents.MeetingReminder => p.MeetingReminders,
        NotificationEvents.ExpenseReminder or NotificationEvents.BudgetAlert => p.ExpenseReminders,
        NotificationEvents.UdharReminder => p.UdharReminders,
        NotificationEvents.GeneralReminder => p.GeneralReminders,
        NotificationEvents.FriendRequest => p.FriendRequests,
        NotificationEvents.ChatMessage => p.ChatMessages,
        NotificationEvents.LoginAlert => p.LoginAlerts,
        NotificationEvents.Announcement => true,
        _ => true
    };

    private static string Truncate(string s, int n) => s.Length > n ? s[..n] : s;
}
