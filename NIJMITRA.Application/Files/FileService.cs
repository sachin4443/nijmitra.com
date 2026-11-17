using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Application.Files;

public record UploadInput(Stream Content, string FileName, long Length);
public record FileDto(Guid Id, string Name, string ContentType, string Extension, long SizeBytes, DateTime CreatedAt);
public record FileStreamResultDto(Stream Content, string FileName, string ContentType, long Size);

/// <summary>
/// Secure upload pipeline: extension allow-list → size & quota limits → magic-byte signature check
/// (content type is derived server-side, never trusted from the client) → malware scan → private storage.
/// </summary>
public sealed class FileService(IAppDbContext db, IFileStorage storage, IMalwareScanner scanner, ISettingsService settings,
    ICurrentUser current, IAuditService audit, TimeProvider clock)
{
    public static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv", [".txt"] = "text/plain", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
        [".webm"] = "video/webm", [".mp4"] = "video/mp4", [".mov"] = "video/quicktime", [".ico"] = "image/x-icon", [".svg"] = "image/svg+xml"
    };

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<StoredFile> UploadAsync(UploadInput input, string purpose, CancellationToken ct, string[]? restrictTo = null, Guid? ownerOverride = null)
    {
        var s = await settings.GetAsync(ct);
        var uid = ownerOverride ?? current.RequireUserId();
        var name = SanitizeName(input.FileName);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var allowed = restrictTo ?? s.AllowedExtensions.ToArray();
        if (string.IsNullOrEmpty(ext) || !allowed.Contains(ext) || !MimeTypes.ContainsKey(ext))
            throw new ValidationAppException("file", $"File type '{ext}' is not allowed. Allowed: {string.Join(", ", allowed)}");
        var max = (long)Math.Max(1, s.MaxUploadMb) * 1024 * 1024;
        if (input.Length <= 0) throw new ValidationAppException("file", "The file is empty.");
        if (input.Length > max) throw new ValidationAppException("file", $"File exceeds the {s.MaxUploadMb} MB limit.");
        var used = await db.StoredFiles.IgnoreQueryFilters().Where(f => f.UserId == uid && !f.IsDeleted).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0;
        if (used + input.Length > (long)s.UserStorageQuotaMb * 1024 * 1024)
            throw new ValidationAppException("file", "Your storage quota is full. Delete some files to upload more.");

        // Buffer to a temp file so the content can be inspected, scanned and then stored.
        var temp = Path.GetTempFileName();
        try
        {
            await using (var fs = File.Create(temp)) await CopyLimitedAsync(input.Content, fs, max, ct);
            await using (var fs = File.OpenRead(temp))
            {
                var header = new byte[16384];
                var n = await fs.ReadAsync(header, ct);
                if (!SignatureMatches(ext, header.AsSpan(0, n)))
                    throw new ValidationAppException("file", "The file content does not match its extension.");
            }
            await using (var fs = File.OpenRead(temp))
            {
                var scan = await scanner.ScanAsync(fs, ct);
                if (!scan.Clean)
                {
                    await audit.LogAsync(new AuditEntry("file_upload_blocked", Details: "malware detected", Success: false, UserId: uid), ct);
                    throw new ValidationAppException("file", "The file was blocked by the malware scanner.");
                }
            }
            StoredBlob blob;
            await using (var fs = File.OpenRead(temp)) blob = await storage.SaveAsync(fs, ct);
            var file = new StoredFile
            {
                UserId = uid, OriginalName = name, Extension = ext, ContentType = MimeTypes[ext], SizeBytes = blob.Size,
                StorageKey = blob.StorageKey, Sha256 = blob.Sha256, Purpose = purpose, CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(new AuditEntry("file_upload", "StoredFile", file.Id.ToString(), $"{purpose}; {ext}; {blob.Size} bytes", UserId: uid), ct);
            return file;
        }
        finally { try { File.Delete(temp); } catch { /* best effort */ } }
    }

    /// <summary>Opens a file that belongs to the current user (Owner query filter enforces this).</summary>
    public async Task<FileStreamResultDto> OpenOwnAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct) ?? throw new NotFoundException("File");
        return await OpenAsync(file, ct);
    }

    /// <summary>Opens a file after the caller has performed its own authorisation (chat participants, share links).</summary>
    public async Task<FileStreamResultDto> OpenAuthorizedAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.StoredFiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, ct) ?? throw new NotFoundException("File");
        return await OpenAsync(file, ct);
    }

    private async Task<FileStreamResultDto> OpenAsync(StoredFile file, CancellationToken ct)
    {
        var stream = await storage.OpenReadAsync(file.StorageKey, ct);
        await audit.LogAsync(new AuditEntry("file_download", "StoredFile", file.Id.ToString(), file.Purpose), ct);
        return new FileStreamResultDto(stream, file.OriginalName, file.ContentType, file.SizeBytes);
    }

    public async Task DeleteOwnAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file == null) return;
        db.StoredFiles.Remove(file); // soft delete; physical removal happens in the purge job
        await db.SaveChangesAsync(ct);
    }

    public static FileDto ToDto(StoredFile f) => new(f.Id, f.OriginalName, f.ContentType, f.Extension, f.SizeBytes, f.CreatedAt);

    public static string SanitizeName(string name)
    {
        name = Path.GetFileName(name ?? "file").Trim();
        var cleaned = new string(name.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c) && c is not '<' and not '>' and not '"').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "file";
        return cleaned.Length > 180 ? cleaned[..150] + Path.GetExtension(cleaned) : cleaned;
    }

    private static async Task CopyLimitedAsync(Stream src, Stream dst, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max) throw new ValidationAppException("file", "File is too large.");
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    public static bool SignatureMatches(string ext, ReadOnlySpan<byte> h)
    {
        static bool StartsWith(ReadOnlySpan<byte> d, params byte[] sig) => d.Length >= sig.Length && d[..sig.Length].SequenceEqual(sig);
        static bool AtPos(ReadOnlySpan<byte> d, int offset, string ascii) => d.Length >= offset + ascii.Length && d.Slice(offset, ascii.Length).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(ascii));
        switch (ext)
        {
            case ".pdf": return AtPos(h, 0, "%PDF");
            case ".png": return StartsWith(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
            case ".jpg": case ".jpeg": return StartsWith(h, 0xFF, 0xD8, 0xFF);
            case ".webp": return AtPos(h, 0, "RIFF") && AtPos(h, 8, "WEBP");
            case ".docx": case ".xlsx": return StartsWith(h, 0x50, 0x4B, 0x03, 0x04);
            case ".doc": case ".xls": return StartsWith(h, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);
            case ".mp3": return AtPos(h, 0, "ID3") || (h.Length > 1 && h[0] == 0xFF && (h[1] & 0xE0) == 0xE0);
            case ".m4a": case ".mp4": case ".mov": return AtPos(h, 4, "ftyp") || AtPos(h, 4, "moov") || AtPos(h, 4, "wide") || AtPos(h, 4, "mdat");
            case ".ogg": return AtPos(h, 0, "OggS");
            case ".wav": return AtPos(h, 0, "RIFF") && AtPos(h, 8, "WAVE");
            case ".webm": return StartsWith(h, 0x1A, 0x45, 0xDF, 0xA3);
            case ".ico": return StartsWith(h, 0x00, 0x00, 0x01, 0x00);
            case ".txt": case ".csv": return !h.Contains((byte)0);
            default: return false;
        }
    }
}
