using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NIJMITRA.Application.Abstractions;
using Npgsql;

namespace NIJMITRA.Infrastructure.Persistence;

public record DatabaseConnectionInfo(string Source, string? Host, int? Port, string? Database, string? Username, bool HasPassword, bool SslRequired, DateTime? UpdatedAt);
public record DatabaseTestResult(bool Success, string Message, string? ServerVersion);

/// <summary>
/// Resolves the PostgreSQL connection string. An administrator can replace it at runtime from the admin panel;
/// the new value is stored AES-256-GCM encrypted in App_Data/database.json (outside Git and the web root) and
/// takes effect for new requests immediately — no redeploy needed. Fallback: ConnectionStrings:DefaultConnection.
/// </summary>
public interface IDatabaseConnectionStore
{
    string ConnectionString { get; }
    DatabaseConnectionInfo Describe();
    Task<DatabaseTestResult> TestAsync(string connectionString, CancellationToken ct);
    void Save(string connectionString);
}

public sealed class DatabaseConnectionStore : IDatabaseConnectionStore
{
    private readonly string _file;
    private readonly IConfiguration _config;
    private readonly IFieldEncryptor _enc;
    private readonly object _lock = new();
    private string? _override;
    private DateTime? _updatedAt;

    public DatabaseConnectionStore(IConfiguration config, IHostEnvironment env, IFieldEncryptor enc)
    {
        _config = config; _enc = enc;
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "database.json");
        if (File.Exists(_file))
        {
            var doc = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_file));
            if (doc?.ConnectionString is { Length: > 0 } c) { _override = enc.Decrypt(c); _updatedAt = doc.UpdatedAt; }
        }
    }

    public string ConnectionString
    {
        get
        {
            lock (_lock)
            {
                return _override ?? _config.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("No database connection configured. Set ConnectionStrings:DefaultConnection.");
            }
        }
    }

    public DatabaseConnectionInfo Describe()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionString);
        return new DatabaseConnectionInfo(_override != null ? "admin-panel" : "configuration", b.Host, b.Port, b.Database, b.Username,
            !string.IsNullOrEmpty(b.Password), b.SslMode is SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull, _updatedAt);
    }

    public async Task<DatabaseTestResult> TestAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 8 };
            await using var conn = new NpgsqlConnection(b.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("select version()", conn);
            var version = (string?)await cmd.ExecuteScalarAsync(ct);
            return new DatabaseTestResult(true, "Connection successful.", version);
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or InvalidOperationException or System.Net.Sockets.SocketException or TimeoutException)
        {
            return new DatabaseTestResult(false, ex.Message.Replace(new NpgsqlConnectionStringBuilder { ConnectionString = SafeParse(connectionString) }.Password ?? "\u0000", "***"), null);
        }
    }

    private static string SafeParse(string cs) { try { return new NpgsqlConnectionStringBuilder(cs).ConnectionString; } catch { return ""; } }

    public void Save(string connectionString)
    {
        _ = new NpgsqlConnectionStringBuilder(connectionString); // validates syntax
        lock (_lock)
        {
            _updatedAt = DateTime.UtcNow;
            File.WriteAllText(_file, JsonSerializer.Serialize(new FileModel(_enc.Encrypt(connectionString), _updatedAt)));
            _override = connectionString;
        }
    }

    private sealed record FileModel(string ConnectionString, DateTime? UpdatedAt);
}
