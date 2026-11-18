using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Journal;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.BackgroundJobs;

public static class JobRegistration
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<MaintenanceJobs>();
        if (!config.GetValue("BackgroundJobs:Enabled", true)) return;
        services.AddHostedService<JobHost>();
    }
}

/// <summary>Runs scheduled work independently of any browser: reminders, presence, disappearing messages, retention.</summary>
public sealed class JobHost(IServiceScopeFactory scopes, ILogger<JobHost> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastRun = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            await Run("reminder-dispatch", TimeSpan.FromSeconds(15), (j, ct) => j.DispatchRemindersAsync(ct), stoppingToken);
            await Run("presence-sweep", TimeSpan.FromSeconds(30), (j, ct) => j.SweepPresenceAsync(ct), stoppingToken);
            await Run("disappearing", TimeSpan.FromSeconds(30), (j, ct) => j.ExpireMessagesAsync(ct), stoppingToken);
            await Run("reminder-plan", TimeSpan.FromMinutes(5), (j, ct) => j.PlanRemindersAsync(ct), stoppingToken);
            await Run("idle-sessions", TimeSpan.FromMinutes(1), (j, ct) => j.ExpireIdleSessionsAsync(ct), stoppingToken);
            await Run("retention", TimeSpan.FromHours(1), (j, ct) => j.RetentionAsync(ct), stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task Run(string name, TimeSpan every, Func<MaintenanceJobs, CancellationToken, Task> work, CancellationToken ct)
    {
        if (_lastRun.TryGetValue(name, out var last) && DateTime.UtcNow - last < every) return;
        _lastRun[name] = DateTime.UtcNow;
        try
        {
            using var scope = scopes.CreateScope();
            await work(scope.ServiceProvider.GetRequiredService<MaintenanceJobs>(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Background job {Job} failed", name); }
    }
}

public sealed class MaintenanceJobs(AppDbContext db, ReminderPlanner planner, IRealtimeNotifier realtime, IFileStorage storage, ISettingsService settings,
    TimeProvider clock, ILogger<MaintenanceJobs> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task DispatchRemindersAsync(CancellationToken ct) => planner.DispatchDueAsync(ct);
    public Task PlanRemindersAsync(CancellationToken ct) => planner.PlanAllAsync(ct);

    public async Task SweepPresenceAsync(CancellationToken ct)
    {
        var stale = await db.UserPresences.Where(p => p.IsOnline && p.LastHeartbeatAt < Now.AddSeconds(-75)).ToListAsync(ct);
        foreach (var p in stale)
        {
            p.IsOnline = false; p.ConnectionCount = 0;
            var friends = await db.FriendConnections.Where(f => f.UserAId == p.UserId || f.UserBId == p.UserId).Select(f => f.UserAId == p.UserId ? f.UserBId : f.UserAId).ToListAsync(ct);
            await realtime.ToUsersAsync(friends, "presence", new { userId = p.UserId, isOnline = false, lastSeenAt = p.LastSeenAt }, ct);
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }

    public async Task ExpireMessagesAsync(CancellationToken ct)
    {
        var expired = await db.ChatMessages.Include(m => m.Attachments).Where(m => m.ExpiresAt != null && m.ExpiresAt <= Now && m.DeletedAt == null).Take(500).ToListAsync(ct);
        foreach (var m in expired)
        {
            m.DeletedAt = Now; m.Body = null;
            var ids = m.Attachments.Select(a => a.StoredFileId).ToList();
            await db.StoredFiles.IgnoreQueryFilters().Where(f => ids.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.IsDeleted, true).SetProperty(f => f.DeletedAt, Now), ct);
            var participants = await db.ChatParticipants.Where(p => p.ConversationId == m.ConversationId).Select(p => p.UserId).ToListAsync(ct);
            await realtime.ToUsersAsync(participants, "messageExpired", new { m.Id, m.ConversationId }, ct);
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>Closes sessions that went idle without the browser telling us (tab closed, device asleep).</summary>
    public async Task ExpireIdleSessionsAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var cutoff = Now.AddSeconds(-(Math.Max(30, s.SessionIdleTimeoutSeconds) + 5));
        var idle = await db.UserSessions.Where(x => x.RevokedAt == null && x.LastActivityAt < cutoff).Take(1000).ToListAsync(ct);
        foreach (var x in idle) { x.RevokedAt = Now; x.RevokedReason = "idle_timeout"; }
        var ids = idle.Select(x => x.Id).ToList();
        if (ids.Count == 0) return;
        await db.RefreshTokens.Where(t => ids.Contains(t.SessionId) && t.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, Now), ct);
        foreach (var x in idle)
            db.LoginAuditLogs.Add(new Domain.Entities.LoginAuditLog { UserId = x.UserId, Event = "auto_logout", Success = true, FailureReason = "idle_timeout",
                SessionId = x.Id, IpAddress = x.IpAddress, Browser = x.Browser, OperatingSystem = x.OperatingSystem, Device = x.DeviceName, Timestamp = Now });
        await db.SaveChangesAsync(ct);
        foreach (var x in idle) await realtime.ToUserAsync(x.UserId, "sessionEnded", new { sessionId = x.Id, reason = "idle_timeout" }, ct);
    }

    public async Task RetentionAsync(CancellationToken ct)
    {
        await db.OtpCodes.Where(o => o.CreatedAt < Now.AddDays(-2)).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(t => t.ExpiresAt < Now.AddDays(-7)).ExecuteDeleteAsync(ct);
        await db.UserSessions.Where(s => s.RevokedAt != null && s.RevokedAt < Now.AddDays(-90)).ExecuteDeleteAsync(ct);
        await db.NotificationQueue.Where(q => (q.Status == QueueStatus.Sent || q.Status == QueueStatus.Dead) && q.CreatedAt < Now.AddDays(-30)).ExecuteDeleteAsync(ct);
        await db.ReminderSchedules.Where(r => r.Status != ScheduleStatus.Pending && r.CreatedAt < Now.AddDays(-30)).ExecuteDeleteAsync(ct);
        // Security & activity logs are kept for one year (fraud prevention / dispute handling), then removed — as stated in the privacy policy.
        await db.LoginAuditLogs.Where(l => l.Timestamp < Now.AddDays(-365)).ExecuteDeleteAsync(ct);
        await db.AuditLogs.Where(l => l.Timestamp < Now.AddDays(-365)).ExecuteDeleteAsync(ct);
        await db.CommunicationLogs.Where(l => l.CreatedAt < Now.AddDays(-365)).ExecuteDeleteAsync(ct);

        // Physically remove files soft-deleted more than 30 days ago.
        var files = await db.StoredFiles.IgnoreQueryFilters().Where(f => f.IsDeleted && f.DeletedAt < Now.AddDays(-30)).Take(500).ToListAsync(ct);
        foreach (var f in files)
        {
            var referenced = await db.DocumentVersions.AnyAsync(v => v.StoredFileId == f.Id && !v.Document!.IsDeleted, ct);
            if (referenced) continue;
            await storage.DeleteAsync(f.StorageKey, ct);
            db.HardDeleteMode = true;
            db.StoredFiles.Remove(f);
        }
        await db.SaveChangesAsync(ct);
        db.HardDeleteMode = false;

        // Complete account deletions whose grace period has passed.
        var due = await db.AccountDeletionRequests.Where(r => r.Status == DeletionRequestStatus.Pending && r.ScheduledFor <= Now).Take(20).ToListAsync(ct);
        foreach (var req in due)
        {
            try { await PurgeUserAsync(req.UserId, ct); req.Status = DeletionRequestStatus.Completed; req.CompletedAt = Now; await db.SaveChangesAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Account purge failed for request {Id}", req.Id); }
        }
    }

    /// <summary>Irreversibly deletes a user's personal data and files. Audit trails keep only the anonymous user id.</summary>
    public async Task PurgeUserAsync(Guid userId, CancellationToken ct)
    {
        var o = new[] { AppDbContext.OwnerFilter, AppDbContext.SoftDeleteFilter };
        var files = await db.StoredFiles.IgnoreQueryFilters().Where(f => f.UserId == userId).ToListAsync(ct);
        foreach (var f in files) await storage.DeleteAsync(f.StorageKey, ct);

        var convIds = await db.ChatParticipants.Where(p => p.UserId == userId).Select(p => p.ConversationId).ToListAsync(ct);
        await db.ChatMessages.Where(m => convIds.Contains(m.ConversationId)).ExecuteDeleteAsync(ct);
        await db.FriendConnections.Where(f => f.UserAId == userId || f.UserBId == userId).ExecuteDeleteAsync(ct);
        await db.ChatConversations.Where(c => convIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
        await db.FriendRequests.Where(f => f.FromUserId == userId || f.ToUserId == userId).ExecuteDeleteAsync(ct);
        await db.FriendInvitations.Where(f => f.InviterUserId == userId).ExecuteDeleteAsync(ct);
        await db.BlockedUsers.Where(b => b.UserId == userId || b.BlockedUserId == userId).ExecuteDeleteAsync(ct);
        await db.UserPresences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

        await db.CreditPayments.Where(p => db.CreditTransactions.IgnoreQueryFilters(o).Where(t => t.UserId == userId).Select(t => t.Id).Contains(p.TransactionId)).ExecuteDeleteAsync(ct);
        await db.CreditAttachments.Where(p => db.CreditTransactions.IgnoreQueryFilters(o).Where(t => t.UserId == userId).Select(t => t.Id).Contains(p.TransactionId)).ExecuteDeleteAsync(ct);
        await db.CreditReminders.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CreditTransactions.IgnoreQueryFilters(o).Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RecurringExpenseOccurrences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Expenses.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RecurringExpenses.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.BudgetCategories.Where(b => db.MonthlyBudgets.IgnoreQueryFilters(o).Where(m => m.UserId == userId).Select(m => m.Id).Contains(b.MonthlyBudgetId)).ExecuteDeleteAsync(ct);
        await db.MonthlyBudgets.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.IncomeRecords.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ExpenseCategories.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.DocumentShareLinks.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Documents.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.DocumentFolders.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PasswordHistory.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PasswordEntries.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.SecureNotes.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PasswordVaults.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CustomTableRecords.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CustomTables.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Products.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ProductRegisters.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Contacts.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ContactGroups.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.DiaryEntries.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CalendarEvents.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Reminders.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ReminderSchedules.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Notifications.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.NotificationPreferences.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PushSubscriptions.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.RecentSearches.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.NotificationQueue.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.StoredFiles.IgnoreQueryFilters(o).Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserAuthenticatorSecrets.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserDevices.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct); // cascades profile, security settings, sessions, tokens
        logger.LogInformation("Account {UserId} purged after deletion grace period", userId);
    }
}
