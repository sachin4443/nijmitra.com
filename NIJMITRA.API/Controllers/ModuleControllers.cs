using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.CustomTables;
using NIJMITRA.Application.Documents;
using NIJMITRA.Application.Files;
using NIJMITRA.Application.Finance;
using NIJMITRA.Application.Journal;
using NIJMITRA.Application.People;
using NIJMITRA.Application.Products;
using NIJMITRA.Application.Vault;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.API.Controllers;

[ApiController, Authorize]
public abstract class AppControllerBase : ControllerBase
{
    protected FileContentResult Download((byte[] Data, string ContentType, string FileName) f) => File(f.Data, f.ContentType, f.FileName);
    protected FileStreamResult Stream(FileStreamResultDto f, bool inline)
    {
        Response.Headers.CacheControl = "private, no-store";
        return inline ? File(f.Content, f.ContentType) : File(f.Content, f.ContentType, f.FileName);
    }
}

[Route("api/finance")]
public class FinanceController(FinanceService svc) : AppControllerBase
{
    [HttpGet("categories")] public Task<IReadOnlyList<CategoryDto>> Categories([FromQuery] CategoryType? type, CancellationToken ct) => svc.GetCategoriesAsync(type, ct);
    [HttpPost("categories")] public Task<CategoryDto> CreateCategory(CategoryRequest r, CancellationToken ct) => svc.CreateCategoryAsync(r, ct);
    [HttpPut("categories/{id:guid}")] public async Task<IActionResult> UpdateCategory(Guid id, CategoryRequest r, CancellationToken ct) { await svc.UpdateCategoryAsync(id, r, ct); return NoContent(); }
    [HttpDelete("categories/{id:guid}")] public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) { await svc.DeleteCategoryAsync(id, ct); return NoContent(); }

    [HttpGet("expenses")]
    public async Task<IActionResult> Expenses([FromQuery] ExpenseQuery q, CancellationToken ct) =>
        Ok(new { page = await svc.GetExpensesAsync(q, ct), total = await svc.SumExpensesAsync(q, ct) });
    [HttpGet("expenses/{id:guid}")] public Task<ExpenseDto> Expense(Guid id, CancellationToken ct) => svc.GetExpenseAsync(id, ct);
    [HttpPost("expenses")] public Task<ExpenseDto> CreateExpense(ExpenseRequest r, CancellationToken ct) => svc.CreateExpenseAsync(r, ct);
    [HttpPut("expenses/{id:guid}")] public Task<ExpenseDto> UpdateExpense(Guid id, ExpenseRequest r, CancellationToken ct) => svc.UpdateExpenseAsync(id, r, ct);
    [HttpDelete("expenses/{id:guid}")] public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken ct) { await svc.DeleteExpenseAsync(id, ct); return NoContent(); }
    [HttpGet("expenses/export")] public async Task<IActionResult> ExportExpenses([FromQuery] ExpenseQuery q, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportExpensesAsync(q, format, ct));

    [HttpGet("income")] public Task<PagedResult<IncomeDto>> Income([FromQuery] IncomeQuery q, CancellationToken ct) => svc.GetIncomeAsync(q, ct);
    [HttpPost("income")] public Task<IncomeDto> CreateIncome(IncomeRequest r, CancellationToken ct) => svc.SaveIncomeAsync(null, r, ct);
    [HttpPut("income/{id:guid}")] public Task<IncomeDto> UpdateIncome(Guid id, IncomeRequest r, CancellationToken ct) => svc.SaveIncomeAsync(id, r, ct);
    [HttpDelete("income/{id:guid}")] public async Task<IActionResult> DeleteIncome(Guid id, CancellationToken ct) { await svc.DeleteIncomeAsync(id, ct); return NoContent(); }
    [HttpGet("income/export")] public async Task<IActionResult> ExportIncome([FromQuery] IncomeQuery q, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportIncomeAsync(q, format, ct));

    [HttpGet("budget/{year:int}/{month:int}")] public Task<BudgetDto> Budget(int year, int month, CancellationToken ct) => svc.GetBudgetAsync(year, month, ct);
    [HttpPut("budget")] public Task<BudgetDto> SaveBudget(BudgetRequest r, CancellationToken ct) => svc.SaveBudgetAsync(r, ct);
    [HttpGet("budget/comparison")] public Task<IReadOnlyList<MonthComparison>> Compare([FromQuery] int months = 6, CancellationToken ct = default) => svc.CompareMonthsAsync(months, ct);

    [HttpGet("recurring")] public Task<IReadOnlyList<RecurringDto>> Recurring(CancellationToken ct) => svc.GetRecurringAsync(ct);
    [HttpPost("recurring")] public async Task<IActionResult> CreateRecurring(RecurringRequest r, CancellationToken ct) => Ok(new { id = await svc.SaveRecurringAsync(null, r, ct) });
    [HttpPut("recurring/{id:guid}")] public async Task<IActionResult> UpdateRecurring(Guid id, RecurringRequest r, CancellationToken ct) => Ok(new { id = await svc.SaveRecurringAsync(id, r, ct) });
    [HttpDelete("recurring/{id:guid}")] public async Task<IActionResult> DeleteRecurring(Guid id, CancellationToken ct) { await svc.DeleteRecurringAsync(id, ct); return NoContent(); }
    [HttpGet("recurring/occurrences")]
    public Task<IReadOnlyList<OccurrenceDto>> Occurrences([FromQuery] OccurrenceStatus? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => svc.GetOccurrencesAsync(status, from, to, ct);
    [HttpPost("recurring/occurrences/{id:guid}/pay")] public Task<OccurrenceDto> Pay(Guid id, [FromQuery] DateOnly? paidOn, CancellationToken ct) => svc.MarkOccurrenceAsync(id, true, paidOn, ct);
    [HttpPost("recurring/occurrences/{id:guid}/skip")] public Task<OccurrenceDto> Skip(Guid id, CancellationToken ct) => svc.MarkOccurrenceAsync(id, false, null, ct);

    [HttpGet("reports")] public Task<ReportDto> Report([FromQuery] string period = "monthly", [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default) => svc.GetReportAsync(period, from, to, ct);
    [HttpGet("reports/export")]
    public async Task<IActionResult> ExportReport([FromQuery] string period = "monthly", [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string format = "pdf", CancellationToken ct = default)
        => Download(await svc.ExportReportAsync(period, from, to, format, ct));
}

[Route("api/uploads")]
public class UploadsController(FileService files) : AppControllerBase
{
    /// <summary>Generic upload for receipts and other single-file fields; returns the file id to attach to a record.</summary>
    [HttpPost, RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Upload(IFormFile file, [FromQuery] string purpose = "attachment", CancellationToken ct = default)
    {
        var allowed = new[] { "receipt", "attachment", "custom-table", "product", "chat" };
        return FileService.ToDto(await files.UploadAsync(await file.ToUploadAsync(), allowed.Contains(purpose) ? purpose : "attachment", ct));
    }
}

[Route("api/documents")]
public class DocumentsController(DocumentService svc) : AppControllerBase
{
    [HttpGet("categories")] public IReadOnlyList<string> Categories() => DocumentService.Categories;
    [HttpGet("folders")] public Task<IReadOnlyList<FolderDto>> Folders(CancellationToken ct) => svc.GetFoldersAsync(ct);
    [HttpPost("folders")] public Task<FolderDto> CreateFolder(FolderRequest r, CancellationToken ct) => svc.SaveFolderAsync(null, r, ct);
    [HttpPut("folders/{id:guid}")] public Task<FolderDto> UpdateFolder(Guid id, FolderRequest r, CancellationToken ct) => svc.SaveFolderAsync(id, r, ct);
    [HttpDelete("folders/{id:guid}")] public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct) { await svc.DeleteFolderAsync(id, ct); return NoContent(); }

    [HttpGet] public Task<PagedResult<DocumentDto>> List([FromQuery] DocumentQuery q, CancellationToken ct) => svc.GetDocumentsAsync(q, ct);
    [HttpGet("history")] public Task<IReadOnlyList<UploadHistoryDto>> History([FromQuery] int take = 50, CancellationToken ct = default) => svc.GetUploadHistoryAsync(take, ct);
    [HttpGet("{id:guid}")] public Task<DocumentDto> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);

    [HttpPost, RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<DocumentDto> Upload(IFormFile file, [FromForm] string? name, [FromForm] Guid? folderId, [FromForm] string category, [FromForm] string? description, [FromForm] string? tags, CancellationToken ct)
        => await svc.UploadAsync(await file.ToUploadAsync(), new DocumentMetaRequest(name ?? "", folderId, category, description, tags?.Split(',').ToList()), ct);

    [HttpPost("{id:guid}/versions"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<DocumentDto> NewVersion(Guid id, IFormFile file, [FromForm] string? note, CancellationToken ct) => await svc.AddVersionAsync(id, await file.ToUploadAsync(), note, ct);
    [HttpGet("{id:guid}/versions")] public Task<IReadOnlyList<DocumentVersionDto>> Versions(Guid id, CancellationToken ct) => svc.GetVersionsAsync(id, ct);
    [HttpPut("{id:guid}")] public Task<DocumentDto> Update(Guid id, DocumentMetaRequest r, CancellationToken ct) => svc.UpdateMetaAsync(id, r, ct);
    [HttpPost("{id:guid}/move")] public async Task<IActionResult> Move(Guid id, [FromQuery] Guid? folderId, CancellationToken ct) { await svc.MoveAsync(id, folderId, ct); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpGet("{id:guid}/download")] public async Task<IActionResult> Download(Guid id, [FromQuery] Guid? versionId, [FromQuery] bool inline = false, CancellationToken ct = default) => Stream(await svc.OpenAsync(id, versionId, ct), inline);

    [HttpPost("{id:guid}/shares")] public Task<ShareLinkDto> Share(Guid id, ShareRequest r, CancellationToken ct) => svc.CreateShareAsync(id, r, ct);
    [HttpGet("{id:guid}/shares")] public Task<IReadOnlyList<ShareLinkDto>> Shares(Guid id, CancellationToken ct) => svc.GetSharesAsync(id, ct);
    [HttpDelete("shares/{shareId:guid}")] public async Task<IActionResult> Revoke(Guid shareId, CancellationToken ct) { await svc.RevokeShareAsync(shareId, ct); return NoContent(); }
}

[ApiController, AllowAnonymous, Route("api/public")]
public class PublicShareController(DocumentService svc) : ControllerBase
{
    [HttpGet("share/{token}"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Shared(string token, CancellationToken ct)
    {
        var f = await svc.OpenSharedAsync(token, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex";
        return File(f.Content, f.ContentType, f.FileName);
    }
}

[Route("api/vault"), DenyImpersonation]
public class VaultController(VaultService svc) : AppControllerBase
{
    [HttpGet] public async Task<IActionResult> Header(CancellationToken ct) => Ok(new { configured = await svc.GetHeaderAsync(ct) is { } h, header = await svc.GetHeaderAsync(ct) });
    [HttpPost("setup")] public async Task<IActionResult> Setup(VaultSetupRequest r, CancellationToken ct) { await svc.SetupAsync(r, ct); return NoContent(); }
    [HttpPut("key")] public async Task<IActionResult> Rekey(VaultRekeyRequest r, CancellationToken ct) { await svc.RekeyAsync(r, ct); return NoContent(); }
    [HttpGet("categories")] public Task<IReadOnlyList<string>> Categories(CancellationToken ct) => svc.GetCategoriesAsync(ct);
    [HttpGet("entries")] public Task<IReadOnlyList<EntryDto>> Entries([FromQuery] string? category, [FromQuery] bool? favorites, [FromQuery] string? search, CancellationToken ct) => svc.GetEntriesAsync(category, favorites, search, ct);
    [HttpGet("entries/{id:guid}")] public Task<EntryDto> Entry(Guid id, CancellationToken ct) => svc.GetEntryAsync(id, ct);
    [HttpPost("entries")] public Task<EntryDto> Create(EntryRequest r, CancellationToken ct) => svc.SaveEntryAsync(null, r, ct);
    [HttpPut("entries/{id:guid}")] public Task<EntryDto> Update(Guid id, EntryRequest r, CancellationToken ct) => svc.SaveEntryAsync(id, r, ct);
    [HttpPost("entries/{id:guid}/favorite")] public async Task<IActionResult> Fav(Guid id, [FromQuery] bool value, CancellationToken ct) { await svc.ToggleFavoriteAsync(id, value, ct); return NoContent(); }
    [HttpDelete("entries/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteEntryAsync(id, ct); return NoContent(); }
    [HttpGet("entries/{id:guid}/history")] public Task<IReadOnlyList<HistoryDto>> History(Guid id, CancellationToken ct) => svc.GetHistoryAsync(id, ct);
    [HttpGet("notes")] public Task<IReadOnlyList<NoteDto>> Notes(CancellationToken ct) => svc.GetNotesAsync(ct);
    [HttpPost("notes")] public Task<NoteDto> CreateNote(NoteRequest r, CancellationToken ct) => svc.SaveNoteAsync(null, r, ct);
    [HttpPut("notes/{id:guid}")] public Task<NoteDto> UpdateNote(Guid id, NoteRequest r, CancellationToken ct) => svc.SaveNoteAsync(id, r, ct);
    [HttpDelete("notes/{id:guid}")] public async Task<IActionResult> DeleteNote(Guid id, CancellationToken ct) { await svc.DeleteNoteAsync(id, ct); return NoContent(); }
    [HttpPost("export")] public Task<VaultBackup> Export(CancellationToken ct) => svc.ExportAsync(ct);
    [HttpPost("import")] public async Task<IActionResult> Import(VaultImportRequest r, CancellationToken ct) => Ok(new { imported = await svc.ImportAsync(r, ct) });
}

[Route("api/tables")]
public class CustomTablesController(CustomTableService svc) : AppControllerBase
{
    [HttpGet] public Task<IReadOnlyList<TableSummaryDto>> List(CancellationToken ct) => svc.GetTablesAsync(ct);
    [HttpGet("{id:guid}")] public Task<TableDto> Get(Guid id, CancellationToken ct) => svc.GetTableAsync(id, ct);
    [HttpPost] public Task<TableDto> Create(TableRequest r, CancellationToken ct) => svc.CreateTableAsync(r, ct);
    [HttpPost("{id:guid}/preview-changes")] public Task<IReadOnlyList<SchemaChangeImpact>> Preview(Guid id, TableRequest r, CancellationToken ct) => svc.PreviewChangesAsync(id, r, ct);
    [HttpPut("{id:guid}")] public Task<TableDto> Update(Guid id, TableRequest r, CancellationToken ct) => svc.UpdateTableAsync(id, r, ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteTableAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/records/query")] public Task<PagedResult<RecordDto>> Records(Guid id, RecordQuery q, CancellationToken ct) => svc.GetRecordsAsync(id, q, ct);
    [HttpGet("{id:guid}/records/{recordId:guid}")] public Task<RecordDto> Record(Guid id, Guid recordId, CancellationToken ct) => svc.GetRecordAsync(id, recordId, ct);
    [HttpPost("{id:guid}/records")] public Task<RecordDto> CreateRecord(Guid id, RecordRequest r, CancellationToken ct) => svc.SaveRecordAsync(id, null, r, ct);
    [HttpPut("{id:guid}/records/{recordId:guid}")] public Task<RecordDto> UpdateRecord(Guid id, Guid recordId, RecordRequest r, CancellationToken ct) => svc.SaveRecordAsync(id, recordId, r, ct);
    [HttpDelete("{id:guid}/records/{recordId:guid}")] public async Task<IActionResult> DeleteRecord(Guid id, Guid recordId, CancellationToken ct) { await svc.DeleteRecordAsync(id, recordId, ct); return NoContent(); }
    [HttpPost("{id:guid}/files"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> UploadFile(Guid id, IFormFile file, [FromQuery] bool image = false, CancellationToken ct = default) => await svc.UploadFileAsync(id, await file.ToUploadAsync(), image, ct);
    [HttpPost("{id:guid}/records/{recordId:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Attach(Guid id, Guid recordId, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, recordId, await file.ToUploadAsync(), ct);
    [HttpDelete("{id:guid}/records/{recordId:guid}/attachments/{fileId:guid}")]
    public async Task<IActionResult> Detach(Guid id, Guid recordId, Guid fileId, CancellationToken ct) { await svc.RemoveAttachmentAsync(id, recordId, fileId, ct); return NoContent(); }
    [HttpGet("{id:guid}/export")] public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportAsync(id, format, ct));
    [HttpPost("{id:guid}/import"), RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<ImportResult> Import(Guid id, IFormFile file, CancellationToken ct) { await using var s = file.OpenReadStream(); return await svc.ImportAsync(id, s, file.FileName, ct); }
}

[Route("api/product-registers")]
public class ProductsController(ProductService svc) : AppControllerBase
{
    [HttpGet] public Task<IReadOnlyList<RegisterDto>> Registers(CancellationToken ct) => svc.GetRegistersAsync(ct);
    [HttpPost] public Task<RegisterDto> CreateRegister(RegisterRequest r, CancellationToken ct) => svc.SaveRegisterAsync(null, r, ct);
    [HttpPut("{id:guid}")] public Task<RegisterDto> UpdateRegister(Guid id, RegisterRequest r, CancellationToken ct) => svc.SaveRegisterAsync(id, r, ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> DeleteRegister(Guid id, CancellationToken ct) { await svc.DeleteRegisterAsync(id, ct); return NoContent(); }
    [HttpGet("{id:guid}/products")] public Task<PagedResult<ProductDto>> Products(Guid id, [FromQuery] ProductQuery q, CancellationToken ct) => svc.GetProductsAsync(id, q, ct);
    [HttpGet("{id:guid}/products/{pid:guid}")] public Task<ProductDto> Product(Guid id, Guid pid, CancellationToken ct) => svc.GetProductAsync(id, pid, ct);
    [HttpPost("{id:guid}/products")] public Task<ProductDto> Create(Guid id, ProductRequest r, CancellationToken ct) => svc.SaveProductAsync(id, null, r, ct);
    [HttpPut("{id:guid}/products/{pid:guid}")] public Task<ProductDto> Update(Guid id, Guid pid, ProductRequest r, CancellationToken ct) => svc.SaveProductAsync(id, pid, r, ct);
    [HttpDelete("{id:guid}/products/{pid:guid}")] public async Task<IActionResult> Delete(Guid id, Guid pid, CancellationToken ct) { await svc.DeleteProductAsync(id, pid, ct); return NoContent(); }
    [HttpPost("images"), RequestSizeLimit(20L * 1024 * 1024)] public async Task<FileDto> Image(IFormFile file, CancellationToken ct) => await svc.UploadImageAsync(await file.ToUploadAsync(), ct);
    [HttpPost("{id:guid}/products/{pid:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Attach(Guid id, Guid pid, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, pid, await file.ToUploadAsync(), ct);
    [HttpDelete("{id:guid}/products/{pid:guid}/attachments/{fileId:guid}")]
    public async Task<IActionResult> Detach(Guid id, Guid pid, Guid fileId, CancellationToken ct) { await svc.RemoveAttachmentAsync(id, pid, fileId, ct); return NoContent(); }
    [HttpGet("{id:guid}/export")] public async Task<IActionResult> Export(Guid id, [FromQuery] ProductQuery q, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportAsync(id, q, format, ct));
    [HttpPost("{id:guid}/import"), RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<ImportResult> Import(Guid id, IFormFile file, CancellationToken ct) { await using var s = file.OpenReadStream(); return await svc.ImportAsync(id, s, file.FileName, ct); }
}

[Route("api/contacts")]
public class ContactsController(ContactService svc) : AppControllerBase
{
    [HttpGet("groups")] public Task<IReadOnlyList<GroupDto>> Groups(CancellationToken ct) => svc.GetGroupsAsync(ct);
    [HttpPost("groups")] public Task<GroupDto> CreateGroup(GroupRequest r, CancellationToken ct) => svc.SaveGroupAsync(null, r, ct);
    [HttpPut("groups/{id:guid}")] public Task<GroupDto> UpdateGroup(Guid id, GroupRequest r, CancellationToken ct) => svc.SaveGroupAsync(id, r, ct);
    [HttpDelete("groups/{id:guid}")] public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken ct) { await svc.DeleteGroupAsync(id, ct); return NoContent(); }
    [HttpGet] public Task<PagedResult<ContactDto>> List([FromQuery] ContactQuery q, CancellationToken ct) => svc.GetContactsAsync(q, ct);
    [HttpGet("{id:guid}")] public Task<ContactDto> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpGet("{id:guid}/summary")] public Task<ContactSummaryDto> Summary(Guid id, CancellationToken ct) => svc.GetSummaryAsync(id, ct);
    [HttpPost] public Task<ContactDto> Create(ContactRequest r, CancellationToken ct) => svc.SaveAsync(null, r, ct);
    [HttpPut("{id:guid}")] public Task<ContactDto> Update(Guid id, ContactRequest r, CancellationToken ct) => svc.SaveAsync(id, r, ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/avatar"), RequestSizeLimit(10L * 1024 * 1024)]
    public async Task<IActionResult> Avatar(Guid id, IFormFile file, CancellationToken ct) => Ok(new { fileId = await svc.SetAvatarAsync(id, await file.ToUploadAsync(), ct) });
    [HttpPost("{id:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Attach(Guid id, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, await file.ToUploadAsync(), ct);
    [HttpDelete("{id:guid}/attachments/{fileId:guid}")] public async Task<IActionResult> Detach(Guid id, Guid fileId, CancellationToken ct) { await svc.RemoveAttachmentAsync(id, fileId, ct); return NoContent(); }
    [HttpGet("export")] public async Task<IActionResult> Export([FromQuery] ContactQuery q, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportAsync(q, format, ct));
}

[Route("api/udhar")]
public class UdharController(UdharService svc) : AppControllerBase
{
    [HttpGet] public Task<PagedResult<CreditDto>> List([FromQuery] CreditQuery q, CancellationToken ct) => svc.GetAsync(q, ct);
    [HttpGet("summary")] public Task<CreditSummaryDto> Summary(CancellationToken ct) => svc.GetSummaryAsync(ct);
    [HttpGet("{id:guid}")] public Task<CreditDto> Get(Guid id, CancellationToken ct) => svc.GetOneAsync(id, ct);
    [HttpPost] public Task<CreditDto> Create(CreditRequest r, CancellationToken ct) => svc.SaveAsync(null, r, ct);
    [HttpPut("{id:guid}")] public Task<CreditDto> Update(Guid id, CreditRequest r, CancellationToken ct) => svc.SaveAsync(id, r, ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/payments")] public Task<CreditDto> Pay(Guid id, PaymentRequest r, CancellationToken ct) => svc.AddPaymentAsync(id, r, ct);
    [HttpDelete("{id:guid}/payments/{paymentId:guid}")] public Task<CreditDto> Unpay(Guid id, Guid paymentId, CancellationToken ct) => svc.DeletePaymentAsync(id, paymentId, ct);
    [HttpPost("{id:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Attach(Guid id, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, await file.ToUploadAsync(), ct);
    [HttpGet("statement")] public async Task<IActionResult> Statement([FromQuery] Guid? contactId, [FromQuery] string? person, [FromQuery] string format = "pdf", CancellationToken ct = default) => Download(await svc.StatementAsync(contactId, person, format, ct));
    [HttpGet("export")] public async Task<IActionResult> Export([FromQuery] CreditQuery q, [FromQuery] string format = "xlsx", CancellationToken ct = default) => Download(await svc.ExportAsync(q, format, ct));
}

[Route("api/diary")]
public class DiaryController(DiaryService svc) : AppControllerBase
{
    [HttpGet] public Task<PagedResult<DiaryListItem>> List([FromQuery] DiaryQuery q, CancellationToken ct) => svc.GetEntriesAsync(q, ct);
    [HttpGet("archive")] public Task<IReadOnlyList<ArchiveBucket>> Archive(CancellationToken ct) => svc.GetArchiveAsync(ct);
    [HttpGet("dates/{year:int}/{month:int}")] public Task<IReadOnlyList<DateOnly>> Dates(int year, int month, CancellationToken ct) => svc.GetEntryDatesAsync(year, month, ct);
    [HttpGet("tags")] public Task<IReadOnlyList<string>> Tags(CancellationToken ct) => svc.GetTagsAsync(ct);
    [HttpGet("{id:guid}")] public Task<DiaryEntryDto> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost] public Task<DiaryEntryDto> Create(DiaryRequest r, CancellationToken ct) => svc.SaveAsync(null, r, ct);
    [HttpPut("{id:guid}")] public Task<DiaryEntryDto> Update(Guid id, DiaryRequest r, CancellationToken ct) => svc.SaveAsync(id, r, ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<DiaryAttachmentDto> Attach(Guid id, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, await file.ToUploadAsync(), ct);
    [HttpDelete("{id:guid}/attachments/{fileId:guid}")] public async Task<IActionResult> Detach(Guid id, Guid fileId, CancellationToken ct) { await svc.RemoveAttachmentAsync(id, fileId, ct); return NoContent(); }
}

[Route("api/calendar")]
public class CalendarController(CalendarService svc) : AppControllerBase
{
    [HttpGet("events")] public Task<IReadOnlyList<EventOccurrence>> Range([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct) => svc.GetRangeAsync(from.ToUniversalTime(), to.ToUniversalTime(), ct);
    [HttpGet("events/{id:guid}")] public Task<EventDto> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost("events")] public Task<EventDto> Create(EventRequest r, CancellationToken ct) => svc.SaveAsync(null, r, ct);
    [HttpPut("events/{id:guid}")] public Task<EventDto> Update(Guid id, EventRequest r, CancellationToken ct) => svc.SaveAsync(id, r, ct);
    [HttpPost("events/{id:guid}/complete")] public async Task<IActionResult> Complete(Guid id, [FromQuery] bool value = true, CancellationToken ct = default) { await svc.SetCompletedAsync(id, value, ct); return NoContent(); }
    [HttpDelete("events/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpPost("events/{id:guid}/attachments"), RequestSizeLimit(110L * 1024 * 1024)]
    public async Task<FileDto> Attach(Guid id, IFormFile file, CancellationToken ct) => await svc.AddAttachmentAsync(id, await file.ToUploadAsync(), ct);
    [HttpGet("upcoming")] public Task<IReadOnlyList<AgendaItem>> Upcoming([FromQuery] int days = 14, CancellationToken ct = default) => svc.GetUpcomingAsync(days, ct);

    [HttpGet("reminders")] public Task<IReadOnlyList<ReminderDto>> Reminders([FromQuery] bool includeCompleted = false, CancellationToken ct = default) => svc.GetRemindersAsync(includeCompleted, ct);
    [HttpPost("reminders")] public Task<ReminderDto> CreateReminder(ReminderRequest r, CancellationToken ct) => svc.SaveReminderAsync(null, r, ct);
    [HttpPut("reminders/{id:guid}")] public Task<ReminderDto> UpdateReminder(Guid id, ReminderRequest r, CancellationToken ct) => svc.SaveReminderAsync(id, r, ct);
    [HttpPost("reminders/{id:guid}/complete")] public async Task<IActionResult> CompleteReminder(Guid id, [FromQuery] bool value = true, CancellationToken ct = default) { await svc.CompleteReminderAsync(id, value, ct); return NoContent(); }
    [HttpDelete("reminders/{id:guid}")] public async Task<IActionResult> DeleteReminder(Guid id, CancellationToken ct) { await svc.DeleteReminderAsync(id, ct); return NoContent(); }
}
