using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Application.Journal;

public record DiaryListItem(Guid Id, string Title, DateOnly EntryDate, TimeOnly EntryTime, string? Mood, IReadOnlyList<string> Tags, string Excerpt, int AttachmentCount, Guid? CoverPhotoId);
public record DiaryAttachmentDto(Guid FileId, string Name, string ContentType, long SizeBytes, bool IsPhoto);
public record DiaryEntryDto(Guid Id, string Title, string ContentHtml, DateOnly EntryDate, TimeOnly EntryTime, string? Mood, IReadOnlyList<string> Tags,
    IReadOnlyList<DiaryAttachmentDto> Attachments, DateTime CreatedAt, DateTime UpdatedAt);
public record DiaryRequest([Required, StringLength(200, MinimumLength = 1)] string Title, [Required, StringLength(200_000)] string ContentHtml, DateOnly EntryDate, TimeOnly EntryTime,
    [StringLength(30)] string? Mood, List<string>? Tags);
public record DiaryQuery(DateOnly? From, DateOnly? To, string? Tag, string? Mood, string? Search, int Page = 1, int PageSize = 20);
public record ArchiveBucket(int Year, int Month, int Count);

/// <summary>Private journal. Content is sanitised, then encrypted at rest (AES-256-GCM). No admin endpoint exposes diary content.</summary>
public sealed partial class DiaryService(IAppDbContext db, IFieldEncryptor enc, IHtmlSanitizerService sanitizer, FileService files, IAuditService audit, TimeProvider clock)
{
    public static readonly string[] Moods = ["happy", "grateful", "calm", "excited", "neutral", "tired", "sad", "anxious", "angry"];

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    private static string Excerpt(string html)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 180 ? text[..180] + "…" : text;
    }

    public async Task<PagedResult<DiaryListItem>> GetEntriesAsync(DiaryQuery q, CancellationToken ct)
    {
        var query = db.DiaryEntries.AsNoTracking();
        if (q.From is { } f) query = query.Where(e => e.EntryDate >= f);
        if (q.To is { } t) query = query.Where(e => e.EntryDate <= t);
        if (!string.IsNullOrWhiteSpace(q.Mood)) query = query.Where(e => e.Mood == q.Mood);
        if (!string.IsNullOrWhiteSpace(q.Tag)) query = query.Where(e => e.Tags.Any(x => x.Tag == q.Tag.ToLower()));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(e => EF.Functions.ILike(e.Title, like) || e.Tags.Any(x => EF.Functions.ILike(x.Tag, like)) || (e.Mood != null && EF.Functions.ILike(e.Mood, like)));
        }
        var page = await query.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.EntryTime).Include(e => e.Tags).Include(e => e.Attachments).AsSplitQuery()
            .ToPagedAsync(q.Page, q.PageSize, ct);
        return new PagedResult<DiaryListItem>(page.Items.Select(e => new DiaryListItem(e.Id, e.Title, e.EntryDate, e.EntryTime, e.Mood, e.Tags.Select(t => t.Tag).ToList(),
            Excerpt(enc.Decrypt(e.EncryptedContent)), e.Attachments.Count, e.Attachments.Where(a => a.IsPhoto).Select(a => (Guid?)a.StoredFileId).FirstOrDefault())).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<DiaryEntryDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.DiaryEntries.AsNoTracking().Include(x => x.Tags).Include(x => x.Attachments).ThenInclude(a => a.StoredFile).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Diary entry");
        return new DiaryEntryDto(e.Id, e.Title, enc.Decrypt(e.EncryptedContent), e.EntryDate, e.EntryTime, e.Mood, e.Tags.Select(t => t.Tag).ToList(),
            e.Attachments.Where(a => a.StoredFile != null).Select(a => new DiaryAttachmentDto(a.StoredFileId, a.StoredFile!.OriginalName, a.StoredFile.ContentType, a.StoredFile.SizeBytes, a.IsPhoto)).ToList(),
            e.CreatedAt, e.UpdatedAt);
    }

    public async Task<DiaryEntryDto> SaveAsync(Guid? id, DiaryRequest r, CancellationToken ct)
    {
        if (r.Mood != null && !Moods.Contains(r.Mood)) throw new ValidationAppException("mood", "Choose a valid mood.");
        DiaryEntry e;
        if (id is { } existing)
        {
            e = await db.DiaryEntries.Include(x => x.Tags).FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Diary entry");
            db.DiaryTags.RemoveRange(e.Tags);
        }
        else { e = new DiaryEntry(); db.DiaryEntries.Add(e); }
        e.Title = r.Title.Trim(); e.EncryptedContent = enc.Encrypt(sanitizer.Sanitize(r.ContentHtml)); e.EntryDate = r.EntryDate; e.EntryTime = r.EntryTime; e.Mood = r.Mood;
        foreach (var t in (r.Tags ?? []).Select(t => t.Trim().ToLowerInvariant().TrimStart('#')).Where(t => t.Length is > 0 and <= 40).Distinct().Take(20))
            db.DiaryTags.Add(new DiaryTag { EntryId = e.Id, Tag = t });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "DiaryEntry", e.Id.ToString()), ct);
        return await GetAsync(e.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var e = await db.DiaryEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Diary entry");
        db.DiaryEntries.Remove(e);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "DiaryEntry", id.ToString()), ct);
    }

    public async Task<DiaryAttachmentDto> AddAttachmentAsync(Guid id, UploadInput input, CancellationToken ct)
    {
        if (!await db.DiaryEntries.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Diary entry");
        var f = await files.UploadAsync(input, "diary", ct);
        var isPhoto = FileService.ImageExtensions.Contains(f.Extension);
        db.DiaryAttachments.Add(new DiaryAttachment { EntryId = id, StoredFileId = f.Id, IsPhoto = isPhoto, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        return new DiaryAttachmentDto(f.Id, f.OriginalName, f.ContentType, f.SizeBytes, isPhoto);
    }

    public async Task RemoveAttachmentAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        if (!await db.DiaryEntries.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Diary entry");
        var a = await db.DiaryAttachments.FirstOrDefaultAsync(x => x.EntryId == id && x.StoredFileId == fileId, ct) ?? throw new NotFoundException("Attachment");
        db.DiaryAttachments.Remove(a);
        await db.SaveChangesAsync(ct);
        await files.DeleteOwnAsync(fileId, ct);
    }

    public async Task<IReadOnlyList<ArchiveBucket>> GetArchiveAsync(CancellationToken ct) =>
        (await db.DiaryEntries.AsNoTracking().GroupBy(e => new { e.EntryDate.Year, e.EntryDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync(ct))
            .Select(b => new ArchiveBucket(b.Year, b.Month, b.Count)).OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).ToList();

    public async Task<IReadOnlyList<DateOnly>> GetEntryDatesAsync(int year, int month, CancellationToken ct)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        return await db.DiaryEntries.AsNoTracking().Where(e => e.EntryDate >= from && e.EntryDate < to).Select(e => e.EntryDate).Distinct().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken ct) =>
        await db.DiaryEntries.AsNoTracking().SelectMany(e => e.Tags.Select(t => t.Tag)).Distinct().OrderBy(t => t).Take(200).ToListAsync(ct);
}
