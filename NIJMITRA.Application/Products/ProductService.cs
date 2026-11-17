using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.CustomTables;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Products;

public record CustomFieldDto(Guid Id, string Name, CustomFieldType FieldType, int SortOrder);
public record CustomFieldInput(Guid? Id, [Required, StringLength(60, MinimumLength = 1)] string Name, CustomFieldType FieldType);
public record RegisterDto(Guid Id, string Name, string? Description, IReadOnlyList<CustomFieldDto> CustomFields, int ProductCount, DateTime UpdatedAt);
public record RegisterRequest([Required, StringLength(100, MinimumLength = 1)] string Name, [StringLength(1000)] string? Description, List<CustomFieldInput>? CustomFields);
public record ProductDto(Guid Id, Guid RegisterId, string ProductName, string? BrandName, string? Composition, string? Packing, decimal? Mrp, decimal? PurchasePrice,
    decimal? GstPercent, string? HsnCode, string? ProductCode, string? Manufacturer, DateOnly? ExpiryDate, Guid? ImageFileId, string? Description,
    IReadOnlyDictionary<string, string?> CustomValues, IReadOnlyList<FileDto> Attachments, DateTime UpdatedAt);
public record ProductRequest([Required, StringLength(200, MinimumLength = 1)] string ProductName, [StringLength(150)] string? BrandName, [StringLength(1000)] string? Composition,
    [StringLength(100)] string? Packing, [Range(0, 99999999)] decimal? Mrp, [Range(0, 99999999)] decimal? PurchasePrice, [Range(0, 100)] decimal? GstPercent,
    [StringLength(20)] string? HsnCode, [StringLength(60)] string? ProductCode, [StringLength(150)] string? Manufacturer, DateOnly? ExpiryDate, Guid? ImageFileId,
    [StringLength(2000)] string? Description, Dictionary<string, string?>? CustomValues);
public record ProductQuery(string? Search, string? Brand, string? Manufacturer, int? ExpiringWithinDays, bool ExpiredOnly, string? Sort, int Page = 1, int PageSize = 25);

public sealed class ProductService(IAppDbContext db, FileService files, IExportService export, IAuditService audit, TimeProvider clock)
{
    private static readonly (string Header, Func<ProductDto, string?> Get)[] FixedColumns =
    [
        ("Product Name", p => p.ProductName), ("Brand Name", p => p.BrandName), ("Composition", p => p.Composition), ("Packing", p => p.Packing),
        ("MRP", p => p.Mrp?.ToString("0.00", CultureInfo.InvariantCulture)), ("Purchase Price", p => p.PurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture)),
        ("GST %", p => p.GstPercent?.ToString("0.##", CultureInfo.InvariantCulture)), ("HSN Code", p => p.HsnCode), ("Product Code", p => p.ProductCode),
        ("Manufacturer", p => p.Manufacturer), ("Expiry Date", p => p.ExpiryDate?.ToString("yyyy-MM-dd")), ("Description", p => p.Description)
    ];

    // ---------------------------------------------------------------- registers
    public async Task<IReadOnlyList<RegisterDto>> GetRegistersAsync(CancellationToken ct)
    {
        var regs = await db.ProductRegisters.AsNoTracking().Include(r => r.CustomFields).OrderBy(r => r.Name).ToListAsync(ct);
        var counts = await db.Products.AsNoTracking().GroupBy(p => p.RegisterId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        return regs.Select(r => Map(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    private static RegisterDto Map(ProductRegister r, int count) => new(r.Id, r.Name, r.Description,
        r.CustomFields.OrderBy(f => f.SortOrder).Select(f => new CustomFieldDto(f.Id, f.Name, f.FieldType, f.SortOrder)).ToList(), count, r.UpdatedAt);

    public async Task<RegisterDto> SaveRegisterAsync(Guid? id, RegisterRequest r, CancellationToken ct)
    {
        var fields = r.CustomFields ?? [];
        if (fields.Count > 30) throw new ValidationAppException("customFields", "At most 30 custom fields.");
        if (fields.Any(f => f.FieldType is not (CustomFieldType.Text or CustomFieldType.Number or CustomFieldType.Decimal or CustomFieldType.Date or CustomFieldType.Boolean)))
            throw new ValidationAppException("customFields", "Custom fields can be Text, Number, Decimal, Date or Yes/No.");
        if (fields.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1 || FixedColumns.Any(c => string.Equals(c.Header, g.Key, StringComparison.OrdinalIgnoreCase))))
            throw new ValidationAppException("customFields", "Custom field names must be unique and different from the standard fields.");
        ProductRegister reg;
        if (id is { } existing) reg = await db.ProductRegisters.Include(x => x.CustomFields).FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Register");
        else { reg = new ProductRegister(); db.ProductRegisters.Add(reg); }
        reg.Name = r.Name.Trim(); reg.Description = TextUtil.Clean(r.Description, 1000);
        var keep = fields.Where(f => f.Id != null).Select(f => f.Id!.Value).ToHashSet();
        foreach (var removed in reg.CustomFields.Where(f => !keep.Contains(f.Id)).ToList()) { reg.CustomFields.Remove(removed); db.ProductCustomFields.Remove(removed); }
        var i = 0;
        foreach (var f in fields)
        {
            var field = f.Id is { } fid ? reg.CustomFields.FirstOrDefault(x => x.Id == fid) : null;
            if (field == null) { field = new ProductCustomField { RegisterId = reg.Id }; reg.CustomFields.Add(field); }
            field.Name = f.Name.Trim(); field.FieldType = f.FieldType; field.SortOrder = i++;
        }
        reg.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Map(reg, await db.Products.CountAsync(p => p.RegisterId == reg.Id, ct));
    }

    public async Task DeleteRegisterAsync(Guid id, CancellationToken ct)
    {
        var reg = await db.ProductRegisters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Register");
        db.ProductRegisters.Remove(reg);
        await db.Products.Where(p => p.RegisterId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true).SetProperty(p => p.DeletedAt, clock.GetUtcNow().UtcDateTime), ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "ProductRegister", id.ToString()), ct);
    }

    // ---------------------------------------------------------------- products
    private IQueryable<Product> Filter(Guid registerId, ProductQuery q)
    {
        var query = db.Products.AsNoTracking().Where(p => p.RegisterId == registerId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(p => EF.Functions.ILike(p.ProductName, like) || (p.BrandName != null && EF.Functions.ILike(p.BrandName, like))
                || (p.Composition != null && EF.Functions.ILike(p.Composition, like)) || (p.ProductCode != null && EF.Functions.ILike(p.ProductCode, like))
                || (p.HsnCode != null && EF.Functions.ILike(p.HsnCode, like)) || (p.Manufacturer != null && EF.Functions.ILike(p.Manufacturer, like)));
        }
        if (!string.IsNullOrWhiteSpace(q.Brand)) query = query.Where(p => p.BrandName == q.Brand);
        if (!string.IsNullOrWhiteSpace(q.Manufacturer)) query = query.Where(p => p.Manufacturer == q.Manufacturer);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (q.ExpiredOnly) query = query.Where(p => p.ExpiryDate != null && p.ExpiryDate < today);
        else if (q.ExpiringWithinDays is { } days) { var limit = today.AddDays(days); query = query.Where(p => p.ExpiryDate != null && p.ExpiryDate >= today && p.ExpiryDate <= limit); }
        return q.Sort switch
        {
            "expiry" => query.OrderBy(p => p.ExpiryDate == null).ThenBy(p => p.ExpiryDate),
            "mrp" => query.OrderByDescending(p => p.Mrp),
            "recent" => query.OrderByDescending(p => p.UpdatedAt),
            _ => query.OrderBy(p => p.ProductName)
        };
    }

    private static ProductDto Map(Product p) => new(p.Id, p.RegisterId, p.ProductName, p.BrandName, p.Composition, p.Packing, p.Mrp, p.PurchasePrice, p.GstPercent,
        p.HsnCode, p.ProductCode, p.Manufacturer, p.ExpiryDate, p.ImageFileId, p.Description, p.CustomValues,
        p.Attachments.Where(a => a.StoredFile != null).Select(a => FileService.ToDto(a.StoredFile!)).ToList(), p.UpdatedAt);

    public async Task<PagedResult<ProductDto>> GetProductsAsync(Guid registerId, ProductQuery q, CancellationToken ct)
    {
        await EnsureRegisterAsync(registerId, ct);
        var page = await Filter(registerId, q).Include(p => p.Attachments).ThenInclude(a => a.StoredFile).ToPagedAsync(q.Page, q.PageSize, ct);
        return new PagedResult<ProductDto>(page.Items.Select(Map).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<ProductDto> GetProductAsync(Guid registerId, Guid id, CancellationToken ct) =>
        Map(await db.Products.AsNoTracking().Include(p => p.Attachments).ThenInclude(a => a.StoredFile)
            .FirstOrDefaultAsync(p => p.Id == id && p.RegisterId == registerId, ct) ?? throw new NotFoundException("Product"));

    public async Task<ProductDto> SaveProductAsync(Guid registerId, Guid? id, ProductRequest r, CancellationToken ct)
    {
        var reg = await db.ProductRegisters.AsNoTracking().Include(x => x.CustomFields).FirstOrDefaultAsync(x => x.Id == registerId, ct) ?? throw new NotFoundException("Register");
        if (r.ImageFileId is { } img && !await db.StoredFiles.AnyAsync(f => f.Id == img && FileService.ImageExtensions.Contains(f.Extension), ct))
            throw new ValidationAppException("imageFileId", "Image not found.");
        Product p;
        if (id is { } existing) p = await db.Products.FirstOrDefaultAsync(x => x.Id == existing && x.RegisterId == registerId, ct) ?? throw new NotFoundException("Product");
        else { p = new Product { RegisterId = registerId }; db.Products.Add(p); }
        p.ProductName = r.ProductName.Trim(); p.BrandName = TextUtil.Clean(r.BrandName, 150); p.Composition = TextUtil.Clean(r.Composition, 1000);
        p.Packing = TextUtil.Clean(r.Packing, 100); p.Mrp = r.Mrp; p.PurchasePrice = r.PurchasePrice; p.GstPercent = r.GstPercent; p.HsnCode = TextUtil.Clean(r.HsnCode, 20);
        p.ProductCode = TextUtil.Clean(r.ProductCode, 60); p.Manufacturer = TextUtil.Clean(r.Manufacturer, 150); p.ExpiryDate = r.ExpiryDate; p.ImageFileId = r.ImageFileId;
        p.Description = TextUtil.Clean(r.Description, 2000);
        var custom = new Dictionary<string, string?>();
        foreach (var f in reg.CustomFields)
        {
            if (r.CustomValues?.TryGetValue(f.Id.ToString(), out var raw) != true || string.IsNullOrWhiteSpace(raw)) continue;
            try { custom[f.Id.ToString()] = CustomTableService.Normalize(new CustomTableColumn { Name = f.Name, FieldType = f.FieldType }, raw.Trim()); }
            catch (ValidationAppException ex) { throw new ValidationAppException("customValues", ex.Message); }
        }
        p.CustomValues = custom;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(id == null ? "record_create" : "record_update", "Product", p.Id.ToString()), ct);
        return await GetProductAsync(registerId, p.Id, ct);
    }

    public async Task DeleteProductAsync(Guid registerId, Guid id, CancellationToken ct)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.RegisterId == registerId, ct) ?? throw new NotFoundException("Product");
        db.Products.Remove(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "Product", id.ToString()), ct);
    }

    public async Task<FileDto> UploadImageAsync(UploadInput input, CancellationToken ct) =>
        FileService.ToDto(await files.UploadAsync(input, "product", ct, FileService.ImageExtensions));

    public async Task<FileDto> AddAttachmentAsync(Guid registerId, Guid id, UploadInput input, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(x => x.Id == id && x.RegisterId == registerId, ct)) throw new NotFoundException("Product");
        var f = await files.UploadAsync(input, "product", ct);
        db.ProductAttachments.Add(new ProductAttachment { ProductId = id, StoredFileId = f.Id, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(f);
    }

    public async Task RemoveAttachmentAsync(Guid registerId, Guid id, Guid fileId, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(x => x.Id == id && x.RegisterId == registerId, ct)) throw new NotFoundException("Product");
        var a = await db.ProductAttachments.FirstOrDefaultAsync(x => x.ProductId == id && x.StoredFileId == fileId, ct) ?? throw new NotFoundException("Attachment");
        db.ProductAttachments.Remove(a);
        await db.SaveChangesAsync(ct);
        await files.DeleteOwnAsync(fileId, ct);
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportAsync(Guid registerId, ProductQuery q, string format, CancellationToken ct)
    {
        var reg = await db.ProductRegisters.AsNoTracking().Include(x => x.CustomFields).FirstOrDefaultAsync(x => x.Id == registerId, ct) ?? throw new NotFoundException("Register");
        var items = (await Filter(registerId, q).Take(50000).ToListAsync(ct)).Select(Map).ToList();
        var fields = reg.CustomFields.OrderBy(f => f.SortOrder).ToList();
        var cols = format == "pdf" ? FixedColumns.Where(c => c.Header is not ("Description" or "Composition")).ToArray() : FixedColumns;
        var headers = cols.Select(c => c.Header).Concat(fields.Select(f => f.Name)).ToList();
        var rows = items.Select(p => (IReadOnlyList<string?>)cols.Select(c => c.Get(p)).Concat(fields.Select(f => p.CustomValues.GetValueOrDefault(f.Id.ToString()))).ToList()).ToList();
        var table = new TableExport(reg.Name, headers, rows, $"{items.Count} product(s)");
        return format.ToLowerInvariant() switch
        {
            "pdf" => (export.ToPdf(table), "application/pdf", "products.pdf"),
            "csv" => (export.ToCsv(table), "text/csv", "products.csv"),
            _ => (export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "products.xlsx")
        };
    }

    public async Task<ImportResult> ImportAsync(Guid registerId, Stream content, string fileName, CancellationToken ct)
    {
        var reg = await db.ProductRegisters.AsNoTracking().Include(x => x.CustomFields).FirstOrDefaultAsync(x => x.Id == registerId, ct) ?? throw new NotFoundException("Register");
        var rows = export.ReadTabular(content, Path.GetExtension(fileName));
        var imported = 0; var errors = new List<string>(); var rowNo = 1;
        foreach (var row in rows)
        {
            rowNo++;
            string? G(string k) => row.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            decimal? D(string k) => G(k) is { } s && decimal.TryParse(s.Replace(",", "").Replace("₹", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
            try
            {
                if (G("Product Name") is not { } name) throw new ValidationAppException("Product Name is required.");
                DateOnly? expiry = null;
                if (G("Expiry Date") is { } ex)
                    expiry = DateOnly.FromDateTime(DateTime.Parse(CustomTableService.Normalize(new CustomTableColumn { Name = "Expiry Date", FieldType = CustomFieldType.Date }, ex), CultureInfo.InvariantCulture));
                var custom = reg.CustomFields.Where(f => G(f.Name) != null).ToDictionary(f => f.Id.ToString(), f => G(f.Name));
                await SaveProductAsync(registerId, null, new ProductRequest(name, G("Brand Name"), G("Composition"), G("Packing"), D("MRP"), D("Purchase Price"), D("GST %") ?? D("GST"),
                    G("HSN Code"), G("Product Code"), G("Manufacturer"), expiry, null, G("Description"), custom), ct);
                imported++;
            }
            catch (Exception e) when (e is ValidationAppException or FormatException)
            {
                if (errors.Count < 100) errors.Add($"Row {rowNo}: {(e is ValidationAppException v && v.Errors.Count > 0 ? string.Join("; ", v.Errors.SelectMany(x => x.Value)) : e.Message)}");
            }
        }
        return new ImportResult(imported, rows.Count - imported, errors);
    }

    private async Task EnsureRegisterAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ProductRegisters.AnyAsync(r => r.Id == id, ct)) throw new NotFoundException("Register");
    }
}
