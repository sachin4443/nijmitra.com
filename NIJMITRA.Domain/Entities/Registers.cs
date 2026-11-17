using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Domain.Entities;

public class CustomTable : OwnedEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public ICollection<CustomTableColumn> Columns { get; set; } = [];
}

public class CustomTableColumn : Entity
{
    public Guid TableId { get; set; }
    public CustomTable? Table { get; set; }
    public string Name { get; set; } = "";
    public CustomFieldType FieldType { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsUnique { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    /// <summary>JSON array of dropdown / multi-select options.</summary>
    public string? OptionsJson { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? RegexPattern { get; set; }
    /// <summary>Arithmetic formula referencing other columns, e.g. "{MRP} * {GST} / 100".</summary>
    public string? Formula { get; set; }
    public bool IsDeleted { get; set; }
}

public class CustomTableRecord : OwnedEntity
{
    public Guid TableId { get; set; }
    public CustomTable? Table { get; set; }
    public string? Notes { get; set; }
    public ICollection<CustomTableValue> Values { get; set; } = [];
    public ICollection<CustomTableAttachment> Attachments { get; set; } = [];
}

/// <summary>Typed EAV cell. Exactly one value column is populated depending on the column type.</summary>
public class CustomTableValue : Entity
{
    public Guid RecordId { get; set; }
    public CustomTableRecord? Record { get; set; }
    public Guid ColumnId { get; set; }
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateTime? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? FileId { get; set; }
}

public class CustomTableAttachment : Entity
{
    public Guid RecordId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProductRegister : OwnedEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<ProductCustomField> CustomFields { get; set; } = [];
}

public class ProductCustomField : Entity
{
    public Guid RegisterId { get; set; }
    public string Name { get; set; } = "";
    public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;
    public int SortOrder { get; set; }
}

public class Product : OwnedEntity
{
    public Guid RegisterId { get; set; }
    public ProductRegister? Register { get; set; }
    public string ProductName { get; set; } = "";
    public string? BrandName { get; set; }
    public string? Composition { get; set; }
    public string? Packing { get; set; }
    public decimal? Mrp { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? GstPercent { get; set; }
    public string? HsnCode { get; set; }
    public string? ProductCode { get; set; }
    public string? Manufacturer { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public Guid? ImageFileId { get; set; }
    public string? Description { get; set; }
    /// <summary>Values for register-specific custom fields keyed by field id.</summary>
    public Dictionary<string, string?> CustomValues { get; set; } = [];
    public ICollection<ProductAttachment> Attachments { get; set; } = [];
}

public class ProductAttachment : Entity
{
    public Guid ProductId { get; set; }
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public DateTime CreatedAt { get; set; }
}
