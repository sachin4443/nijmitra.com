using NIJMITRA.Domain.Common;

namespace NIJMITRA.Domain.Entities;

/// <summary>
/// Zero-knowledge vault header. The random 256-bit vault key is generated in the browser and stored
/// here only in wrapped (AES-256-GCM encrypted) form — once under a key derived from the master password
/// (PBKDF2-SHA256) and once under the one-time recovery key. The server never sees either secret.
/// </summary>
public class PasswordVault : AuditableEntity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public string KdfAlgorithm { get; set; } = "PBKDF2-SHA256";
    public int KdfIterations { get; set; }
    public string KdfSalt { get; set; } = "";
    public string WrappedKey { get; set; } = "";
    public string RecoverySalt { get; set; } = "";
    public string RecoveryWrappedKey { get; set; } = "";
    public int FormatVersion { get; set; } = 1;
}

public class PasswordEntry : OwnedEntity
{
    /// <summary>Plaintext, searchable metadata (never secrets).</summary>
    public string Title { get; set; } = "";
    public string? WebsiteUrl { get; set; }
    public string? ApplicationName { get; set; }
    public string Category { get; set; } = "Other";
    public string? Tags { get; set; }
    public bool IsFavorite { get; set; }
    /// <summary>Client-side AES-256-GCM ciphertext of {username,email,password,notes}.</summary>
    public string EncryptedData { get; set; } = "";
    public int Revision { get; set; } = 1;
    public ICollection<PasswordHistory> History { get; set; } = [];
}

public class PasswordHistory : Entity, IOwnedEntity
{
    public Guid UserId { get; set; }
    public Guid PasswordEntryId { get; set; }
    public string EncryptedPassword { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

public class SecureNote : OwnedEntity
{
    /// <summary>Client-side ciphertext of {title, content}.</summary>
    public string EncryptedData { get; set; } = "";
    public bool IsFavorite { get; set; }
}
