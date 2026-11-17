using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Application.Documents;

public record FolderDto(Guid Id, string Name, Guid? ParentId, int DocumentCount);
public record FolderRequest([Required, StringLength(100, MinimumLength = 1)] string Name, Guid? ParentId);
public record DocumentDto(Guid Id, string Name, Guid? FolderId, string? FolderName, string Category, string? Description, IReadOnlyList<string> Tags,
    int VersionCount, long SizeBytes, string ContentType, string Extension, DateTime CreatedAt, DateTime UpdatedAt);
public record DocumentVersionDto(Guid Id, int VersionNumber, long SizeBytes, string FileName, string? Note, DateTime UploadedAt);
public record DocumentMetaRequest([Required, StringLength(200, MinimumLength = 1)] string Name, Guid? FolderId, [Required, StringLength(40)] string Category,
    [StringLength(2000)] string? Description, List<string>? Tags);
public record DocumentQuery(Guid? FolderId, bool AllFolders, string? Category, string? Tag, string? Search, string? Sort, int Page = 1, int PageSize = 30);
public record ShareRequest([Range(1, 720)] int ExpiresInHours, [Range(1, 1000)] int? MaxDownloads);
public record ShareLinkDto(Guid Id, string? Url, DateTime CreatedAt, DateTime ExpiresAt, int? MaxDownloads, int DownloadCount, bool IsActive);
public record UploadHistoryDto(Guid DocumentId, string DocumentName, int VersionNumber, long SizeBytes, DateTime UploadedAt);

public sealed class DocumentService(IAppDbContext db, FileService files, IFieldEncryptor enc, ITokenService tokens, IAuditService audit, TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<AppOptions> app)
{
    public static readonly string[] Categories = ["Personal", "Identity", "Education", "Employment", "Financial", "Medical", "Property", "Business", "Receipts", "Other"];
    private static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".jpg", ".jpeg", ".png", ".webp", ".txt"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- folders
    public async Task<IReadOnlyList<FolderDto>> GetFoldersAsync(CancellationToken ct) =>
        await db.DocumentFolders.AsNoTracking().OrderBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentId, db.Documents.Count(d => d.FolderId == f.Id))).ToListAsync(ct);

    public async Task<FolderDto> SaveFolderAsync(Guid? id, FolderRequest r, CancellationToken ct)
    {
        if (r.ParentId is { } p)
        {
            if (!await db.DocumentFolders.AnyAsync(f => f.Id == p, ct)) throw new ValidationAppException("parentId", "Parent folder not found.");
            if (id != null && await IsDescendantAsync(p, id.Value, ct)) throw new ValidationAppException("parentId", "A folder cannot be moved inside itself.");
        }
        DocumentFolder f;
        if (id is { } existing) f = await db.DocumentFolders.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Folder");
        else { f = new DocumentFolder(); db.DocumentFolders.Add(f); }
        f.Name = r.Name.Trim(); f.ParentId = r.ParentId;
        await db.SaveChangesAsync(ct);
        return new FolderDto(f.Id, f.Name, f.ParentId, 0);
    }

    private async Task<bool> IsDescendantAsync(Guid candidate, Guid folderId, CancellationToken ct)
    {
        var all = await db.DocumentFolders.AsNoTracking().Select(f => new { f.Id, f.ParentId }).ToListAsync(ct);
        Guid? cur = candidate;
        for (var i = 0; i < 100 && cur != null; i++)
        {
            if (cur == folderId) return true;
            cur = all.FirstOrDefault(f => f.Id == cur)?.ParentId;
        }
        return false;
    }

    public async Task DeleteFolderAsync(Guid id, CancellationToken ct)
    {
        var f = await db.DocumentFolders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Folder");
        if (await db.Documents.AnyAsync(d => d.FolderId == id, ct) || await db.DocumentFolders.AnyAsync(x => x.ParentId == id, ct))
            throw new ConflictException("The folder is not empty. Move or delete its contents first.");
        db.DocumentFolders.Remove(f);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- documents
    private static readonly System.Linq.Expressions.Expression<Func<Document, DocumentDto>> Projection = d => new DocumentDto(d.Id, d.Name, d.FolderId,
        d.Folder != null ? d.Folder.Name : null, d.Category, d.Description, d.Tags.Select(t => t.Tag).ToList(), d.VersionCount, d.SizeBytes, d.ContentType, d.Extension, d.CreatedAt, d.UpdatedAt);

    public async Task<PagedResult<DocumentDto>> GetDocumentsAsync(DocumentQuery q, CancellationToken ct)
    {
        var query = db.Documents.AsNoTracking();
        if (!q.AllFolders) query = query.Where(d => d.FolderId == q.FolderId);
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(d => d.Category == q.Category);
        if (!string.IsNullOrWhiteSpace(q.Tag)) query = query.Where(d => d.Tags.Any(t => t.Tag == q.Tag.ToLower()));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(d => EF.Functions.ILike(d.Name, like) || (d.Description != null && EF.Functions.ILike(d.Description, like)) || d.Tags.Any(t => EF.Functions.ILike(t.Tag, like)));
        }
        query = q.Sort switch
        {
            "name" => query.OrderBy(d => d.Name),
            "size" => query.OrderByDescending(d => d.SizeBytes),
            "oldest" => query.OrderBy(d => d.CreatedAt),
            _ => query.OrderByDescending(d => d.UpdatedAt)
        };
        return await query.Select(Projection).ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<DocumentDto> GetAsync(Guid id, CancellationToken ct) =>
        await db.Documents.AsNoTracking().Where(d => d.Id == id).Select(Projection).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Document");

    public async Task<DocumentDto> UploadAsync(UploadInput input, DocumentMetaRequest meta, CancellationToken ct)
    {
        ValidateMeta(meta);
        if (meta.FolderId is { } fid && !await db.DocumentFolders.AnyAsync(f => f.Id == fid, ct)) throw new ValidationAppException("folderId", "Folder not found.");
        var file = await files.UploadAsync(input, "document", ct, DocumentExtensions);
        var doc = new Document
        {
            Name = string.IsNullOrWhiteSpace(meta.Name) ? Path.GetFileNameWithoutExtension(file.OriginalName) : meta.Name.Trim(),
            FolderId = meta.FolderId, Category = meta.Category, Description = TextUtil.Clean(meta.Description, 2000),
            SizeBytes = file.SizeBytes, ContentType = file.ContentType, Extension = file.Extension, VersionCount = 1
        };
        var version = new DocumentVersion { DocumentId = doc.Id, VersionNumber = 1, StoredFileId = file.Id, UploadedAt = Now, Note = "Initial upload" };
        doc.CurrentVersionId = version.Id;
        db.Documents.Add(doc);
        db.DocumentVersions.Add(version);
        SetTags(doc, meta.Tags);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_create", "Document", doc.Id.ToString()), ct);
        return await GetAsync(doc.Id, ct);
    }

    public async Task<DocumentDto> AddVersionAsync(Guid id, UploadInput input, string? note, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        var file = await files.UploadAsync(input, "document", ct, DocumentExtensions);
        var version = new DocumentVersion { DocumentId = doc.Id, VersionNumber = doc.VersionCount + 1, StoredFileId = file.Id, UploadedAt = Now, Note = TextUtil.Clean(note, 300) };
        db.DocumentVersions.Add(version);
        doc.VersionCount++; doc.CurrentVersionId = version.Id; doc.SizeBytes = file.SizeBytes; doc.ContentType = file.ContentType; doc.Extension = file.Extension;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "Document", doc.Id.ToString(), $"version {version.VersionNumber}"), ct);
        return await GetAsync(doc.Id, ct);
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> GetVersionsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Documents.AnyAsync(d => d.Id == id, ct)) throw new NotFoundException("Document");
        return await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == id).OrderByDescending(v => v.VersionNumber)
            .Select(v => new DocumentVersionDto(v.Id, v.VersionNumber, v.StoredFile!.SizeBytes, v.StoredFile.OriginalName, v.Note, v.UploadedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UploadHistoryDto>> GetUploadHistoryAsync(int take, CancellationToken ct) =>
        await db.DocumentVersions.AsNoTracking()
            .Join(db.Documents /* owner + soft-delete filters apply */, v => v.DocumentId, d => d.Id, (v, d) => new { v, d })
            .OrderByDescending(x => x.v.UploadedAt).Take(Math.Clamp(take, 1, 200))
            .Select(x => new UploadHistoryDto(x.d.Id, x.d.Name, x.v.VersionNumber, x.v.StoredFile!.SizeBytes, x.v.UploadedAt)).ToListAsync(ct);

    public async Task<DocumentDto> UpdateMetaAsync(Guid id, DocumentMetaRequest r, CancellationToken ct)
    {
        ValidateMeta(r);
        var doc = await db.Documents.Include(d => d.Tags).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        if (r.FolderId is { } fid && !await db.DocumentFolders.AnyAsync(f => f.Id == fid, ct)) throw new ValidationAppException("folderId", "Folder not found.");
        doc.Name = r.Name.Trim(); doc.FolderId = r.FolderId; doc.Category = r.Category; doc.Description = TextUtil.Clean(r.Description, 2000);
        db.DocumentTags.RemoveRange(doc.Tags);
        doc.Tags.Clear();
        SetTags(doc, r.Tags);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "Document", doc.Id.ToString()), ct);
        return await GetAsync(id, ct);
    }

    public async Task MoveAsync(Guid id, Guid? folderId, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        if (folderId is { } fid && !await db.DocumentFolders.AnyAsync(f => f.Id == fid, ct)) throw new ValidationAppException("folderId", "Folder not found.");
        doc.FolderId = folderId;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        db.Documents.Remove(doc);
        await db.DocumentShareLinks.Where(s => s.DocumentId == id && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "Document", id.ToString()), ct);
    }

    public async Task<FileStreamResultDto> OpenAsync(Guid id, Guid? versionId, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Document");
        var vid = versionId ?? doc.CurrentVersionId;
        var fileId = await db.DocumentVersions.Where(v => v.Id == vid && v.DocumentId == id).Select(v => (Guid?)v.StoredFileId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Version");
        var f = await files.OpenOwnAsync(fileId, ct);
        return f with { FileName = SafeDownloadName(doc.Name, doc.Extension) };
    }

    private static string SafeDownloadName(string name, string ext) => FileService.SanitizeName(name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext);

    // ---------------------------------------------------------------- sharing
    public async Task<ShareLinkDto> CreateShareAsync(Guid id, ShareRequest r, CancellationToken ct)
    {
        if (!await db.Documents.AnyAsync(d => d.Id == id, ct)) throw new NotFoundException("Document");
        var token = tokens.CreateOpaqueToken(32);
        var link = new DocumentShareLink { DocumentId = id, TokenHash = enc.Hash(token), CreatedAt = Now, ExpiresAt = Now.AddHours(r.ExpiresInHours), MaxDownloads = r.MaxDownloads };
        db.DocumentShareLinks.Add(link);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("document_share_created", "Document", id.ToString(), $"expires {link.ExpiresAt:u}"), ct);
        // The raw token is returned exactly once; only its hash is stored.
        return new ShareLinkDto(link.Id, $"{app.Value.PublicBaseUrl.TrimEnd('/')}/api/public/share/{token}", link.CreatedAt, link.ExpiresAt, link.MaxDownloads, 0, true);
    }

    public async Task<IReadOnlyList<ShareLinkDto>> GetSharesAsync(Guid id, CancellationToken ct) =>
        await db.DocumentShareLinks.AsNoTracking().Where(s => s.DocumentId == id).OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShareLinkDto(s.Id, null, s.CreatedAt, s.ExpiresAt, s.MaxDownloads, s.DownloadCount,
                s.RevokedAt == null && s.ExpiresAt > Now && (s.MaxDownloads == null || s.DownloadCount < s.MaxDownloads))).ToListAsync(ct);

    public async Task RevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var s = await db.DocumentShareLinks.FirstOrDefaultAsync(x => x.Id == shareId, ct) ?? throw new NotFoundException("Share link");
        s.RevokedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Anonymous download through a share link. Owner filters are bypassed deliberately after validating the token.</summary>
    public async Task<FileStreamResultDto> OpenSharedAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) throw new NotFoundException("Link");
        var hash = enc.Hash(token);
        var link = await db.DocumentShareLinks.IgnoreQueryFilters().Include(l => l.Document).FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link == null || link.RevokedAt != null || link.ExpiresAt <= Now || link.Document == null || link.Document.IsDeleted
            || (link.MaxDownloads != null && link.DownloadCount >= link.MaxDownloads)) throw new NotFoundException("This link has expired or");
        link.DownloadCount++;
        await db.SaveChangesAsync(ct);
        var fileId = await db.DocumentVersions.Where(v => v.Id == link.Document.CurrentVersionId).Select(v => v.StoredFileId).FirstAsync(ct);
        await audit.LogAsync(new AuditEntry("document_share_download", "Document", link.DocumentId.ToString(), UserId: link.UserId), ct);
        var f = await files.OpenAuthorizedAsync(fileId, ct);
        return f with { FileName = SafeDownloadName(link.Document.Name, link.Document.Extension) };
    }

    private static void ValidateMeta(DocumentMetaRequest r)
    {
        if (!Categories.Contains(r.Category)) throw new ValidationAppException("category", "Choose a valid category.");
    }

    private void SetTags(Document doc, List<string>? tags)
    {
        foreach (var t in (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 40).Distinct().Take(20))
            db.DocumentTags.Add(new DocumentTag { DocumentId = doc.Id, Tag = t });
    }
}
