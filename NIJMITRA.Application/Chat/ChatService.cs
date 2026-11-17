using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Chat;

public record UserCardDto(Guid Id, string PublicId, string FullName, bool HasAvatar, string Relationship);
public record InvitationDto(Guid Id, string Code, DateTime ExpiresAt, int MaxUses, int UsedCount, bool IsActive);
public record InvitationRequest([Range(1, 720)] int ExpiresInHours = 168, [Range(1, 50)] int MaxUses = 1);
public record FriendRequestDto(Guid Id, UserCardDto User, string? Message, FriendRequestStatus Status, DateTime CreatedAt);
public record SendFriendRequest([Required, StringLength(20)] string PublicId, [StringLength(200)] string? Message);
public record FriendDto(Guid UserId, string PublicId, string FullName, bool HasAvatar, bool IsOnline, DateTime? LastSeenAt, Guid ConversationId,
    string? LastMessage, DateTime? LastMessageAt, int UnreadCount, bool IsBlocked, bool BlockedMe, int? DisappearingSeconds);
public record AttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);
public record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, ChatMessageType Type, string? Body, Guid? ReplyToMessageId, string? ReplyPreview,
    DateTime CreatedAt, bool IsDeleted, DateTime? ExpiresAt, string Status, IReadOnlyList<AttachmentDto> Attachments);
public record SendMessageRequest([StringLength(4000)] string? Body, Guid? ReplyToMessageId, List<Guid>? FileIds);
public record ReportRequest([Required, StringLength(60)] string Reason, [StringLength(1000)] string? Details);
public record DisappearingRequest([Range(0, 604800)] int Seconds);

/// <summary>
/// Friends & private chat. Every operation verifies that the caller participates in the conversation.
/// Messages are protected in transit (TLS) and by server-side access control; they are not end-to-end encrypted.
/// </summary>
public sealed class ChatService(IAppDbContext db, ICurrentUser current, FileService files, IRealtimeNotifier realtime, INotificationService notifications,
    ISettingsService settings, IAuditService audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Guid Me => current.RequireUserId();
    private static (Guid A, Guid B) Pair(Guid x, Guid y) => x.CompareTo(y) < 0 ? (x, y) : (y, x);

    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        if (!(await settings.GetAsync(ct)).ChatEnabled) throw new ForbiddenException("Chat is currently disabled by the administrator.", "chat_disabled");
    }

    private async Task<string> RelationshipAsync(Guid other, CancellationToken ct)
    {
        var (a, b) = Pair(Me, other);
        if (await db.FriendConnections.AnyAsync(f => f.UserAId == a && f.UserBId == b, ct)) return "friend";
        if (await db.FriendRequests.AnyAsync(r => r.FromUserId == Me && r.ToUserId == other && r.Status == FriendRequestStatus.Pending, ct)) return "request_sent";
        if (await db.FriendRequests.AnyAsync(r => r.FromUserId == other && r.ToUserId == Me && r.Status == FriendRequestStatus.Pending, ct)) return "request_received";
        return "none";
    }

    private Task<bool> IsBlockedEitherWayAsync(Guid other, CancellationToken ct) =>
        db.BlockedUsers.AnyAsync(b => (b.UserId == Me && b.BlockedUserId == other) || (b.UserId == other && b.BlockedUserId == Me), ct);

    // ---------------------------------------------------------------- discovery & invitations
    public async Task<UserCardDto> FindByPublicIdAsync(string publicId, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var id = (publicId ?? "").Trim().ToUpperInvariant();
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).FirstOrDefaultAsync(x => x.PublicId == id && x.Status == UserStatus.Active, ct);
        if (u == null || u.Id == Me || await db.BlockedUsers.AnyAsync(b => b.UserId == u.Id && b.BlockedUserId == Me, ct)) throw new NotFoundException("User");
        return new UserCardDto(u.Id, u.PublicId, u.FullName, false, await RelationshipAsync(u.Id, ct));
    }

    public async Task<InvitationDto> CreateInvitationAsync(InvitationRequest r, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = "NM-" + new string(RandomNumberGenerator.GetItems<char>(alphabet, 8));
        var inv = new FriendInvitation { InviterUserId = Me, Code = code, CreatedAt = Now, ExpiresAt = Now.AddHours(r.ExpiresInHours), MaxUses = r.MaxUses };
        db.FriendInvitations.Add(inv);
        await db.SaveChangesAsync(ct);
        return new InvitationDto(inv.Id, inv.Code, inv.ExpiresAt, inv.MaxUses, 0, true);
    }

    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken ct) =>
        await db.FriendInvitations.AsNoTracking().Where(i => i.InviterUserId == Me).OrderByDescending(i => i.CreatedAt).Take(30)
            .Select(i => new InvitationDto(i.Id, i.Code, i.ExpiresAt, i.MaxUses, i.UsedCount, i.RevokedAt == null && i.ExpiresAt > Now && i.UsedCount < i.MaxUses)).ToListAsync(ct);

    public async Task RevokeInvitationAsync(Guid id, CancellationToken ct) =>
        await db.FriendInvitations.Where(i => i.Id == id && i.InviterUserId == Me).ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, Now), ct);

    /// <summary>Redeeming a valid invitation code creates the friendship immediately (the inviter consented by sharing it).</summary>
    public async Task<FriendDto> AcceptInvitationAsync(string code, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (!c.StartsWith("NM-")) c = "NM-" + c;
        var inv = await db.FriendInvitations.FirstOrDefaultAsync(i => i.Code == c, ct);
        if (inv == null || inv.RevokedAt != null || inv.ExpiresAt <= Now || inv.UsedCount >= inv.MaxUses) throw new ValidationAppException("code", "This invitation code is invalid or has expired.");
        if (inv.InviterUserId == Me) throw new ValidationAppException("code", "You cannot accept your own invitation.");
        if (await IsBlockedEitherWayAsync(inv.InviterUserId, ct)) throw new ValidationAppException("code", "This invitation cannot be used.");
        inv.UsedCount++;
        await db.SaveChangesAsync(ct);
        var conv = await ConnectAsync(Me, inv.InviterUserId, ct);
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.FriendRequest, inv.InviterUserId, "Invitation accepted",
            $"{await NameAsync(Me, ct)} accepted your invitation. You can now chat.", "/chat/" + conv), ct);
        return (await GetFriendsAsync(ct)).First(f => f.UserId == inv.InviterUserId);
    }

    // ---------------------------------------------------------------- friend requests
    public async Task<FriendRequestDto> SendRequestAsync(SendFriendRequest r, CancellationToken ct)
    {
        var target = await FindByPublicIdAsync(r.PublicId, ct);
        if (target.Relationship == "friend") throw new ConflictException("You are already friends.");
        if (target.Relationship == "request_sent") throw new ConflictException("Friend request already sent.");
        if (await IsBlockedEitherWayAsync(target.Id, ct)) throw new ValidationAppException("publicId", "You cannot send a request to this user.");
        if (target.Relationship == "request_received")
        {
            var incoming = await db.FriendRequests.FirstAsync(x => x.FromUserId == target.Id && x.ToUserId == Me && x.Status == FriendRequestStatus.Pending, ct);
            await RespondAsync(incoming.Id, true, ct);
            return new FriendRequestDto(incoming.Id, target with { Relationship = "friend" }, incoming.Message, FriendRequestStatus.Accepted, incoming.CreatedAt);
        }
        var recent = await db.FriendRequests.CountAsync(x => x.FromUserId == Me && x.CreatedAt > Now.AddDays(-1), ct);
        if (recent >= 30) throw new ValidationAppException("Daily friend-request limit reached.");
        var req = new FriendRequest { FromUserId = Me, ToUserId = target.Id, Message = TextUtil.Clean(r.Message, 200), CreatedAt = Now };
        db.FriendRequests.Add(req);
        await db.SaveChangesAsync(ct);
        var myName = await NameAsync(Me, ct);
        await realtime.ToUserAsync(target.Id, "friendRequest", new { req.Id, from = myName }, ct);
        await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.FriendRequest, target.Id, "New friend request",
            $"{myName} wants to connect with you on NIJMITRA.", "/chat?tab=requests", DedupeKey: $"fr:{req.Id}"), ct);
        return new FriendRequestDto(req.Id, target with { Relationship = "request_sent" }, req.Message, req.Status, req.CreatedAt);
    }

    public async Task<IReadOnlyList<FriendRequestDto>> GetRequestsAsync(bool incoming, CancellationToken ct)
    {
        var q = db.FriendRequests.AsNoTracking().Where(r => r.Status == FriendRequestStatus.Pending && (incoming ? r.ToUserId == Me : r.FromUserId == Me));
        return await q.OrderByDescending(r => r.CreatedAt).Select(r => new FriendRequestDto(r.Id,
            incoming ? new UserCardDto(r.FromUserId, r.FromUser!.PublicId, r.FromUser.FullName, r.FromUser.Profile!.AvatarFileId != null, "request_received")
                     : new UserCardDto(r.ToUserId, r.ToUser!.PublicId, r.ToUser.FullName, false, "request_sent"),
            r.Message, r.Status, r.CreatedAt)).ToListAsync(ct);
    }

    public async Task RespondAsync(Guid requestId, bool accept, CancellationToken ct)
    {
        var r = await db.FriendRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.ToUserId == Me && x.Status == FriendRequestStatus.Pending, ct) ?? throw new NotFoundException("Friend request");
        r.Status = accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Rejected;
        r.RespondedAt = Now;
        await db.SaveChangesAsync(ct);
        if (accept)
        {
            var conv = await ConnectAsync(r.FromUserId, r.ToUserId, ct);
            await realtime.ToUserAsync(r.FromUserId, "friendAccepted", new { conversationId = conv }, ct);
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.FriendRequest, r.FromUserId, "Friend request accepted",
                $"{await NameAsync(Me, ct)} accepted your friend request.", "/chat/" + conv), ct);
        }
    }

    public async Task CancelRequestAsync(Guid requestId, CancellationToken ct) =>
        await db.FriendRequests.Where(x => x.Id == requestId && x.FromUserId == Me && x.Status == FriendRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FriendRequestStatus.Cancelled).SetProperty(x => x.RespondedAt, Now), ct);

    private async Task<Guid> ConnectAsync(Guid x, Guid y, CancellationToken ct)
    {
        var (a, b) = Pair(x, y);
        var existing = await db.FriendConnections.FirstOrDefaultAsync(f => f.UserAId == a && f.UserBId == b, ct);
        if (existing != null) return existing.ConversationId;
        var conv = new ChatConversation { CreatedAt = Now };
        conv.Participants.Add(new ChatParticipant { ConversationId = conv.Id, UserId = a, JoinedAt = Now });
        conv.Participants.Add(new ChatParticipant { ConversationId = conv.Id, UserId = b, JoinedAt = Now });
        db.ChatConversations.Add(conv);
        db.FriendConnections.Add(new FriendConnection { UserAId = a, UserBId = b, ConversationId = conv.Id, CreatedAt = Now });
        await db.FriendRequests.Where(r => r.Status == FriendRequestStatus.Pending && ((r.FromUserId == x && r.ToUserId == y) || (r.FromUserId == y && r.ToUserId == x)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, FriendRequestStatus.Accepted).SetProperty(r => r.RespondedAt, Now), ct);
        await db.SaveChangesAsync(ct);
        return conv.Id;
    }

    public async Task UnfriendAsync(Guid userId, CancellationToken ct)
    {
        var (a, b) = Pair(Me, userId);
        var f = await db.FriendConnections.FirstOrDefaultAsync(x => x.UserAId == a && x.UserBId == b, ct) ?? throw new NotFoundException("Friend");
        db.FriendConnections.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- friends & presence
    public async Task<IReadOnlyList<FriendDto>> GetFriendsAsync(CancellationToken ct)
    {
        var me = Me;
        var rows = await db.FriendConnections.AsNoTracking().Where(f => f.UserAId == me || f.UserBId == me)
            .Select(f => new { FriendId = f.UserAId == me ? f.UserBId : f.UserAId, f.ConversationId }).ToListAsync(ct);
        var ids = rows.Select(r => r.FriendId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.PublicId, u.FullName, HasAvatar = u.Profile!.AvatarFileId != null }).ToDictionaryAsync(u => u.Id, ct);
        var presence = await db.UserPresences.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var blocks = await db.BlockedUsers.AsNoTracking().Where(b => (b.UserId == me && ids.Contains(b.BlockedUserId)) || (b.BlockedUserId == me && ids.Contains(b.UserId))).ToListAsync(ct);
        var convIds = rows.Select(r => r.ConversationId).ToList();
        var convs = await db.ChatConversations.AsNoTracking().Where(c => convIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var myReads = await db.ChatParticipants.AsNoTracking().Where(p => p.UserId == me && convIds.Contains(p.ConversationId)).ToDictionaryAsync(p => p.ConversationId, p => p.LastReadAt, ct);
        var now = Now;
        var result = new List<FriendDto>();
        foreach (var r in rows)
        {
            if (!users.TryGetValue(r.FriendId, out var u)) continue;
            var last = await db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == r.ConversationId && (m.ExpiresAt == null || m.ExpiresAt > now)).OrderByDescending(m => m.CreatedAt)
                .Select(m => new { m.Body, m.Type, m.DeletedAt, m.CreatedAt }).FirstOrDefaultAsync(ct);
            var read = myReads.GetValueOrDefault(r.ConversationId);
            var unread = await db.ChatMessages.CountAsync(m => m.ConversationId == r.ConversationId && m.SenderId != me && m.DeletedAt == null && (m.ExpiresAt == null || m.ExpiresAt > now) && (read == null || m.CreatedAt > read), ct);
            var p = presence.GetValueOrDefault(r.FriendId);
            var online = p is { IsOnline: true } && p.LastHeartbeatAt > now.AddSeconds(-75);
            result.Add(new FriendDto(u.Id, u.PublicId, u.FullName, u.HasAvatar, online, p?.LastSeenAt, r.ConversationId,
                last == null ? null : last.DeletedAt != null ? "Message deleted" : last.Type == ChatMessageType.Text ? last.Body : "📎 " + last.Type,
                last?.CreatedAt, unread, blocks.Any(b => b.UserId == me && b.BlockedUserId == u.Id), blocks.Any(b => b.UserId == u.Id && b.BlockedUserId == me),
                convs.GetValueOrDefault(r.ConversationId)?.DisappearingSeconds));
        }
        return result.OrderByDescending(f => f.LastMessageAt ?? DateTime.MinValue).ThenBy(f => f.FullName).ToList();
    }

    public async Task<IReadOnlyList<Guid>> FriendIdsAsync(Guid userId, CancellationToken ct) =>
        await db.FriendConnections.AsNoTracking().Where(f => f.UserAId == userId || f.UserBId == userId).Select(f => f.UserAId == userId ? f.UserBId : f.UserAId).ToListAsync(ct);

    public async Task BlockAsync(Guid userId, bool block, CancellationToken ct)
    {
        if (userId == Me) throw new ValidationAppException("You cannot block yourself.");
        var existing = await db.BlockedUsers.FirstOrDefaultAsync(b => b.UserId == Me && b.BlockedUserId == userId, ct);
        if (block && existing == null) db.BlockedUsers.Add(new BlockedUser { UserId = Me, BlockedUserId = userId, CreatedAt = Now });
        if (!block && existing != null) db.BlockedUsers.Remove(existing);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(block ? "user_blocked" : "user_unblocked", "User", userId.ToString()), ct);
    }

    public async Task ReportAsync(Guid userId, ReportRequest r, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw new NotFoundException("User");
        db.UserReports.Add(new UserReport { ReporterId = Me, ReportedUserId = userId, Reason = r.Reason.Trim(), Details = TextUtil.Clean(r.Details, 1000), CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("user_reported", "User", userId.ToString()), ct);
    }

    // ---------------------------------------------------------------- messages
    private async Task<(ChatConversation Conv, Guid Other)> RequireConversationAsync(Guid conversationId, CancellationToken ct)
    {
        var conv = await db.ChatConversations.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conv == null || conv.Participants.All(p => p.UserId != Me)) throw new NotFoundException("Conversation");
        return (conv, conv.Participants.First(p => p.UserId != Me).UserId);
    }

    public async Task<IReadOnlyList<Guid>> ParticipantIdsAsync(Guid conversationId, CancellationToken ct)
    {
        var (conv, _) = await RequireConversationAsync(conversationId, ct);
        return conv.Participants.Select(p => p.UserId).ToList();
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid conversationId, DateTime? before, string? search, int take, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var (conv, other) = await RequireConversationAsync(conversationId, ct);
        var otherP = conv.Participants.First(p => p.UserId == other);
        var now = Now;
        var q = db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId && (m.ExpiresAt == null || m.ExpiresAt > now));
        if (before is { } b) q = q.Where(m => m.CreatedAt < b);
        if (!string.IsNullOrWhiteSpace(search)) { var like = TextUtil.LikePattern(search); q = q.Where(m => m.DeletedAt == null && m.Body != null && EF.Functions.ILike(m.Body, like)); }
        var msgs = await q.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take, 1, 100)).Include(m => m.Attachments).ToListAsync(ct);
        var replyIds = msgs.Where(m => m.ReplyToMessageId != null).Select(m => m.ReplyToMessageId!.Value).Distinct().ToList();
        var replies = await db.ChatMessages.AsNoTracking().Where(m => replyIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DeletedAt != null ? "Message deleted" : m.Body ?? "📎 Attachment", ct);
        return msgs.OrderBy(m => m.CreatedAt).Select(m => Map(m, otherP, replies)).ToList();
    }

    private MessageDto Map(ChatMessage m, ChatParticipant otherP, IReadOnlyDictionary<Guid, string>? replies = null)
    {
        var status = m.SenderId != Me ? "received" : otherP.LastReadAt >= m.CreatedAt ? "read" : otherP.LastDeliveredAt >= m.CreatedAt ? "delivered" : "sent";
        var deleted = m.DeletedAt != null;
        return new MessageDto(m.Id, m.ConversationId, m.SenderId, m.Type, deleted ? null : m.Body, m.ReplyToMessageId,
            m.ReplyToMessageId is { } r && replies != null ? Preview(replies.GetValueOrDefault(r)) : null, m.CreatedAt, deleted, m.ExpiresAt, status,
            deleted ? [] : m.Attachments.Select(a => new AttachmentDto(a.StoredFileId, a.FileName, a.ContentType, a.SizeBytes)).ToList());
    }

    private static string? Preview(string? s) => s == null ? null : s.Length > 80 ? s[..80] + "…" : s;

    public async Task<MessageDto> SendAsync(Guid conversationId, SendMessageRequest r, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        var (conv, other) = await RequireConversationAsync(conversationId, ct);
        var (a, b) = Pair(Me, other);
        if (!await db.FriendConnections.AnyAsync(f => f.UserAId == a && f.UserBId == b, ct)) throw new ForbiddenException("You can only message your friends.");
        if (await IsBlockedEitherWayAsync(other, ct)) throw new ForbiddenException("Messages cannot be sent to this user.", "blocked");
        var body = TextUtil.Clean(r.Body, 4000);
        var fileIds = (r.FileIds ?? []).Distinct().Take(10).ToList();
        if (body == null && fileIds.Count == 0) throw new ValidationAppException("body", "Type a message or attach a file.");
        var sent = await db.ChatMessages.CountAsync(m => m.SenderId == Me && m.CreatedAt > Now.AddMinutes(-1), ct);
        if (sent > 60) throw new ValidationAppException("You are sending messages too quickly.");
        if (r.ReplyToMessageId is { } rid && !await db.ChatMessages.AnyAsync(m => m.Id == rid && m.ConversationId == conversationId, ct)) throw new ValidationAppException("replyToMessageId", "Message not found.");
        var fileRows = await db.StoredFiles.Where(f => fileIds.Contains(f.Id)).ToListAsync(ct); // owner filter: only the sender's own uploads
        if (fileRows.Count != fileIds.Count) throw new ValidationAppException("fileIds", "Attachment not found.");
        var type = fileRows.Count == 0 ? ChatMessageType.Text
            : fileRows[0].ContentType.StartsWith("image/") ? ChatMessageType.Image : fileRows[0].ContentType.StartsWith("audio/") ? ChatMessageType.Audio
            : fileRows[0].ContentType.StartsWith("video/") ? ChatMessageType.Video : ChatMessageType.File;
        var msg = new ChatMessage { ConversationId = conversationId, SenderId = Me, Body = body, Type = type, ReplyToMessageId = r.ReplyToMessageId, CreatedAt = Now,
            ExpiresAt = conv.DisappearingSeconds is > 0 ? Now.AddSeconds(conv.DisappearingSeconds.Value) : null };
        foreach (var f in fileRows)
        {
            f.Purpose = "chat";
            msg.Attachments.Add(new ChatAttachment { MessageId = msg.Id, StoredFileId = f.Id, FileName = f.OriginalName, ContentType = f.ContentType, SizeBytes = f.SizeBytes });
        }
        db.ChatMessages.Add(msg);
        conv.LastMessageAt = msg.CreatedAt;
        var mine = conv.Participants.First(p => p.UserId == Me);
        mine.LastReadAt = msg.CreatedAt; mine.LastDeliveredAt = msg.CreatedAt;
        await db.SaveChangesAsync(ct);
        var otherP = conv.Participants.First(p => p.UserId == other);
        var replies = r.ReplyToMessageId is { } rr ? await db.ChatMessages.Where(m => m.Id == rr).ToDictionaryAsync(m => m.Id, m => m.DeletedAt != null ? "Message deleted" : m.Body ?? "📎 Attachment", ct) : null;
        var dto = Map(msg, otherP, replies);
        await realtime.ToUsersAsync([Me, other], "message", dto, ct);
        var presence = await db.UserPresences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == other, ct);
        if (presence is not { IsOnline: true } || presence.LastHeartbeatAt < Now.AddSeconds(-75))
        {
            var name = await NameAsync(Me, ct);
            await notifications.DispatchAsync(new NotificationRequest(NotificationEvents.ChatMessage, other, name,
                type == ChatMessageType.Text ? (body!.Length > 100 ? body[..100] + "…" : body) : $"📎 Sent a {type.ToString().ToLowerInvariant()}", "/chat/" + conversationId,
                DedupeKey: $"chat:{conversationId}:{other}:{Now:yyyyMMddHH}{Now.Minute / 10}"), ct);
        }
        return dto;
    }

    public async Task DeleteMessageAsync(Guid messageId, CancellationToken ct)
    {
        var m = await db.ChatMessages.Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == messageId, ct) ?? throw new NotFoundException("Message");
        var (_, other) = await RequireConversationAsync(m.ConversationId, ct);
        if (m.SenderId != Me) throw new ForbiddenException("You can only delete your own messages.");
        if (m.DeletedAt != null) return;
        m.DeletedAt = Now; m.Body = null;
        foreach (var a in m.Attachments) await files.DeleteOwnAsync(a.StoredFileId, ct);
        await db.SaveChangesAsync(ct);
        await realtime.ToUsersAsync([Me, other], "messageDeleted", new { m.Id, m.ConversationId }, ct);
    }

    public async Task MarkAsync(Guid conversationId, bool read, CancellationToken ct)
    {
        var (conv, other) = await RequireConversationAsync(conversationId, ct);
        var mine = conv.Participants.First(p => p.UserId == Me);
        mine.LastDeliveredAt = Now;
        if (read) mine.LastReadAt = Now;
        await db.SaveChangesAsync(ct);
        await realtime.ToUserAsync(other, read ? "read" : "delivered", new { conversationId, userId = Me, at = Now }, ct);
    }

    /// <summary>Marks every conversation as delivered when the user comes online.</summary>
    public async Task MarkAllDeliveredAsync(Guid userId, CancellationToken ct)
    {
        var parts = await db.ChatParticipants.Where(p => p.UserId == userId).ToListAsync(ct);
        foreach (var p in parts) p.LastDeliveredAt = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int?> SetDisappearingAsync(Guid conversationId, int seconds, CancellationToken ct)
    {
        var (conv, other) = await RequireConversationAsync(conversationId, ct);
        conv.DisappearingSeconds = seconds <= 0 ? null : seconds;
        db.ChatMessages.Add(new ChatMessage { ConversationId = conversationId, SenderId = Me, Type = ChatMessageType.System, CreatedAt = Now,
            Body = seconds <= 0 ? "turned off disappearing messages" : $"turned on disappearing messages ({Human(seconds)})" });
        await db.SaveChangesAsync(ct);
        await realtime.ToUsersAsync([Me, other], "conversationUpdated", new { conversationId, disappearingSeconds = conv.DisappearingSeconds }, ct);
        return conv.DisappearingSeconds;
    }

    private static string Human(int s) => s >= 86400 ? $"{s / 86400} day(s)" : s >= 3600 ? $"{s / 3600} hour(s)" : $"{s / 60} minute(s)";

    public async Task<FileStreamResultDto> OpenAttachmentAsync(Guid messageId, Guid fileId, CancellationToken ct)
    {
        var m = await db.ChatMessages.AsNoTracking().Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == messageId && x.DeletedAt == null && (x.ExpiresAt == null || x.ExpiresAt > Now), ct)
                ?? throw new NotFoundException("Attachment");
        await RequireConversationAsync(m.ConversationId, ct); // participant check
        if (m.Attachments.All(a => a.StoredFileId != fileId)) throw new NotFoundException("Attachment");
        return await files.OpenAuthorizedAsync(fileId, ct);
    }

    private async Task<string> NameAsync(Guid id, CancellationToken ct) => await db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstAsync(ct);
}
