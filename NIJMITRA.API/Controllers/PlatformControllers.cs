using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Chat;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Application.Search;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.Controllers;

[Route("api/search")]
public class SearchController(SearchService svc) : AppControllerBase
{
    [HttpGet] public Task<SearchResponse> Search([FromQuery] string q, [FromQuery] string? modules, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => svc.SearchAsync(q, modules?.Split(',', StringSplitOptions.RemoveEmptyEntries), page, pageSize, ct);
    [HttpGet("recent")] public Task<IReadOnlyList<string>> Recent(CancellationToken ct) => svc.RecentAsync(ct);
    [HttpDelete("recent")] public async Task<IActionResult> Clear(CancellationToken ct) { await svc.ClearRecentAsync(ct); return NoContent(); }
}

[Route("api/dashboard")]
public class DashboardController(DashboardService svc) : AppControllerBase
{
    [HttpGet] public Task<DashboardDto> Get(CancellationToken ct) => svc.GetAsync(ct);
}

[Route("api/notifications")]
public class NotificationsController(InAppNotificationService svc) : AppControllerBase
{
    [HttpGet] public Task<PagedResult<NotificationDto>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => svc.ListAsync(unreadOnly, page, pageSize, ct);
    [HttpGet("unread-count")] public async Task<IActionResult> Unread(CancellationToken ct) => Ok(new { count = await svc.UnreadCountAsync(ct) });
    [HttpPost("{id:guid}/read")] public async Task<IActionResult> Read(Guid id, CancellationToken ct) { await svc.MarkReadAsync(id, ct); return NoContent(); }
    [HttpPost("read-all")] public async Task<IActionResult> ReadAll(CancellationToken ct) => Ok(new { updated = await svc.MarkAllReadAsync(ct) });
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}

[Route("api/chat"), DenyImpersonation]
public class ChatController(ChatService svc) : AppControllerBase
{
    [HttpGet("users/{publicId}")] public Task<UserCardDto> Find(string publicId, CancellationToken ct) => svc.FindByPublicIdAsync(publicId, ct);
    [HttpGet("invitations")] public Task<IReadOnlyList<InvitationDto>> Invitations(CancellationToken ct) => svc.GetInvitationsAsync(ct);
    [HttpPost("invitations")] public Task<InvitationDto> Invite(InvitationRequest r, CancellationToken ct) => svc.CreateInvitationAsync(r, ct);
    [HttpDelete("invitations/{id:guid}")] public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) { await svc.RevokeInvitationAsync(id, ct); return NoContent(); }
    [HttpPost("invitations/accept")] public Task<FriendDto> Accept([FromQuery] string code, CancellationToken ct) => svc.AcceptInvitationAsync(code, ct);
    [HttpGet("requests")] public Task<IReadOnlyList<FriendRequestDto>> Requests([FromQuery] bool incoming = true, CancellationToken ct = default) => svc.GetRequestsAsync(incoming, ct);
    [HttpPost("requests")] public Task<FriendRequestDto> Send(SendFriendRequest r, CancellationToken ct) => svc.SendRequestAsync(r, ct);
    [HttpPost("requests/{id:guid}/accept")] public async Task<IActionResult> AcceptReq(Guid id, CancellationToken ct) { await svc.RespondAsync(id, true, ct); return NoContent(); }
    [HttpPost("requests/{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, CancellationToken ct) { await svc.RespondAsync(id, false, ct); return NoContent(); }
    [HttpDelete("requests/{id:guid}")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await svc.CancelRequestAsync(id, ct); return NoContent(); }
    [HttpGet("friends")] public Task<IReadOnlyList<FriendDto>> Friends(CancellationToken ct) => svc.GetFriendsAsync(ct);
    [HttpDelete("friends/{userId:guid}")] public async Task<IActionResult> Unfriend(Guid userId, CancellationToken ct) { await svc.UnfriendAsync(userId, ct); return NoContent(); }
    [HttpPost("users/{userId:guid}/block")] public async Task<IActionResult> Block(Guid userId, [FromQuery] bool value = true, CancellationToken ct = default) { await svc.BlockAsync(userId, value, ct); return NoContent(); }
    [HttpPost("users/{userId:guid}/report")] public async Task<IActionResult> Report(Guid userId, ReportRequest r, CancellationToken ct) { await svc.ReportAsync(userId, r, ct); return NoContent(); }

    [HttpGet("conversations/{id:guid}/messages")]
    public Task<IReadOnlyList<MessageDto>> Messages(Guid id, [FromQuery] DateTime? before, [FromQuery] string? search, [FromQuery] int take = 40, CancellationToken ct = default) => svc.GetMessagesAsync(id, before, search, take, ct);
    [HttpPost("conversations/{id:guid}/messages")] public Task<MessageDto> SendMessage(Guid id, SendMessageRequest r, CancellationToken ct) => svc.SendAsync(id, r, ct);
    [HttpDelete("messages/{id:guid}")] public async Task<IActionResult> DeleteMessage(Guid id, CancellationToken ct) { await svc.DeleteMessageAsync(id, ct); return NoContent(); }
    [HttpPost("conversations/{id:guid}/read")] public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) { await svc.MarkAsync(id, true, ct); return NoContent(); }
    [HttpPut("conversations/{id:guid}/disappearing")] public async Task<IActionResult> Disappearing(Guid id, DisappearingRequest r, CancellationToken ct) => Ok(new { seconds = await svc.SetDisappearingAsync(id, r.Seconds, ct) });
    [HttpGet("messages/{messageId:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> File(Guid messageId, Guid fileId, [FromQuery] bool inline = false, CancellationToken ct = default) => Stream(await svc.OpenAttachmentAsync(messageId, fileId, ct), inline);
}

/// <summary>Public bootstrap info for the SPA (no secrets).</summary>
[ApiController, AllowAnonymous, Route("api/system")]
public class SystemController(ISettingsService settings, IAppDbContext db, NIJMITRA.Application.Files.FileService files) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> Info(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var push = await db.CommunicationProviders.AsNoTracking().Where(p => p.Channel == NotificationChannel.Push && p.IsActive).OrderByDescending(p => p.IsDefault).Select(p => p.SettingsJson).FirstOrDefaultAsync(ct);
        string? vapid = null;
        if (push != null) vapid = System.Text.Json.JsonDocument.Parse(push).RootElement.TryGetProperty("publicKey", out var k) ? k.GetString() : null;
        return Ok(new
        {
            s.ApplicationName, s.Tagline, s.HindiTagline, s.MaintenanceMode, s.MaintenanceMessage, s.AllowRegistration, s.ChatEnabled, s.SupportEmail,
            logoUrl = s.LogoFileId != null ? "/api/system/branding/logo" : null, faviconUrl = s.FaviconFileId != null ? "/api/system/branding/favicon" : null,
            vapidPublicKey = vapid, maxUploadMb = s.MaxUploadMb, allowedExtensions = s.AllowedExtensions, website = s.Website, accountDeletionGraceDays = s.AccountDeletionGraceDays
        });
    }

    [HttpGet("branding/{kind}")]
    public async Task<IActionResult> Branding(string kind, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var id = kind == "favicon" ? s.FaviconFileId : kind == "logo" ? s.LogoFileId : null;
        if (id == null || !Guid.TryParse(id, out var fid)) return NotFound();
        var f = await files.OpenAuthorizedAsync(fid, ct);
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(f.Content, f.ContentType);
    }
}

/// <summary>Digital Asset Links for the NijMitra Android app (Trusted Web Activity). Managed in Admin → Settings → Website.</summary>
[ApiController, AllowAnonymous, Route(".well-known")]
public class WellKnownController(ISettingsService settings) : ControllerBase
{
    [HttpGet("assetlinks.json")]
    public async Task<IActionResult> AssetLinks(CancellationToken ct)
    {
        var w = (await settings.GetAsync(ct)).Website;
        Response.Headers.CacheControl = "public, max-age=300";
        if (string.IsNullOrWhiteSpace(w.AndroidPackageName) || w.AndroidSha256Fingerprints.Count == 0) return new JsonResult(Array.Empty<object>());
        return new JsonResult(new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new { @namespace = "android_app", package_name = w.AndroidPackageName, sha256_cert_fingerprints = w.AndroidSha256Fingerprints },
            },
        });
    }
}
