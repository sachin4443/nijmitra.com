using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.Infrastructure.Notifications;

/// <summary>Loads providers and decrypts their secrets just-in-time for sending/testing.</summary>
public sealed class ProviderResolver(IAppDbContext db, IFieldEncryptor enc, IEnumerable<IChannelProvider> implementations)
{
    public IChannelProvider Implementation(ProviderType type) =>
        implementations.FirstOrDefault(p => p.Type == type) ?? throw new ProviderException($"No implementation registered for {type}.");

    public ProviderContext Context(CommunicationProvider p)
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, string?>>(p.SettingsJson) ?? [];
        var secrets = string.IsNullOrEmpty(p.EncryptedSecretsJson) ? [] :
            JsonSerializer.Deserialize<Dictionary<string, string?>>(enc.Decrypt(p.EncryptedSecretsJson)) ?? [];
        return new ProviderContext(p.Id, p.Name, p.ProviderType, settings, secrets);
    }

    public Task<List<CommunicationProvider>> ActiveForAsync(NotificationChannel channel, CancellationToken ct) =>
        db.CommunicationProviders.AsNoTracking().Where(p => p.Channel == channel && p.IsActive)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Priority).ToListAsync(ct);
}

/// <summary>
/// Background outbox processor: claims due queue items, tries active providers in priority order (failover),
/// records a CommunicationLog per attempt, retries with exponential backoff, and wipes message bodies after delivery.
/// Runs independently of any browser session.
/// </summary>
public sealed class NotificationDispatcher(IServiceScopeFactory scopes, ILogger<NotificationDispatcher> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Notification dispatcher batch failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enc = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var resolver = scope.ServiceProvider.GetRequiredService<ProviderResolver>();
        var now = clock.GetUtcNow().UtcDateTime;

        // Reclaim items stuck in Processing (e.g. after a crash).
        await db.NotificationQueue.Where(q => q.Status == QueueStatus.Processing && q.NextAttemptAt < now.AddMinutes(-5))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QueueStatus.Pending), ct);

        var ids = await db.NotificationQueue.Where(q => q.Status == QueueStatus.Pending && q.NextAttemptAt <= now)
            .OrderBy(q => q.NextAttemptAt).Select(q => q.Id).Take(25).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            var claimed = await db.NotificationQueue.Where(q => q.Id == id && q.Status == QueueStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QueueStatus.Processing).SetProperty(q => q.NextAttemptAt, now), ct);
            if (claimed == 0) continue;
            var item = await db.NotificationQueue.FirstAsync(q => q.Id == id, ct);
            await DeliverAsync(db, enc, resolver, item, ct);
            processed++;
        }
        return processed;
    }

    private async Task DeliverAsync(AppDbContext db, IFieldEncryptor enc, ProviderResolver resolver, NotificationQueueItem item, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        item.Attempts++;
        var providers = await resolver.ActiveForAsync(item.Channel, ct);
        string? lastError = providers.Count == 0 ? "No active provider is configured for this channel." : null;
        var delivered = false;
        var permanent = providers.Count == 0;

        if (providers.Count > 0 && item.EncryptedBody != null)
        {
            var recipient = enc.Decrypt(item.EncryptedRecipient);
            var body = enc.Decrypt(item.EncryptedBody);
            var meta = item.EncryptedVariables == null ? null : JsonSerializer.Deserialize<QueueMeta>(enc.Decrypt(item.EncryptedVariables));
            var vars = meta?.Vars ?? new Dictionary<string, string>();
            foreach (var provider in providers)
            {
                try
                {
                    var impl = resolver.Implementation(provider.ProviderType);
                    var ctx = resolver.Context(provider);
                    if (item.Channel == NotificationChannel.Push)
                    {
                        var userId = Guid.Parse(recipient);
                        var subs = await db.PushSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == userId).ToListAsync(ct);
                        var anyOk = false;
                        foreach (var s in subs)
                        {
                            try
                            {
                                var subJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["endpoint"] = s.Endpoint, ["p256dh"] = s.P256dh, ["auth"] = s.Auth });
                                await impl.SendAsync(ctx, new OutboundMessage(subJson, item.Subject, body, meta?.TemplateId, vars, item.EventType), ct);
                                s.LastSuccessAt = now; anyOk = true;
                            }
                            catch (PushSubscriptionGoneException) { db.PushSubscriptions.Remove(s); }
                        }
                        if (!anyOk) throw new ProviderException("No reachable push subscriptions.");
                    }
                    else
                    {
                        await impl.SendAsync(ctx, new OutboundMessage(recipient, item.Subject, body, meta?.TemplateId, vars, item.EventType), ct);
                    }
                    delivered = true;
                    item.ProviderId = provider.Id;
                    Log(db, item, provider.Name, QueueStatus.Sent, null, false, now);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = SafeError(ex);
                    permanent = ex is ProviderException { Transient: false } && providers.Count == 1 && item.Attempts >= 2;
                    Log(db, item, provider.Name, QueueStatus.Failed, lastError, true, now);
                    logger.LogWarning("Delivery via {Provider} failed for queue item {Id}: {Error}", provider.Name, item.Id, lastError);
                }
            }
        }

        if (delivered)
        {
            item.Status = QueueStatus.Sent; item.SentAt = now; item.LastError = null;
            item.EncryptedBody = null; item.EncryptedVariables = null; // never keep message content (e.g. OTPs) after delivery
        }
        else
        {
            item.LastError = lastError;
            if (item.Attempts >= item.MaxAttempts || permanent)
            {
                item.Status = QueueStatus.Dead; item.EncryptedBody = null; item.EncryptedVariables = null;
                var last = db.ChangeTracker.Entries<CommunicationLog>().LastOrDefault(e => e.State == EntityState.Added)?.Entity;
                if (last != null) last.WillRetry = false;
                else Log(db, item, null, QueueStatus.Dead, lastError, false, now);
            }
            else
            {
                item.Status = QueueStatus.Pending;
                item.NextAttemptAt = now.AddSeconds(30 * Math.Pow(2, item.Attempts - 1));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static void Log(AppDbContext db, NotificationQueueItem item, string? provider, QueueStatus status, string? error, bool willRetry, DateTime now) =>
        db.CommunicationLogs.Add(new CommunicationLog
        {
            UserId = item.UserId, QueueItemId = item.Id, Channel = item.Channel, EventType = item.EventType,
            MaskedRecipient = item.MaskedRecipient, ProviderName = provider, Status = status, FailureReason = error,
            Attempt = item.Attempts, WillRetry = willRetry && item.Attempts < item.MaxAttempts, CreatedAt = now
        });

    /// <summary>Strips anything that could contain secrets (URLs with keys, tokens) from provider errors.</summary>
    private static string SafeError(Exception ex)
    {
        var msg = ex is ProviderException or PushSubscriptionGoneException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
        msg = System.Text.RegularExpressions.Regex.Replace(msg, @"https?://\S+", "[url]");
        msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\b\d{4,8}\b", "[redacted]");
        return msg.Length > 480 ? msg[..480] : msg;
    }

    private sealed record QueueMeta(string? TemplateId, Dictionary<string, string> Vars)
    {
        [System.Text.Json.Serialization.JsonPropertyName("templateId")] public string? TemplateId { get; init; } = TemplateId;
        [System.Text.Json.Serialization.JsonPropertyName("vars")] public Dictionary<string, string> Vars { get; init; } = Vars;
    }
}

public sealed class ProviderGateway(ProviderResolver resolver) : IProviderGateway
{
    public Task<ProviderTestResult> TestConnectionAsync(CommunicationProvider provider, CancellationToken ct) =>
        resolver.Implementation(provider.ProviderType).TestConnectionAsync(resolver.Context(provider), ct);

    public Task SendDirectAsync(CommunicationProvider provider, OutboundMessage message, CancellationToken ct) =>
        resolver.Implementation(provider.ProviderType).SendAsync(resolver.Context(provider), message, ct);

    public (string PublicKey, string PrivateKey) GenerateVapidKeys() => WebPushProvider.GenerateKeys();
}
