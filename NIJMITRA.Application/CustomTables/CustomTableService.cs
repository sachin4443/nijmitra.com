using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.CustomTables;

public record ColumnDto(Guid Id, string Name, CustomFieldType FieldType, int SortOrder, bool IsRequired, bool IsUnique, bool IsVisible, string? DefaultValue,
    string? Placeholder, IReadOnlyList<string> Options, decimal? MinValue, decimal? MaxValue, int? MaxLength, string? RegexPattern, string? Formula);
public record ColumnInput(Guid? Id, [Required, StringLength(80, MinimumLength = 1)] string Name, CustomFieldType FieldType, bool IsRequired, bool IsUnique, bool IsVisible,
    [StringLength(500)] string? DefaultValue, [StringLength(150)] string? Placeholder, List<string>? Options, decimal? MinValue, decimal? MaxValue,
    [Range(1, 10000)] int? MaxLength, [StringLength(300)] string? RegexPattern, [StringLength(500)] string? Formula);
public record TableSummaryDto(Guid Id, string Name, string? Description, string? Icon, int ColumnCount, int RecordCount, DateTime UpdatedAt);
public record TableDto(Guid Id, string Name, string? Description, string? Icon, IReadOnlyList<ColumnDto> Columns, int RecordCount, DateTime CreatedAt, DateTime UpdatedAt);
public record TableRequest([Required, StringLength(100, MinimumLength = 1)] string Name, [StringLength(1000)] string? Description, [StringLength(40)] string? Icon,
    List<ColumnInput> Columns, bool ConfirmDataLoss = false);
public record RecordDto(Guid Id, IReadOnlyDictionary<string, string?> Values, string? Notes, IReadOnlyList<FileDto> Attachments, DateTime CreatedAt, DateTime UpdatedAt);
public record RecordRequest(Dictionary<string, string?> Values, [StringLength(4000)] string? Notes);
public record RecordFilter(Guid ColumnId, string Op, string? Value);
public record RecordQuery(string? Search, Guid? SortColumnId, bool SortDesc, List<RecordFilter>? Filters, int Page = 1, int PageSize = 25);
public record SchemaChangeImpact(string Column, string Change, int AffectedValues);
public record ImportResult(int Imported, int Failed, IReadOnlyList<string> Errors);

public sealed class CustomTableService(IAppDbContext db, FileService files, IExportService export, IAuditService audit, TimeProvider clock)
{
    private const int MaxColumns = 60;
    private static readonly CustomFieldType[] Numeric = [CustomFieldType.Number, CustomFieldType.Decimal, CustomFieldType.Currency];

    // ---------------------------------------------------------------- tables
    public async Task<IReadOnlyList<TableSummaryDto>> GetTablesAsync(CancellationToken ct) =>
        await db.CustomTables.AsNoTracking().OrderBy(t => t.Name).Select(t => new TableSummaryDto(t.Id, t.Name, t.Description, t.Icon,
            t.Columns.Count(c => !c.IsDeleted), db.CustomTableRecords.Count(r => r.TableId == t.Id), t.UpdatedAt)).ToListAsync(ct);

    public async Task<TableDto> GetTableAsync(Guid id, CancellationToken ct)
    {
        var t = await db.CustomTables.AsNoTracking().Include(x => x.Columns).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Table");
        var count = await db.CustomTableRecords.CountAsync(r => r.TableId == id, ct);
        return new TableDto(t.Id, t.Name, t.Description, t.Icon, t.Columns.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).Select(MapColumn).ToList(), count, t.CreatedAt, t.UpdatedAt);
    }

    private static ColumnDto MapColumn(CustomTableColumn c) => new(c.Id, c.Name, c.FieldType, c.SortOrder, c.IsRequired, c.IsUnique, c.IsVisible, c.DefaultValue, c.Placeholder,
        string.IsNullOrEmpty(c.OptionsJson) ? [] : JsonSerializer.Deserialize<List<string>>(c.OptionsJson) ?? [], c.MinValue, c.MaxValue, c.MaxLength, c.RegexPattern, c.Formula);

    public async Task<TableDto> CreateTableAsync(TableRequest r, CancellationToken ct)
    {
        ValidateDefinition(r);
        if (await db.CustomTables.AnyAsync(t => t.Name.ToLower() == r.Name.Trim().ToLower(), ct)) throw new ConflictException("You already have a table with this name.");
        var t = new CustomTable { Name = r.Name.Trim(), Description = TextUtil.Clean(r.Description, 1000), Icon = r.Icon };
        db.CustomTables.Add(t);
        var i = 0;
        foreach (var c in r.Columns) db.CustomTableColumns.Add(ApplyColumn(new CustomTableColumn { TableId = t.Id }, c, i++));
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_create", "CustomTable", t.Id.ToString()), ct);
        return await GetTableAsync(t.Id, ct);
    }

    /// <summary>Previews what an edit would do to existing data (type changes and removed columns).</summary>
    public async Task<IReadOnlyList<SchemaChangeImpact>> PreviewChangesAsync(Guid id, TableRequest r, CancellationToken ct)
    {
        var t = await db.CustomTables.AsNoTracking().Include(x => x.Columns).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Table");
        var impacts = new List<SchemaChangeImpact>();
        foreach (var existing in t.Columns.Where(c => !c.IsDeleted))
        {
            var incoming = r.Columns.FirstOrDefault(c => c.Id == existing.Id);
            var count = await db.CustomTableValues.CountAsync(v => v.ColumnId == existing.Id && (v.TextValue != null || v.NumberValue != null || v.DateValue != null || v.BoolValue != null || v.FileId != null), ct);
            if (incoming == null) { if (count > 0) impacts.Add(new(existing.Name, "removed", count)); }
            else if (incoming.FieldType != existing.FieldType && Storage(incoming.FieldType) != Storage(existing.FieldType) && count > 0)
                impacts.Add(new(existing.Name, $"type {existing.FieldType} → {incoming.FieldType} (values that cannot be converted will be cleared)", count));
        }
        return impacts;
    }

    public async Task<TableDto> UpdateTableAsync(Guid id, TableRequest r, CancellationToken ct)
    {
        ValidateDefinition(r);
        var impacts = await PreviewChangesAsync(id, r, ct);
        if (impacts.Count > 0 && !r.ConfirmDataLoss)
            throw new ValidationAppException(new Dictionary<string, string[]> { ["confirmDataLoss"] = impacts.Select(i => $"{i.Column}: {i.Change} — {i.AffectedValues} value(s)").ToArray() });
        var t = await db.CustomTables.Include(x => x.Columns).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Table");
        if (await db.CustomTables.AnyAsync(x => x.Id != id && x.Name.ToLower() == r.Name.Trim().ToLower(), ct)) throw new ConflictException("You already have a table with this name.");
        t.Name = r.Name.Trim(); t.Description = TextUtil.Clean(r.Description, 1000); t.Icon = r.Icon;
        var keep = r.Columns.Where(c => c.Id != null).Select(c => c.Id!.Value).ToHashSet();
        foreach (var removed in t.Columns.Where(c => !c.IsDeleted && !keep.Contains(c.Id)))
        {
            removed.IsDeleted = true;
            await db.CustomTableValues.Where(v => v.ColumnId == removed.Id).ExecuteDeleteAsync(ct);
        }
        var i = 0;
        foreach (var input in r.Columns)
        {
            var col = input.Id is { } cid ? t.Columns.FirstOrDefault(c => c.Id == cid && !c.IsDeleted) ?? throw new ValidationAppException("columns", "Unknown column.") : null;
            if (col == null) { db.CustomTableColumns.Add(ApplyColumn(new CustomTableColumn { TableId = t.Id }, input, i++)); continue; }
            var oldType = col.FieldType;
            ApplyColumn(col, input, i++);
            if (oldType != col.FieldType) await ConvertValuesAsync(col, oldType, ct);
        }
        t.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_update", "CustomTable", t.Id.ToString(), "definition changed"), ct);
        return await GetTableAsync(t.Id, ct);
    }

    public async Task DeleteTableAsync(Guid id, CancellationToken ct)
    {
        var t = await db.CustomTables.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Table");
        db.CustomTables.Remove(t);
        await db.CustomTableRecords.Where(r => r.TableId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted, true).SetProperty(r => r.DeletedAt, clock.GetUtcNow().UtcDateTime), ct);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "CustomTable", id.ToString()), ct);
    }

    private static string Storage(CustomFieldType t) => t switch
    {
        CustomFieldType.Number or CustomFieldType.Decimal or CustomFieldType.Currency => "num",
        CustomFieldType.Date or CustomFieldType.DateTime => "date",
        CustomFieldType.Boolean => "bool",
        CustomFieldType.FileAttachment or CustomFieldType.Image => "file",
        CustomFieldType.Formula => "none",
        _ => "text"
    };

    private async Task ConvertValuesAsync(CustomTableColumn col, CustomFieldType oldType, CancellationToken ct)
    {
        var values = await db.CustomTableValues.Where(v => v.ColumnId == col.Id).ToListAsync(ct);
        foreach (var v in values)
        {
            var asText = Display(oldType, v);
            v.TextValue = null; v.NumberValue = null; v.DateValue = null; v.BoolValue = null; v.FileId = null;
            if (asText == null || col.FieldType == CustomFieldType.Formula) continue;
            try { Store(col, v, Normalize(col, asText)); } catch (ValidationAppException) { /* incompatible → cleared, as confirmed by the user */ }
        }
    }

    private void ValidateDefinition(TableRequest r)
    {
        if (r.Columns == null || r.Columns.Count == 0) throw new ValidationAppException("columns", "Add at least one column.");
        if (r.Columns.Count > MaxColumns) throw new ValidationAppException("columns", $"A table can have at most {MaxColumns} columns.");
        var dup = r.Columns.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new ValidationAppException("columns", $"Column name '{dup.Key}' is used more than once.");
        var numeric = r.Columns.Where(c => Numeric.Contains(c.FieldType)).Select(c => c.Name.Trim()).ToList();
        foreach (var c in r.Columns)
        {
            if (c.FieldType is CustomFieldType.Dropdown or CustomFieldType.MultiSelect && (c.Options == null || c.Options.Count(o => !string.IsNullOrWhiteSpace(o)) == 0))
                throw new ValidationAppException("columns", $"'{c.Name}' needs at least one option.");
            if (c.FieldType == CustomFieldType.Formula && (string.IsNullOrWhiteSpace(c.Formula) || !FormulaEvaluator.IsValid(c.Formula, numeric)))
                throw new ValidationAppException("columns", $"Formula for '{c.Name}' is invalid. Use number columns like {{Price}} * {{Qty}}.");
            if (!string.IsNullOrEmpty(c.RegexPattern))
                try { _ = new Regex(c.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(50)); }
                catch { throw new ValidationAppException("columns", $"Validation pattern for '{c.Name}' is invalid."); }
            if (c.MinValue > c.MaxValue) throw new ValidationAppException("columns", $"Minimum is greater than maximum for '{c.Name}'.");
        }
    }

    private static CustomTableColumn ApplyColumn(CustomTableColumn col, ColumnInput c, int order)
    {
        col.Name = c.Name.Trim(); col.FieldType = c.FieldType; col.SortOrder = order; col.IsRequired = c.IsRequired && c.FieldType != CustomFieldType.Formula;
        col.IsUnique = c.IsUnique && Storage(c.FieldType) is "text" or "num" or "date"; col.IsVisible = c.IsVisible; col.DefaultValue = TextUtil.Clean(c.DefaultValue, 500);
        col.Placeholder = TextUtil.Clean(c.Placeholder, 150);
        col.OptionsJson = c.FieldType is CustomFieldType.Dropdown or CustomFieldType.MultiSelect
            ? JsonSerializer.Serialize(c.Options!.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().Take(200).ToList()) : null;
        col.MinValue = c.MinValue; col.MaxValue = c.MaxValue; col.MaxLength = c.MaxLength; col.RegexPattern = TextUtil.Clean(c.RegexPattern, 300);
        col.Formula = c.FieldType == CustomFieldType.Formula ? c.Formula!.Trim() : null;
        return col;
    }

    // ---------------------------------------------------------------- records
    public async Task<PagedResult<RecordDto>> GetRecordsAsync(Guid tableId, RecordQuery q, CancellationToken ct)
    {
        var columns = await LoadColumnsAsync(tableId, ct);
        var query = db.CustomTableRecords.AsNoTracking().Where(r => r.TableId == tableId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = TextUtil.LikePattern(q.Search);
            query = query.Where(r => r.Values.Any(v => v.TextValue != null && EF.Functions.ILike(v.TextValue, like)) || (r.Notes != null && EF.Functions.ILike(r.Notes, like)));
        }
        foreach (var f in q.Filters ?? [])
        {
            var col = columns.FirstOrDefault(c => c.Id == f.ColumnId) ?? throw new ValidationAppException("filters", "Unknown filter column.");
            query = ApplyFilter(query, col, f);
        }
        if (q.SortColumnId is { } sc && columns.FirstOrDefault(c => c.Id == sc) is { } sortCol && sortCol.FieldType != CustomFieldType.Formula)
        {
            query = Storage(sortCol.FieldType) switch
            {
                "num" => q.SortDesc ? query.OrderByDescending(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.NumberValue).FirstOrDefault())
                                    : query.OrderBy(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.NumberValue).FirstOrDefault()),
                "date" => q.SortDesc ? query.OrderByDescending(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.DateValue).FirstOrDefault())
                                     : query.OrderBy(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.DateValue).FirstOrDefault()),
                "bool" => q.SortDesc ? query.OrderByDescending(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.BoolValue).FirstOrDefault())
                                     : query.OrderBy(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.BoolValue).FirstOrDefault()),
                _ => q.SortDesc ? query.OrderByDescending(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.TextValue).FirstOrDefault())
                                : query.OrderBy(r => r.Values.Where(v => v.ColumnId == sc).Select(v => v.TextValue).FirstOrDefault())
            };
        }
        else query = query.OrderByDescending(r => r.CreatedAt);
        var page = await query.Include(r => r.Values).Include(r => r.Attachments).ThenInclude(a => a.StoredFile).AsSplitQuery().ToPagedAsync(q.Page, q.PageSize, ct);
        return new PagedResult<RecordDto>(page.Items.Select(r => MapRecord(r, columns)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    private static IQueryable<CustomTableRecord> ApplyFilter(IQueryable<CustomTableRecord> q, CustomTableColumn col, RecordFilter f)
    {
        var id = col.Id;
        var op = (f.Op ?? "eq").ToLowerInvariant();
        if (op == "empty") return q.Where(r => !r.Values.Any(v => v.ColumnId == id && (v.TextValue != null || v.NumberValue != null || v.DateValue != null || v.BoolValue != null || v.FileId != null)));
        if (op == "notempty") return q.Where(r => r.Values.Any(v => v.ColumnId == id && (v.TextValue != null || v.NumberValue != null || v.DateValue != null || v.BoolValue != null || v.FileId != null)));
        var value = f.Value ?? "";
        switch (Storage(col.FieldType))
        {
            case "num":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) throw new ValidationAppException("filters", $"'{col.Name}' filter needs a number.");
                return op switch
                {
                    "gt" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.NumberValue > n)),
                    "gte" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.NumberValue >= n)),
                    "lt" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.NumberValue < n)),
                    "lte" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.NumberValue <= n)),
                    _ => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.NumberValue == n))
                };
            case "date":
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) throw new ValidationAppException("filters", $"'{col.Name}' filter needs a date.");
                var next = d.Date.AddDays(1);
                return op switch
                {
                    "gt" or "gte" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.DateValue >= d)),
                    "lt" or "lte" => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.DateValue < next)),
                    _ => q.Where(r => r.Values.Any(v => v.ColumnId == id && v.DateValue >= d.Date && v.DateValue < next))
                };
            case "bool":
                var b = value is "true" or "1" or "yes";
                return q.Where(r => r.Values.Any(v => v.ColumnId == id && v.BoolValue == b));
            default:
                if (op == "eq") return q.Where(r => r.Values.Any(v => v.ColumnId == id && v.TextValue != null && v.TextValue.ToLower() == value.ToLower()));
                var like = TextUtil.LikePattern(value);
                return q.Where(r => r.Values.Any(v => v.ColumnId == id && v.TextValue != null && EF.Functions.ILike(v.TextValue, like)));
        }
    }

    public async Task<RecordDto> GetRecordAsync(Guid tableId, Guid recordId, CancellationToken ct)
    {
        var columns = await LoadColumnsAsync(tableId, ct);
        var r = await db.CustomTableRecords.AsNoTracking().Include(x => x.Values).Include(x => x.Attachments).ThenInclude(a => a.StoredFile)
            .FirstOrDefaultAsync(x => x.Id == recordId && x.TableId == tableId, ct) ?? throw new NotFoundException("Record");
        return MapRecord(r, columns);
    }

    public async Task<RecordDto> SaveRecordAsync(Guid tableId, Guid? recordId, RecordRequest req, CancellationToken ct)
    {
        var columns = await LoadColumnsAsync(tableId, ct);
        CustomTableRecord record;
        if (recordId is { } rid)
            record = await db.CustomTableRecords.Include(x => x.Values).FirstOrDefaultAsync(x => x.Id == rid && x.TableId == tableId, ct) ?? throw new NotFoundException("Record");
        else { record = new CustomTableRecord { TableId = tableId }; db.CustomTableRecords.Add(record); }
        var errors = new Dictionary<string, string[]>();
        foreach (var col in columns.Where(c => c.FieldType != CustomFieldType.Formula))
        {
            req.Values.TryGetValue(col.Id.ToString(), out var raw);
            if (recordId == null && string.IsNullOrWhiteSpace(raw) && col.DefaultValue != null) raw = col.DefaultValue;
            try
            {
                var normalized = string.IsNullOrWhiteSpace(raw) ? null : Normalize(col, raw.Trim());
                if (normalized == null && col.IsRequired) throw new ValidationAppException(col.Name + " is required.");
                if (normalized != null && Storage(col.FieldType) == "file" && !await db.StoredFiles.AnyAsync(f => f.Id == Guid.Parse(normalized), ct))
                    throw new ValidationAppException("File not found.");
                var value = record.Values.FirstOrDefault(v => v.ColumnId == col.Id);
                if (value == null) { value = new CustomTableValue { RecordId = record.Id, ColumnId = col.Id }; db.CustomTableValues.Add(value); record.Values.Add(value); }
                value.TextValue = null; value.NumberValue = null; value.DateValue = null; value.BoolValue = null; value.FileId = null;
                if (normalized != null) Store(col, value, normalized);
                if (col.IsUnique && normalized != null && await IsDuplicateAsync(col, value, record.Id, ct))
                    throw new ValidationAppException($"'{raw}' already exists in {col.Name}.");
            }
            catch (ValidationAppException ex) { errors[col.Id.ToString()] = [ex.Message]; }
        }
        if (errors.Count > 0)
        {
            if (recordId == null) db.CustomTableRecords.Entry(record).State = EntityState.Detached;
            throw new ValidationAppException(errors);
        }
        record.Notes = TextUtil.Clean(req.Notes, 4000);
        record.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(recordId == null ? "record_create" : "record_update", "CustomTableRecord", record.Id.ToString()), ct);
        return await GetRecordAsync(tableId, record.Id, ct);
    }

    private async Task<bool> IsDuplicateAsync(CustomTableColumn col, CustomTableValue v, Guid recordId, CancellationToken ct)
    {
        var q = db.CustomTableValues.Where(x => x.ColumnId == col.Id && x.RecordId != recordId && !x.Record!.IsDeleted);
        return Storage(col.FieldType) switch
        {
            "num" => await q.AnyAsync(x => x.NumberValue == v.NumberValue, ct),
            "date" => await q.AnyAsync(x => x.DateValue == v.DateValue, ct),
            _ => await q.AnyAsync(x => x.TextValue != null && x.TextValue.ToLower() == v.TextValue!.ToLower(), ct)
        };
    }

    public async Task DeleteRecordAsync(Guid tableId, Guid recordId, CancellationToken ct)
    {
        var r = await db.CustomTableRecords.FirstOrDefaultAsync(x => x.Id == recordId && x.TableId == tableId, ct) ?? throw new NotFoundException("Record");
        db.CustomTableRecords.Remove(r);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("record_delete", "CustomTableRecord", recordId.ToString()), ct);
    }

    public async Task<FileDto> UploadFileAsync(Guid tableId, UploadInput input, bool imageOnly, CancellationToken ct)
    {
        if (!await db.CustomTables.AnyAsync(t => t.Id == tableId, ct)) throw new NotFoundException("Table");
        var f = await files.UploadAsync(input, "custom-table", ct, imageOnly ? FileService.ImageExtensions : null);
        return FileService.ToDto(f);
    }

    public async Task<FileDto> AddAttachmentAsync(Guid tableId, Guid recordId, UploadInput input, CancellationToken ct)
    {
        if (!await db.CustomTableRecords.AnyAsync(x => x.Id == recordId && x.TableId == tableId, ct)) throw new NotFoundException("Record");
        var f = await files.UploadAsync(input, "custom-table", ct);
        db.CustomTableAttachments.Add(new CustomTableAttachment { RecordId = recordId, StoredFileId = f.Id, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        return FileService.ToDto(f);
    }

    public async Task RemoveAttachmentAsync(Guid tableId, Guid recordId, Guid fileId, CancellationToken ct)
    {
        if (!await db.CustomTableRecords.AnyAsync(x => x.Id == recordId && x.TableId == tableId, ct)) throw new NotFoundException("Record");
        var a = await db.CustomTableAttachments.FirstOrDefaultAsync(x => x.RecordId == recordId && x.StoredFileId == fileId, ct) ?? throw new NotFoundException("Attachment");
        db.CustomTableAttachments.Remove(a);
        await db.SaveChangesAsync(ct);
        await files.DeleteOwnAsync(fileId, ct);
    }

    // ---------------------------------------------------------------- import / export
    public async Task<(byte[] Data, string ContentType, string FileName)> ExportAsync(Guid tableId, string format, CancellationToken ct)
    {
        var table = await GetTableAsync(tableId, ct);
        var columns = await LoadColumnsAsync(tableId, ct);
        var records = await db.CustomTableRecords.AsNoTracking().Where(r => r.TableId == tableId).Include(r => r.Values).OrderBy(r => r.CreatedAt).Take(50000).ToListAsync(ct);
        var fileNames = await db.StoredFiles.AsNoTracking().Where(f => f.Purpose == "custom-table").ToDictionaryAsync(f => f.Id, f => f.OriginalName, ct);
        var visible = columns.Where(c => c.IsVisible || format != "pdf").ToList();
        var rows = records.Select(r =>
        {
            var mapped = MapRecord(r, columns).Values;
            return (IReadOnlyList<string?>)visible.Select(c =>
            {
                var v = mapped.GetValueOrDefault(c.Id.ToString());
                if (v != null && Storage(c.FieldType) == "file" && Guid.TryParse(v, out var fid)) return fileNames.GetValueOrDefault(fid, "file");
                if (v != null && c.FieldType == CustomFieldType.MultiSelect) return string.Join(", ", JsonSerializer.Deserialize<List<string>>(v) ?? []);
                return v;
            }).Append(r.Notes).ToList();
        }).ToList();
        var exp = new TableExport(table.Name, visible.Select(c => c.Name).Append("Notes").ToList(), rows, table.Description);
        var safe = Regex.Replace(table.Name, "[^A-Za-z0-9_-]+", "-").Trim('-').ToLowerInvariant();
        return format.ToLowerInvariant() switch
        {
            "pdf" => (export.ToPdf(exp), "application/pdf", $"{safe}.pdf"),
            "csv" => (export.ToCsv(exp), "text/csv", $"{safe}.csv"),
            _ => (export.ToExcel(exp), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safe}.xlsx")
        };
    }

    /// <summary>Imports rows from CSV/Excel. Headers are matched to column names (case-insensitive). Each row is validated like a manual entry.</summary>
    public async Task<ImportResult> ImportAsync(Guid tableId, Stream content, string fileName, CancellationToken ct)
    {
        var columns = await LoadColumnsAsync(tableId, ct);
        var rows = export.ReadTabular(content, Path.GetExtension(fileName));
        var imported = 0; var errors = new List<string>();
        var rowNo = 1;
        foreach (var row in rows)
        {
            rowNo++;
            var values = new Dictionary<string, string?>();
            foreach (var col in columns.Where(c => Storage(c.FieldType) is not ("file" or "none")))
            {
                if (!row.TryGetValue(col.Name, out var v)) continue;
                values[col.Id.ToString()] = col.FieldType == CustomFieldType.MultiSelect && v != null && !v.TrimStart().StartsWith('[')
                    ? JsonSerializer.Serialize(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : v;
            }
            row.TryGetValue("Notes", out var notes);
            try { await SaveRecordAsync(tableId, null, new RecordRequest(values, notes), ct); imported++; }
            catch (ValidationAppException ex)
            {
                var msg = ex.Errors.Count > 0 ? string.Join("; ", ex.Errors.Select(e => $"{columns.FirstOrDefault(c => c.Id.ToString() == e.Key)?.Name ?? e.Key}: {string.Join(", ", e.Value)}")) : ex.Message;
                if (errors.Count < 100) errors.Add($"Row {rowNo}: {msg}");
            }
        }
        await audit.LogAsync(new AuditEntry("custom_table_import", "CustomTable", tableId.ToString(), $"{imported} imported, {rows.Count - imported} failed"), ct);
        return new ImportResult(imported, rows.Count - imported, errors);
    }

    // ---------------------------------------------------------------- value conversion
    private async Task<List<CustomTableColumn>> LoadColumnsAsync(Guid tableId, CancellationToken ct)
    {
        if (!await db.CustomTables.AnyAsync(t => t.Id == tableId, ct)) throw new NotFoundException("Table");
        return await db.CustomTableColumns.AsNoTracking().Where(c => c.TableId == tableId && !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync(ct);
    }

    private static RecordDto MapRecord(CustomTableRecord r, List<CustomTableColumn> columns)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var c in columns.Where(c => c.FieldType != CustomFieldType.Formula))
            dict[c.Id.ToString()] = r.Values.FirstOrDefault(v => v.ColumnId == c.Id) is { } v ? Display(c.FieldType, v) : null;
        foreach (var c in columns.Where(c => c.FieldType == CustomFieldType.Formula))
        {
            var result = FormulaEvaluator.Evaluate(c.Formula!, name =>
            {
                var src = columns.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                return src == null ? null : r.Values.FirstOrDefault(v => v.ColumnId == src.Id)?.NumberValue;
            });
            dict[c.Id.ToString()] = result?.ToString("0.####", CultureInfo.InvariantCulture);
        }
        return new RecordDto(r.Id, dict, r.Notes, r.Attachments.Where(a => a.StoredFile != null).Select(a => FileService.ToDto(a.StoredFile!)).ToList(), r.CreatedAt, r.UpdatedAt);
    }

    private static string? Display(CustomFieldType t, CustomTableValue v) => Storage(t) switch
    {
        "num" => v.NumberValue?.ToString(t == CustomFieldType.Number ? "0.########" : "0.00######", CultureInfo.InvariantCulture),
        "date" => v.DateValue == null ? null : t == CustomFieldType.Date ? v.DateValue.Value.ToString("yyyy-MM-dd") : v.DateValue.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        "bool" => v.BoolValue?.ToString().ToLowerInvariant(),
        "file" => v.FileId?.ToString(),
        _ => v.TextValue
    };

    /// <summary>Validates and canonicalises a raw value for the column type. Throws ValidationAppException with a friendly message.</summary>
    public static string Normalize(CustomTableColumn col, string raw)
    {
        switch (col.FieldType)
        {
            case CustomFieldType.Number:
            case CustomFieldType.Decimal:
            case CustomFieldType.Currency:
                var cleaned = raw.Replace(",", "").Replace("₹", "").Trim();
                if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) throw new ValidationAppException($"{col.Name} must be a number.");
                if (col.FieldType == CustomFieldType.Number && n != Math.Truncate(n)) throw new ValidationAppException($"{col.Name} must be a whole number.");
                if (col.MinValue != null && n < col.MinValue) throw new ValidationAppException($"{col.Name} must be at least {col.MinValue}.");
                if (col.MaxValue != null && n > col.MaxValue) throw new ValidationAppException($"{col.Name} must be at most {col.MaxValue}.");
                if (Math.Abs(n) > 1_000_000_000_000m) throw new ValidationAppException($"{col.Name} is too large.");
                return (col.FieldType == CustomFieldType.Currency ? Math.Round(n, 2) : n).ToString(CultureInfo.InvariantCulture);
            case CustomFieldType.Date:
                if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
                    && !DateTime.TryParseExact(raw, ["dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy", "d-M-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    throw new ValidationAppException($"{col.Name} must be a valid date.");
                return d.ToString("yyyy-MM-dd");
            case CustomFieldType.DateTime:
                if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                    throw new ValidationAppException($"{col.Name} must be a valid date and time.");
                return dt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            case CustomFieldType.Boolean:
                return raw.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "y" or "haan" => "true",
                    "false" or "0" or "no" or "n" or "nahi" => "false",
                    _ => throw new ValidationAppException($"{col.Name} must be Yes or No.")
                };
            case CustomFieldType.Email:
                if (!MailAddress.TryCreate(raw, out var m) || m.Address != raw) throw new ValidationAppException($"{col.Name} must be a valid email address.");
                return raw.ToLowerInvariant();
            case CustomFieldType.Phone:
                var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
                if (digits.TrimStart('+').Length is < 6 or > 15) throw new ValidationAppException($"{col.Name} must be a valid phone number.");
                return raw.Length > 30 ? digits : raw;
            case CustomFieldType.Url:
                var u = raw.Contains("://") ? raw : "https://" + raw;
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ValidationAppException($"{col.Name} must be a valid web address.");
                return u;
            case CustomFieldType.Dropdown:
                var opts = JsonSerializer.Deserialize<List<string>>(col.OptionsJson ?? "[]") ?? [];
                var match = opts.FirstOrDefault(o => string.Equals(o, raw, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationAppException($"{col.Name}: '{raw}' is not one of the allowed options.");
                return match;
            case CustomFieldType.MultiSelect:
                var allowed = JsonSerializer.Deserialize<List<string>>(col.OptionsJson ?? "[]") ?? [];
                List<string> selected;
                try { selected = raw.TrimStart().StartsWith('[') ? JsonSerializer.Deserialize<List<string>>(raw) ?? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); }
                catch (JsonException) { throw new ValidationAppException($"{col.Name} has an invalid selection."); }
                var canon = selected.Select(s => allowed.FirstOrDefault(o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationAppException($"{col.Name}: '{s}' is not an allowed option.")).Distinct().ToList();
                return JsonSerializer.Serialize(canon);
            case CustomFieldType.FileAttachment:
            case CustomFieldType.Image:
                if (!Guid.TryParse(raw, out var fid)) throw new ValidationAppException($"{col.Name} must reference an uploaded file.");
                return fid.ToString();
            default:
                var max = col.MaxLength ?? (col.FieldType == CustomFieldType.LongText ? 10000 : 500);
                if (raw.Length > max) throw new ValidationAppException($"{col.Name} must be at most {max} characters.");
                if (!string.IsNullOrEmpty(col.RegexPattern))
                {
                    try { if (!Regex.IsMatch(raw, col.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100))) throw new ValidationAppException($"{col.Name} is not in the expected format."); }
                    catch (RegexMatchTimeoutException) { throw new ValidationAppException($"{col.Name} could not be validated."); }
                }
                return raw;
        }
    }

    private static void Store(CustomTableColumn col, CustomTableValue v, string normalized)
    {
        switch (Storage(col.FieldType))
        {
            case "num": v.NumberValue = decimal.Parse(normalized, CultureInfo.InvariantCulture); break;
            case "date": v.DateValue = DateTime.SpecifyKind(DateTime.Parse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc); break;
            case "bool": v.BoolValue = normalized == "true"; break;
            case "file": v.FileId = Guid.Parse(normalized); break;
            default: v.TextValue = normalized; break;
        }
    }
}
