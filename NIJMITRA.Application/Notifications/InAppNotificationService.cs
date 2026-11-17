using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;

namespace NIJMITRA.Application.Notifications;

public record NotificationDto(Guid Id, string EventType, string Title, string? Body, string? Link, bool IsRead, DateTime CreatedAt);

public sealed class InAppNotificationService(IAppDbContext db, TimeProvider clock)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().Where(n => !unreadOnly || !n.IsRead).OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.EventType, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt)).ToPagedAsync(page, pageSize, ct);

    public Task<int> UnreadCountAsync(CancellationToken ct) => db.Notifications.CountAsync(n => !n.IsRead, ct);

    public async Task MarkReadAsync(Guid id, CancellationToken ct) =>
        await db.Notifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, clock.GetUtcNow().UtcDateTime), ct);

    public async Task<int> MarkAllReadAsync(CancellationToken ct) =>
        await db.Notifications.Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, clock.GetUtcNow().UtcDateTime), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct) => await db.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync(ct);
}
