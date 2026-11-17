using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NIJMITRA.Application.Abstractions;

namespace NIJMITRA.API.Hubs;

/// <summary>Single real-time hub: notifications, chat events and presence. Users join a private group "u:{id}".</summary>
[Authorize]
public partial class AppHub : Hub
{
    public static string UserGroup(Guid id) => $"u:{id}";
}

public sealed class RealtimeNotifier(IHubContext<AppHub> hub) : IRealtimeNotifier
{
    public Task ToUserAsync(Guid userId, string method, object payload, CancellationToken ct = default) =>
        hub.Clients.Group(AppHub.UserGroup(userId)).SendAsync(method, payload, ct);

    public Task ToUsersAsync(IEnumerable<Guid> userIds, string method, object payload, CancellationToken ct = default) =>
        hub.Clients.Groups(userIds.Distinct().Select(AppHub.UserGroup).ToList()).SendAsync(method, payload, ct);
}
