using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NIJMITRA.Infrastructure.Persistence;
using Xunit;

namespace NIJMITRA.Tests;

/// <summary>
/// Boots the real API against a throw-away PostgreSQL database. Connection (without database name) comes from
/// NIJMITRA_TEST_DB, e.g. "Host=localhost;Port=5431;Username=postgres;Password=..." — never committed.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbName = "nijmitra_test_" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "nm-test-" + Guid.NewGuid().ToString("N"));
    public static string ServerConnection => Environment.GetEnvironmentVariable("NIJMITRA_TEST_DB") ?? "Host=localhost;Port=5431;Username=postgres";
    public string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _dbName }.ConnectionString;
    public const string AdminMobile = "+919000000001";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["Storage:RootPath"] = _storage,
            ["BackgroundJobs:Enabled"] = "false",
            ["App:BootstrapSuperAdminMobiles:0"] = AdminMobile,
            ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)),
            ["Security:DataEncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        }));
    }

    public Task InitializeAsync() { _ = Server; return Task.CompletedTask; }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(ServerConnection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
        try { Directory.Delete(_storage, true); } catch { }
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> work) => WithDbAsync(async db => { await work(db); return 0; });

    private static int _seq = 100;
    public static string NewMobile() => "+9198" + Interlocked.Increment(ref _seq).ToString("D8");

    public async Task<UserClient> RegisterAsync(string? mobile = null, string name = "Test User")
    {
        mobile ??= NewMobile();
        var anon = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var reg = await anon.PostAsJsonAsync("/api/auth/register", new { fullName = name, mobile, password = "Str0ng@Pass", confirmPassword = "Str0ng@Pass", pin = "4826", acceptTerms = true });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        return await LoginAsync(mobile);
    }

    public async Task<UserClient> LoginAsync(string mobile, string password = "Str0ng@Pass")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new { mobile, password, deviceKey = "test-device" });
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(res.IsSuccessStatusCode, json.ToString());
        Assert.Equal("success", json.GetProperty("status").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        client.DefaultRequestHeaders.Add("X-Requested-With", "NIJMITRA");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        return new UserClient(client, me.GetProperty("id").GetGuid(), mobile, me.GetProperty("publicId").GetString()!);
    }
}

public record UserClient(HttpClient Http, Guid UserId, string Mobile, string PublicId)
{
    public async Task<JsonElement> GetJson(string url)
    {
        var r = await Http.GetAsync(url);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"GET {url} → {(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    public async Task<JsonElement> PostJson(string url, object payload)
    {
        var r = await Http.PostAsJsonAsync(url, payload);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"POST {url} → {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    public async Task<JsonElement> PutJson(string url, object payload)
    {
        var r = await Http.PutAsJsonAsync(url, payload);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"PUT {url} → {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    public async Task<JsonElement> Upload(string url, byte[] content, string fileName, Dictionary<string, string>? fields = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", fileName);
        foreach (var f in fields ?? []) form.Add(new StringContent(f.Value), f.Key);
        var r = await Http.PostAsync(url, form);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"UPLOAD {url} → {(int)r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<TestApp>;

public static class Samples
{
    public static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
