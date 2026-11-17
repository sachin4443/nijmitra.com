using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Chat;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.Hubs;

public partial class AppHub(AppDbContext db, ChatService chat, ICurrentUser current, TimeProvider clock) : Hub
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public override async Task OnConnectedAsync()
    {
        var uid = current.RequireUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(uid));
        if (current.IsImpersonating) { await base.OnConnectedAsync(); return; } // support sessions don't affect presence
        var p = await db.UserPresences.FirstOrDefaultAsync(x => x.UserId == uid);
        if (p == null) { p = new UserPresence { UserId = uid }; db.UserPresences.Add(p); }
        var wasOnline = p.IsOnline && p.LastHeartbeatAt > Now.AddSeconds(-75);
        p.ConnectionCount = Math.Max(0, p.ConnectionCount) + 1; p.IsOnline = true; p.LastHeartbeatAt = Now; p.LastSeenAt = Now;
        await db.SaveChangesAsync();
        await chat.MarkAllDeliveredAsync(uid, Context.ConnectionAborted);
        if (!wasOnline) await BroadcastPresenceAsync(uid, true);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (current.UserId is { } uid && !current.IsImpersonating)
        {
            var p = await db.UserPresences.FirstOrDefaultAsync(x => x.UserId == uid);
            if (p != null)
            {
                p.ConnectionCount = Math.Max(0, p.ConnectionCount - 1);
                p.LastSeenAt = Now;
                if (p.ConnectionCount == 0) p.IsOnline = false;
                await db.SaveChangesAsync();
                if (!p.IsOnline) await BroadcastPresenceAsync(uid, false);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client heartbeat every ~25 s keeps presence accurate; stale connections are swept by a background job.</summary>
    public async Task Heartbeat()
    {
        if (current.IsImpersonating) return;
        var uid = current.RequireUserId();
        await db.UserPresences.Where(p => p.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastHeartbeatAt, Now).SetProperty(p => p.IsOnline, true).SetProperty(p => p.LastSeenAt, Now));
    }

    public async Task Typing(Guid conversationId, bool isTyping)
    {
        if (current.IsImpersonating) return;
        var ids = await chat.ParticipantIdsAsync(conversationId, Context.ConnectionAborted);
        var me = current.RequireUserId();
        await Clients.Groups(ids.Where(i => i != me).Select(UserGroup).ToList()).SendAsync("typing", new { conversationId, userId = me, isTyping });
    }

    public async Task MarkRead(Guid conversationId)
    {
        if (current.IsImpersonating) return;
        await chat.MarkAsync(conversationId, true, Context.ConnectionAborted);
    }

    private async Task BroadcastPresenceAsync(Guid uid, bool online)
    {
        var friends = await chat.FriendIdsAsync(uid, Context.ConnectionAborted);
        if (friends.Count > 0) await Clients.Groups(friends.Select(UserGroup).ToList()).SendAsync("presence", new { userId = uid, isOnline = online, lastSeenAt = Now });
    }
}

/// <summary>Re-validates the session on every hub call so revoked / idle-expired sessions lose real-time access immediately.</summary>
public sealed class SessionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await EnsureAsync(ctx.ServiceProvider, ctx.Context);
        return await next(ctx);
    }

    public async Task OnConnectedAsync(HubLifetimeContext ctx, Func<HubLifetimeContext, Task> next)
    {
        await EnsureAsync(ctx.ServiceProvider, ctx.Context);
        await next(ctx);
    }

    private static async Task EnsureAsync(IServiceProvider sp, HubCallerContext hub)
    {
        var current = sp.GetRequiredService<ICurrentUser>();
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var settings = sp.GetRequiredService<ISettingsService>();
        var s = await settings.GetAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var ok = current.SessionId is { } sid && await db.UserSessions.AnyAsync(x => x.Id == sid && x.RevokedAt == null && x.AbsoluteExpiresAt > now
                                                                                    && x.LastActivityAt > now.AddSeconds(-(Math.Max(30, s.SessionIdleTimeoutSeconds) + 5)));
        if (!ok) { hub.Abort(); throw new HubException("session_expired"); }
    }
}
