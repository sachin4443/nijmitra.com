using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NIJMITRA.Application.Abstractions;

namespace NIJMITRA.Infrastructure.Storage;

public class StorageOptions
{
    /// <summary>Private directory outside the web root. Relative paths resolve against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/storage";
    /// <summary>"none" (default) or "clamav".</summary>
    public string MalwareScanner { get; set; } = "none";
    public string ClamAvHost { get; set; } = "127.0.0.1";
    public int ClamAvPort { get; set; } = 3310;
}

/// <summary>
/// Stores files under random, unguessable keys in a sharded private directory. Original file names
/// and physical paths are never exposed — clients only ever see StoredFile ids behind authorised endpoints.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        var path = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path));
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string key)
    {
        if (key.Length < 8 || key.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid storage key.");
        var full = Path.GetFullPath(Path.Combine(_root, key[..2], key[2..4], key));
        if (!full.StartsWith(_root, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return full;
    }

    public async Task<StoredBlob> SaveAsync(Stream content, CancellationToken ct = default)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                size += read;
            }
        }
        return new StoredBlob(key, size, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var path = PathFor(storageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("Stored file missing.");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = PathFor(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default) => Task.FromResult(File.Exists(PathFor(storageKey)));
}

/// <summary>Used when no scanner is configured; reports Scanned=false so callers can surface that honestly.</summary>
public sealed class NoOpMalwareScanner : IMalwareScanner
{
    public Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default) => Task.FromResult(new ScanResult(true, null, false));
}

/// <summary>ClamAV clamd INSTREAM integration (TCP).</summary>
public sealed class ClamAvMalwareScanner(IOptions<StorageOptions> options, ILogger<ClamAvMalwareScanner> logger) : IMalwareScanner
{
    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(options.Value.ClamAvHost, options.Value.ClamAvPort, ct);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), ct);
        var buffer = new byte[16384];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            var len = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(read));
            await stream.WriteAsync(len, ct);
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        await stream.WriteAsync(new byte[4], ct);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var reply = (await reader.ReadToEndAsync(ct)).Trim('\0', ' ', '\n');
        if (reply.EndsWith("OK")) return new ScanResult(true, null, true);
        logger.LogWarning("ClamAV flagged an upload: {Reply}", reply);
        return new ScanResult(false, reply.Replace("stream:", "").Replace("FOUND", "").Trim(), true);
    }
}
