using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;

namespace NIJMITRA.Infrastructure.Services;

/// <summary>
/// Converts .xlsx / .csv into rows and .docx into plain text blocks (headings, paragraphs, list items, tables).
/// Output is plain text only — no HTML from the file ever reaches the browser. Large files are truncated.
/// </summary>
public sealed class OfficePreviewService : IOfficePreviewService
{
    private const long MaxBytes = 20 * 1024 * 1024;
    private const int MaxRows = 500, MaxCols = 40, MaxSheets = 10, MaxBlocks = 3000, MaxCell = 500;
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static string Kind(string contentType, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (contentType == Xlsx || ext == ".xlsx") return "xlsx";
        if (contentType == Docx || ext == ".docx") return "docx";
        if (contentType is "text/csv" or "application/csv" || ext == ".csv") return "csv";
        return "";
    }

    public bool CanPreview(string contentType, string fileName) => Kind(contentType, fileName) != "";

    public async Task<OfficePreview> PreviewAsync(Stream content, string contentType, string fileName, CancellationToken ct)
    {
        var kind = Kind(contentType, fileName);
        if (kind == "") throw new ValidationAppException("This file type cannot be previewed.");
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxBytes) throw new ValidationAppException("This file is too large to preview. Please download it.");
            ms.Write(buffer, 0, read);
        }
        ms.Position = 0;
        try
        {
            return kind switch { "xlsx" => Excel(ms), "docx" => Word(ms), _ => Csv(ms) };
        }
        catch (ValidationAppException) { throw; }
        catch (Exception)
        {
            throw new ValidationAppException("This file could not be read for preview. Please download it to open.");
        }
    }

    private static string Cell(string? v) => v == null ? "" : v.Length > MaxCell ? v[..MaxCell] + "…" : v;

    private static OfficePreview Excel(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var sheets = new List<PreviewSheet>();
        foreach (var ws in wb.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).Take(MaxSheets))
        {
            var used = ws.RangeUsed();
            var rows = new List<IReadOnlyList<string>>();
            var truncated = false;
            if (used != null)
            {
                var firstCol = used.FirstColumn().ColumnNumber();
                var lastCol = Math.Min(used.LastColumn().ColumnNumber(), firstCol + MaxCols - 1);
                truncated = used.LastColumn().ColumnNumber() > lastCol || used.RowCount() > MaxRows;
                foreach (var row in used.Rows().Take(MaxRows))
                {
                    var r = new List<string>(lastCol - firstCol + 1);
                    for (var c = firstCol; c <= lastCol; c++)
                    {
                        var cell = ws.Cell(row.RowNumber(), c);
                        string v;
                        try { v = cell.GetFormattedString(); } catch { v = cell.Value.ToString(); }
                        r.Add(Cell(v));
                    }
                    rows.Add(r);
                }
            }
            sheets.Add(new PreviewSheet(ws.Name, rows, truncated));
        }
        return new OfficePreview("table", Sheets: sheets, Truncated: wb.Worksheets.Count > MaxSheets);
    }

    private static OfficePreview Csv(Stream s)
    {
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var delimiter = DetectDelimiter(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var truncated = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            if (ch == '"' && field.Length == 0) { quoted = true; continue; }
            if (ch == delimiter) { if (row.Count < MaxCols) row.Add(Cell(field.ToString())); field.Clear(); continue; }
            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (row.Count < MaxCols) row.Add(Cell(field.ToString()));
                field.Clear(); rows.Add(row); row = [];
                if (rows.Count >= MaxRows) { truncated = i < text.Length - 1; break; }
                continue;
            }
            field.Append(ch);
        }
        if (rows.Count < MaxRows && (field.Length > 0 || row.Count > 0)) { row.Add(Cell(field.ToString())); rows.Add(row); }
        return new OfficePreview("table", Sheets: [new PreviewSheet("CSV", rows, truncated)], Truncated: truncated);
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t', '|' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
    }

    private static OfficePreview Word(Stream s)
    {
        using var doc = WordprocessingDocument.Open(s, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var blocks = new List<PreviewBlock>();
        if (body == null) return new OfficePreview("document", Blocks: blocks);
        foreach (var el in body.Elements())
        {
            if (blocks.Count >= MaxBlocks) return new OfficePreview("document", Blocks: blocks, Truncated: true);
            switch (el)
            {
                case Paragraph p:
                {
                    var text = ParagraphText(p);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                    var type = style.StartsWith("Title", StringComparison.OrdinalIgnoreCase) || style.Equals("Heading1", StringComparison.OrdinalIgnoreCase) ? "h1"
                        : style.Equals("Heading2", StringComparison.OrdinalIgnoreCase) || style.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) ? "h2"
                        : style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ? "h3"
                        : p.ParagraphProperties?.NumberingProperties != null || style.StartsWith("List", StringComparison.OrdinalIgnoreCase) ? "li" : "p";
                    blocks.Add(new PreviewBlock(type, Cell(text.Length > 4000 ? text[..4000] : text)));
                    break;
                }
                case Table t:
                {
                    var rows = t.Elements<TableRow>().Take(MaxRows)
                        .Select(r => (IReadOnlyList<string>)r.Elements<TableCell>().Take(MaxCols).Select(c => Cell(string.Join(" ", c.Elements<Paragraph>().Select(ParagraphText)).Trim())).ToList())
                        .ToList();
                    if (rows.Count > 0) blocks.Add(new PreviewBlock("table", Rows: rows));
                    break;
                }
            }
        }
        return new OfficePreview("document", Blocks: blocks);
    }

    private static string ParagraphText(Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Descendants())
        {
            switch (node)
            {
                case Text t: sb.Append(t.Text); break;
                case TabChar: sb.Append('\t'); break;
                case Break: sb.Append('\n'); break;
            }
        }
        return sb.ToString().Trim();
    }
}
