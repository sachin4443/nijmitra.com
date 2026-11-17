using NIJMITRA.Domain.Common;

namespace NIJMITRA.Domain.Entities;

public class DocumentFolder : OwnedEntity
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
}

public class Document : OwnedEntity
{
    public string Name { get; set; } = "";
    public Guid? FolderId { get; set; }
    public DocumentFolder? Folder { get; set; }
    public string Category { get; set; } = "Personal";
    public string? Description { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public int VersionCount { get; set; }
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public string Extension { get; set; } = "";
    public ICollection<DocumentVersion> Versions { get; set; } = [];
    public ICollection<DocumentTag> Tags { get; set; } = [];
}

public class DocumentVersion : Entity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public int VersionNumber { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public string? Note { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class DocumentTag : Entity
{
    public Guid DocumentId { get; set; }
    public string Tag { get; set; } = "";
}

public class DocumentShareLink : Entity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int? MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public DateTime? RevokedAt { get; set; }
}
