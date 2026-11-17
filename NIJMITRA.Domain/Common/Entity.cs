namespace NIJMITRA.Domain.Common;

/// <summary>Base entity with a time-ordered GUID primary key.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Entity with UTC audit columns populated automatically by the DbContext.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Marks an entity as personal data owned by exactly one user. The DbContext applies a
/// global "Owner" query filter and stamps/validates UserId from the authenticated
/// server-side identity — a UserId supplied by the client is never trusted.
/// </summary>
public interface IOwnedEntity
{
    Guid UserId { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

public abstract class OwnedEntity : AuditableEntity, IOwnedEntity, ISoftDelete
{
    public Guid UserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
