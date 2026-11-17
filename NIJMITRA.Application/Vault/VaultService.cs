using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Application.Vault;

public record VaultHeaderDto(string KdfAlgorithm, int KdfIterations, string KdfSalt, string WrappedKey, string RecoverySalt, string RecoveryWrappedKey, int FormatVersion, DateTime UpdatedAt);
public record VaultSetupRequest([Range(100_000, 5_000_000)] int KdfIterations, [Required] string KdfSalt, [Required] string WrappedKey,
    [Required] string RecoverySalt, [Required] string RecoveryWrappedKey);
public record VaultRekeyRequest([Range(100_000, 5_000_000)] int KdfIterations, [Required] string KdfSalt, [Required] string WrappedKey, string? RecoverySalt, string? RecoveryWrappedKey);
public record EntryDto(Guid Id, string Title, string? WebsiteUrl, string? ApplicationName, string Category, string? Tags, bool IsFavorite, string EncryptedData, int Revision, DateTime CreatedAt, DateTime UpdatedAt);
public record EntryRequest([Required, StringLength(150, MinimumLength = 1)] string Title, [StringLength(500)] string? WebsiteUrl, [StringLength(150)] string? ApplicationName,
    [Required, StringLength(40)] string Category, [StringLength(300)] string? Tags, bool IsFavorite, [Required] string EncryptedData, string? PreviousEncryptedPassword);
public record HistoryDto(Guid Id, string EncryptedPassword, DateTime ChangedAt);
public record NoteDto(Guid Id, string EncryptedData, bool IsFavorite, DateTime CreatedAt, DateTime UpdatedAt);
public record NoteRequest([Required] string EncryptedData, bool IsFavorite);
public record VaultBackup(int FormatVersion, DateTime ExportedAt, VaultHeaderDto Header, List<EntryDto> Entries, List<NoteDto> Notes);
public record VaultImportRequest(List<EntryRequest> Entries, List<NoteRequest> Notes);

/// <summary>
/// Zero-knowledge password vault. All secrets are encrypted in the browser (AES-256-GCM, key derived from the master
/// password with PBKDF2-SHA256). This service only stores and returns opaque ciphertext; it can never decrypt it.
/// Only non-secret metadata (title, website, application, category, tags) is stored in plaintext so it can be listed and searched.
/// </summary>
public sealed partial class VaultService(IAppDbContext db, ICurrentUser current, AuthService auth, IAuditService audit, TimeProvider clock)
{
    public static readonly string[] DefaultCategories = ["Social Media", "Email", "Banking", "Shopping", "Government", "Work", "Education", "Hosting", "Other"];
    private const int MaxCipherLength = 64 * 1024;

    [GeneratedRegex(@"^[A-Za-z0-9+/=_\-.:]+$")]
    private static partial Regex CipherPattern();

    private static void ValidateCipher(string? value, string field, bool required = true)
    {
        if (string.IsNullOrEmpty(value)) { if (required) throw new ValidationAppException(field, "Encrypted payload is required."); return; }
        if (value.Length > MaxCipherLength || !CipherPattern().IsMatch(value)) throw new ValidationAppException(field, "Encrypted payload is malformed.");
    }

    public async Task<VaultHeaderDto?> GetHeaderAsync(CancellationToken ct)
    {
        var v = await db.PasswordVaults.AsNoTracking().FirstOrDefaultAsync(ct);
        return v == null ? null : new VaultHeaderDto(v.KdfAlgorithm, v.KdfIterations, v.KdfSalt, v.WrappedKey, v.RecoverySalt, v.RecoveryWrappedKey, v.FormatVersion, v.UpdatedAt);
    }

    public async Task SetupAsync(VaultSetupRequest r, CancellationToken ct)
    {
        if (await db.PasswordVaults.AnyAsync(ct)) throw new ConflictException("Your vault is already set up.");
        foreach (var (val, name) in new[] { (r.KdfSalt, "kdfSalt"), (r.WrappedKey, "wrappedKey"), (r.RecoverySalt, "recoverySalt"), (r.RecoveryWrappedKey, "recoveryWrappedKey") })
            ValidateCipher(val, name);
        db.PasswordVaults.Add(new PasswordVault { KdfIterations = r.KdfIterations, KdfSalt = r.KdfSalt, WrappedKey = r.WrappedKey, RecoverySalt = r.RecoverySalt, RecoveryWrappedKey = r.RecoveryWrappedKey });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("vault_created"), ct);
    }

    /// <summary>Changes the master password (re-wrapped key) — the vault key itself and all entries stay unchanged.</summary>
    public async Task RekeyAsync(VaultRekeyRequest r, CancellationToken ct)
    {
        await auth.EnsureRecentlyReauthenticatedAsync(ct);
        var v = await db.PasswordVaults.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Vault");
        ValidateCipher(r.KdfSalt, "kdfSalt"); ValidateCipher(r.WrappedKey, "wrappedKey");
        v.KdfIterations = r.KdfIterations; v.KdfSalt = r.KdfSalt; v.WrappedKey = r.WrappedKey;
        if (r.RecoveryWrappedKey != null)
        {
            ValidateCipher(r.RecoverySalt, "recoverySalt"); ValidateCipher(r.RecoveryWrappedKey, "recoveryWrappedKey");
            v.RecoverySalt = r.RecoverySalt!; v.RecoveryWrappedKey = r.RecoveryWrappedKey;
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("vault_master_password_changed"), ct);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct)
    {
        var used = await db.PasswordEntries.AsNoTracking().Select(e => e.Category).Distinct().ToListAsync(ct);
        return DefaultCategories.Union(used).ToList();
    }

    private static EntryDto Map(PasswordEntry e) => new(e.Id, e.Title, e.WebsiteUrl, e.ApplicationName, e.Category, e.Tags, e.IsFavorite, e.EncryptedData, e.Revision, e.CreatedAt, e.UpdatedAt);

    public async Task<IReadOnlyList<EntryDto>> GetEntriesAsync(string? category, bool? favorites, string? search, CancellationToken ct)
    {
        await EnsureVaultAsync(ct);
        var q = db.PasswordEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);
        if (favorites == true) q = q.Where(e => e.IsFavorite);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = TextUtil.LikePattern(search);
            q = q.Where(e => EF.Functions.ILike(e.Title, like) || (e.WebsiteUrl != null && EF.Functions.ILike(e.WebsiteUrl, like))
                             || (e.ApplicationName != null && EF.Functions.ILike(e.ApplicationName, like)) || (e.Tags != null && EF.Functions.ILike(e.Tags, like)));
        }
        return (await q.OrderByDescending(e => e.IsFavorite).ThenBy(e => e.Title).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<EntryDto> GetEntryAsync(Guid id, CancellationToken ct) =>
        Map(await db.PasswordEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Credential"));

    public async Task<EntryDto> SaveEntryAsync(Guid? id, EntryRequest r, CancellationToken ct)
    {
        await EnsureVaultAsync(ct);
        ValidateCipher(r.EncryptedData, "encryptedData");
        ValidateCipher(r.PreviousEncryptedPassword, "previousEncryptedPassword", false);
        if (r.WebsiteUrl is { Length: > 0 } url && !Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out _))
            throw new ValidationAppException("websiteUrl", "Enter a valid website URL.");
        PasswordEntry e;
        if (id is { } existing)
        {
            e = await db.PasswordEntries.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Credential");
            e.Revision++;
            if (!string.IsNullOrEmpty(r.PreviousEncryptedPassword))
            {
                db.PasswordHistory.Add(new PasswordHistory { PasswordEntryId = e.Id, EncryptedPassword = r.PreviousEncryptedPassword, ChangedAt = clock.GetUtcNow().UtcDateTime });
                var old = await db.PasswordHistory.Where(h => h.PasswordEntryId == e.Id).OrderByDescending(h => h.ChangedAt).Skip(19).ToListAsync(ct);
                db.PasswordHistory.RemoveRange(old);
            }
        }
        else { e = new PasswordEntry(); db.PasswordEntries.Add(e); }
        e.Title = r.Title.Trim(); e.WebsiteUrl = TextUtil.Clean(r.WebsiteUrl, 500); e.ApplicationName = TextUtil.Clean(r.ApplicationName, 150);
        e.Category = r.Category.Trim(); e.Tags = TextUtil.Clean(r.Tags, 300); e.IsFavorite = r.IsFavorite; e.EncryptedData = r.EncryptedData;
        await db.SaveChangesAsync(ct);
        // Audit records the action only — never the content.
        await audit.LogAsync(new AuditEntry(id == null ? "vault_entry_created" : "vault_entry_updated", "PasswordEntry", e.Id.ToString()), ct);
        return Map(e);
    }

    public async Task ToggleFavoriteAsync(Guid id, bool favorite, CancellationToken ct)
    {
        var e = await db.PasswordEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Credential");
        e.IsFavorite = favorite;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteEntryAsync(Guid id, CancellationToken ct)
    {
        var e = await db.PasswordEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Credential");
        db.PasswordEntries.Remove(e);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("vault_entry_deleted", "PasswordEntry", id.ToString()), ct);
    }

    public async Task<IReadOnlyList<HistoryDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.PasswordEntries.AnyAsync(e => e.Id == id, ct)) throw new NotFoundException("Credential");
        return await db.PasswordHistory.AsNoTracking().Where(h => h.PasswordEntryId == id).OrderByDescending(h => h.ChangedAt)
            .Select(h => new HistoryDto(h.Id, h.EncryptedPassword, h.ChangedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NoteDto>> GetNotesAsync(CancellationToken ct)
    {
        await EnsureVaultAsync(ct);
        return await db.SecureNotes.AsNoTracking().OrderByDescending(n => n.IsFavorite).ThenByDescending(n => n.UpdatedAt)
            .Select(n => new NoteDto(n.Id, n.EncryptedData, n.IsFavorite, n.CreatedAt, n.UpdatedAt)).ToListAsync(ct);
    }

    public async Task<NoteDto> SaveNoteAsync(Guid? id, NoteRequest r, CancellationToken ct)
    {
        await EnsureVaultAsync(ct);
        ValidateCipher(r.EncryptedData, "encryptedData");
        SecureNote n;
        if (id is { } existing) n = await db.SecureNotes.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Note");
        else { n = new SecureNote(); db.SecureNotes.Add(n); }
        n.EncryptedData = r.EncryptedData; n.IsFavorite = r.IsFavorite;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry(id == null ? "secure_note_created" : "secure_note_updated", "SecureNote", n.Id.ToString()), ct);
        return new NoteDto(n.Id, n.EncryptedData, n.IsFavorite, n.CreatedAt, n.UpdatedAt);
    }

    public async Task DeleteNoteAsync(Guid id, CancellationToken ct)
    {
        var n = await db.SecureNotes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Note");
        db.SecureNotes.Remove(n);
        await db.SaveChangesAsync(ct);
    }

    public async Task<VaultBackup> ExportAsync(CancellationToken ct)
    {
        await auth.EnsureRecentlyReauthenticatedAsync(ct);
        var header = await GetHeaderAsync(ct) ?? throw new NotFoundException("Vault");
        var entries = (await db.PasswordEntries.AsNoTracking().ToListAsync(ct)).Select(Map).ToList();
        var notes = await db.SecureNotes.AsNoTracking().Select(n => new NoteDto(n.Id, n.EncryptedData, n.IsFavorite, n.CreatedAt, n.UpdatedAt)).ToListAsync(ct);
        await audit.LogAsync(new AuditEntry("vault_exported", Details: $"{entries.Count} entries, {notes.Count} notes (encrypted)"), ct);
        return new VaultBackup(1, clock.GetUtcNow().UtcDateTime, header, entries, notes);
    }

    /// <summary>Imports entries already encrypted with this vault's key (the browser re-encrypts before upload).</summary>
    public async Task<int> ImportAsync(VaultImportRequest r, CancellationToken ct)
    {
        await EnsureVaultAsync(ct);
        if (r.Entries.Count + r.Notes.Count > 5000) throw new ValidationAppException("Too many items in one import (max 5000).");
        foreach (var e in r.Entries)
        {
            ValidateCipher(e.EncryptedData, "encryptedData");
            db.PasswordEntries.Add(new PasswordEntry
            {
                Title = (e.Title ?? "Imported").Trim()[..Math.Min(150, (e.Title ?? "Imported").Trim().Length)], WebsiteUrl = TextUtil.Clean(e.WebsiteUrl, 500),
                ApplicationName = TextUtil.Clean(e.ApplicationName, 150), Category = TextUtil.Clean(e.Category, 40) ?? "Other", Tags = TextUtil.Clean(e.Tags, 300),
                IsFavorite = e.IsFavorite, EncryptedData = e.EncryptedData
            });
        }
        foreach (var n in r.Notes) { ValidateCipher(n.EncryptedData, "encryptedData"); db.SecureNotes.Add(new SecureNote { EncryptedData = n.EncryptedData, IsFavorite = n.IsFavorite }); }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(new AuditEntry("vault_imported", Details: $"{r.Entries.Count} entries, {r.Notes.Count} notes"), ct);
        return r.Entries.Count + r.Notes.Count;
    }

    private async Task EnsureVaultAsync(CancellationToken ct)
    {
        if (!await db.PasswordVaults.AnyAsync(ct)) throw new ValidationAppException("Set up your vault master password first.");
    }
}
