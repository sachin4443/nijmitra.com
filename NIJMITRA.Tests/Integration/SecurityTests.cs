using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Notifications;
using OtpNet;
using Xunit;

namespace NIJMITRA.Tests.Integration;

[Collection("api")]
public class IsolationTests(TestApp app)
{
    private async Task<(UserClient A, UserClient B)> TwoUsers() => (await app.RegisterAsync(name: "User A"), await app.RegisterAsync(name: "User B"));

    [Fact] // Scenario 1
    public async Task UserACannotAccessUserBRecords()
    {
        var (a, b) = await TwoUsers();
        var cats = await b.GetJson("/api/finance/categories?type=Expense");
        var exp = await b.PostJson("/api/finance/expenses", new { name = "Electricity bill", amount = 1450.50, categoryId = cats[0].GetProperty("id").GetGuid(), paymentMethod = "Upi", date = "2026-11-10" });
        var id = exp.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/finance/expenses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.DeleteAsync($"/api/finance/expenses/{id}")).StatusCode);
        var aList = await a.GetJson("/api/finance/expenses");
        Assert.Equal(0, aList.GetProperty("page").GetProperty("totalCount").GetInt32());
        // A cannot attach B's category either
        var put = await a.Http.PostAsJsonAsync("/api/finance/expenses", new { name = "x", amount = 1, categoryId = cats[0].GetProperty("id").GetGuid(), paymentMethod = "Cash", date = "2026-11-10" });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        // Search for "Electricity" from A finds nothing
        var search = await a.GetJson("/api/search?q=Electricity");
        Assert.Equal(0, search.GetProperty("totalCount").GetInt32());
        var bSearch = await b.GetJson("/api/search?q=Electricity");
        Assert.True(bSearch.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact] // Scenarios 2 & 16
    public async Task UserACannotDownloadUserBDocuments()
    {
        var (a, b) = await TwoUsers();
        var doc = await b.Upload("/api/documents", Samples.Pdf, "aadhaar.pdf", new() { ["category"] = "Identity", ["name"] = "Aadhaar" });
        var id = doc.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await b.Http.GetAsync($"/api/documents/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/documents/{id}/download")).StatusCode);
        var fileId = await app.WithDbAsync(db => db.DocumentVersions.Where(v => v.DocumentId == id).Select(v => v.StoredFileId).FirstAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/files/{fileId}")).StatusCode);
        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/documents/{id}/download")).StatusCode);
        // Expiring share link works, then stops after revoke
        var share = await b.PostJson($"/api/documents/{id}/shares", new { expiresInHours = 1, maxDownloads = 2 });
        var url = new Uri(share.GetProperty("url").GetString()!).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(url)).StatusCode);
        await b.Http.DeleteAsync($"/api/documents/shares/{share.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/public/share/invalid-token")).StatusCode);
    }

    [Fact]
    public async Task RejectsDisguisedFiles()
    {
        var a = await app.RegisterAsync();
        using var form = new MultipartFormDataContent { { new ByteArrayContent([0x4D, 0x5A, 0x90, 0x00, 0x03]), "file", "invoice.pdf" }, { new StringContent("Receipts"), "category" } };
        var r = await a.Http.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var form2 = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "virus.exe" }, { new StringContent("Other"), "category" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.PostAsync("/api/documents", form2)).StatusCode);
    }

    [Fact] // Scenarios 3, 9
    public async Task VaultIsIsolatedAndStoresOnlyCiphertext()
    {
        var (a, b) = await TwoUsers();
        await b.PostJson("/api/vault/setup", new { kdfIterations = 600000, kdfSalt = "c2FsdA==", wrappedKey = "d3JhcHBlZA==", recoverySalt = "cnNhbHQ=", recoveryWrappedKey = "cndyYXA=" });
        const string cipher = "v1.aXZpdml2aXZpdml2.Y2lwaGVydGV4dGNpcGhlcnRleHQ=";
        var entry = await b.PostJson("/api/vault/entries", new { title = "Google", websiteUrl = "https://google.com", category = "Email", isFavorite = true, encryptedData = cipher });
        var id = entry.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/vault/entries/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.GetAsync("/api/vault/entries")).StatusCode); // A has no vault
        // Plaintext payloads are refused — the server only accepts ciphertext-shaped data
        var bad = await b.Http.PostAsJsonAsync("/api/vault/entries", new { title = "Bank", category = "Banking", encryptedData = "{\"password\":\"hunter2 plaintext\"}" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var stored = await app.WithDbAsync(db => db.PasswordEntries.IgnoreQueryFilters().Where(e => e.Id == id).Select(e => e.EncryptedData).FirstAsync());
        Assert.Equal(cipher, stored);
        // Global search returns metadata only
        var s = await b.GetJson("/api/search?q=google&modules=passwords");
        var hit = s.GetProperty("groups")[0].GetProperty("items")[0];
        Assert.DoesNotContain(cipher, hit.ToString());
    }

    [Fact] // Scenario 4
    public async Task DiaryIsPrivateAndEncryptedAtRest()
    {
        var (a, b) = await TwoUsers();
        var e = await b.PostJson("/api/diary", new { title = "My day", contentHtml = "<p>Very private thought<script>alert(1)</script></p>", entryDate = "2026-11-17", entryTime = "21:30:00", mood = "calm", tags = new[] { "personal" } });
        var id = e.GetProperty("id").GetGuid();
        Assert.DoesNotContain("<script>", e.GetProperty("contentHtml").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/diary/{id}")).StatusCode);
        var raw = await app.WithDbAsync(db => db.DiaryEntries.IgnoreQueryFilters().Where(x => x.Id == id).Select(x => x.EncryptedContent).FirstAsync());
        Assert.DoesNotContain("private", raw);
        Assert.StartsWith("v1:", raw);
    }

    [Fact] // Scenario 11
    public async Task CustomTablesAreIsolated()
    {
        var (a, b) = await TwoUsers();
        var t = await b.PostJson("/api/tables", new
        {
            name = "Medicine Register",
            columns = new object[]
            {
                new { name = "Brand Name", fieldType = "Text", isRequired = true, isUnique = true, isVisible = true },
                new { name = "MRP", fieldType = "Currency", isRequired = false, isUnique = false, isVisible = true },
                new { name = "GST", fieldType = "Decimal", isRequired = false, isUnique = false, isVisible = true },
                new { name = "Tax", fieldType = "Formula", formula = "{MRP} * {GST} / 100", isRequired = false, isUnique = false, isVisible = true },
                new { name = "Expiry Date", fieldType = "Date", isRequired = false, isUnique = false, isVisible = true },
            }
        });
        var tableId = t.GetProperty("id").GetGuid();
        var cols = t.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("id").GetString()!);
        var rec = await b.PostJson($"/api/tables/{tableId}/records", new { values = new Dictionary<string, string> { [cols["Brand Name"]] = "Dolo 650", [cols["MRP"]] = "30", [cols["GST"]] = "12", [cols["Expiry Date"]] = "2027-05-01" } });
        Assert.Equal("3.6", rec.GetProperty("values").GetProperty(cols["Tax"]).GetString());
        var dup = await b.Http.PostAsJsonAsync($"/api/tables/{tableId}/records", new { values = new Dictionary<string, string> { [cols["Brand Name"]] = "dolo 650" } });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode); // unique
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/tables/{tableId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.PostAsJsonAsync($"/api/tables/{tableId}/records/query", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.PostAsJsonAsync($"/api/tables/{tableId}/records", new { values = new Dictionary<string, string>() })).StatusCode);
        // Sorting/filtering works for the owner
        var q = await b.PostJson($"/api/tables/{tableId}/records/query", new { sortColumnId = cols["MRP"], sortDesc = true, filters = new[] { new { columnId = cols["MRP"], op = "gt", value = "10" } } });
        Assert.Equal(1, q.GetProperty("totalCount").GetInt32());
        // Removing a column with data requires confirmation
        var keep = t.GetProperty("columns").EnumerateArray().Where(c => c.GetProperty("name").GetString() is "Brand Name").Select(c => new { id = c.GetProperty("id").GetGuid(), name = "Brand Name", fieldType = "Text", isRequired = true, isUnique = true, isVisible = true }).ToArray();
        var change = await b.Http.PutAsJsonAsync($"/api/tables/{tableId}", new { name = "Medicine Register", columns = keep });
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Contains("confirmDataLoss", await change.Content.ReadAsStringAsync());
    }

    [Fact] // Scenario 5
    public async Task NonParticipantsCannotReadConversations()
    {
        var a = await app.RegisterAsync(name: "Asha");
        var b = await app.RegisterAsync(name: "Bharat");
        var c = await app.RegisterAsync(name: "Chetan");
        await a.PostJson("/api/chat/requests", new { publicId = b.PublicId });
        var reqs = await b.GetJson("/api/chat/requests?incoming=true");
        await b.PostJson($"/api/chat/requests/{reqs[0].GetProperty("id").GetGuid()}/accept", new { });
        var friends = await a.GetJson("/api/chat/friends");
        var conv = friends[0].GetProperty("conversationId").GetGuid();
        var msg = await a.PostJson($"/api/chat/conversations/{conv}/messages", new { body = "Namaste Bharat" });
        Assert.Equal("Namaste Bharat", (await b.GetJson($"/api/chat/conversations/{conv}/messages"))[0].GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.Http.GetAsync($"/api/chat/conversations/{conv}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Http.PostAsJsonAsync($"/api/chat/conversations/{conv}/messages", new { body = "hi" })).StatusCode);
        // Non-friends can't message, B can't delete A's message
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Http.DeleteAsync($"/api/chat/messages/{msg.GetProperty("id").GetGuid()}")).StatusCode);
        // Invite codes
        var inv = await c.PostJson("/api/chat/invitations", new { expiresInHours = 24, maxUses = 1 });
        var accepted = await a.PostJson($"/api/chat/invitations/accept?code={inv.GetProperty("code").GetString()}", new { });
        Assert.Equal(c.UserId, accepted.GetProperty("userId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Http.PostAsync($"/api/chat/invitations/accept?code={inv.GetProperty("code").GetString()}", null)).StatusCode);
    }
}

[Collection("api")]
public class AuthTests(TestApp app)
{
    [Fact] // Scenario 6
    public async Task InvalidAuthenticatorCodeIsRejected()
    {
        var u = await app.RegisterAsync();
        var setup = await u.PostJson("/api/auth/2fa/setup", new { });
        var secret = setup.GetProperty("secret").GetString()!.Replace(" ", "");
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Http.PostAsJsonAsync("/api/auth/2fa/enable", new { code = "123456" })).StatusCode);
        var codes = await u.PostJson("/api/auth/2fa/enable", new { code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp() });
        Assert.Equal(10, codes.GetProperty("codes").GetArrayLength());
        var anon = app.CreateClient();
        var login = await (await anon.PostAsJsonAsync("/api/auth/login", new { mobile = u.Mobile, password = "Str0ng@Pass" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("two_factor_required", login.GetProperty("status").GetString());
        Assert.False(login.TryGetProperty("accessToken", out var at) && at.ValueKind == JsonValueKind.String);
        var bad = await anon.PostAsJsonAsync("/api/auth/login/2fa", new { challengeToken = login.GetProperty("challengeToken").GetString(), code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        // A recovery code works exactly once
        var rc = codes.GetProperty("codes")[0].GetString();
        var ok = await anon.PostAsJsonAsync("/api/auth/login/2fa", new { challengeToken = login.GetProperty("challengeToken").GetString(), code = rc, isRecoveryCode = true });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var again = await anon.PostAsJsonAsync("/api/auth/login/2fa", new { challengeToken = login.GetProperty("challengeToken").GetString(), code = rc, isRecoveryCode = true });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact] // Scenarios 7 & 8
    public async Task IdleSessionsAreExpiredServerSide()
    {
        var u = await app.RegisterAsync();
        Assert.Equal(HttpStatusCode.OK, (await u.Http.GetAsync("/api/me")).StatusCode);
        // Simulate 61 seconds of inactivity (default timeout = 60 s)
        await app.WithDbAsync(db => db.UserSessions.Where(s => s.UserId == u.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTime.UtcNow.AddSeconds(-66))));
        var r = await u.Http.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("session_expired", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await u.Http.PostAsync("/api/auth/refresh", null)).StatusCode); // refresh revoked too
        var audit = await app.WithDbAsync(db => db.LoginAuditLogs.AnyAsync(l => l.UserId == u.UserId && l.Event == "auto_logout"));
        Assert.True(audit);
    }

    [Fact]
    public async Task PassiveRequestsDoNotExtendSession()
    {
        var u = await app.RegisterAsync();
        var before = DateTime.UtcNow.AddSeconds(-40);
        await app.WithDbAsync(db => db.UserSessions.Where(s => s.UserId == u.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, before)));
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/unread-count");
        req.Headers.Add("X-Activity", "passive");
        Assert.Equal(HttpStatusCode.OK, (await u.Http.SendAsync(req)).StatusCode);
        var last = await app.WithDbAsync(db => db.UserSessions.Where(s => s.UserId == u.UserId).Select(s => s.LastActivityAt).FirstAsync());
        Assert.True(last < DateTime.UtcNow.AddSeconds(-30));
    }

    [Fact]
    public async Task RefreshTokenRotationDetectsReuse()
    {
        var u = await app.RegisterAsync();
        var cookie = await app.WithDbAsync(db => db.RefreshTokens.CountAsync(t => t.Session!.UserId == u.UserId));
        Assert.Equal(1, cookie);
        Assert.Equal(HttpStatusCode.OK, (await u.Http.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await u.Http.PostAsync("/api/auth/refresh", null)).StatusCode);
        var tokens = await app.WithDbAsync(db => db.RefreshTokens.Where(t => t.Session!.UserId == u.UserId).ToListAsync());
        Assert.Equal(2, tokens.Count(t => t.RevokedAt != null));
        // Missing CSRF header is refused
        var noHeader = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await noHeader.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task LockoutAfterRepeatedFailures()
    {
        var u = await app.RegisterAsync();
        var anon = app.CreateClient();
        for (var i = 0; i < 5; i++) await anon.PostAsJsonAsync("/api/auth/login", new { mobile = u.Mobile, password = "wrong-password" });
        var r = await anon.PostAsJsonAsync("/api/auth/login", new { mobile = u.Mobile, password = "Str0ng@Pass" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("locked_out", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasswordsAndPinsAreHashed()
    {
        var u = await app.RegisterAsync();
        var user = await app.WithDbAsync(db => db.Users.FirstAsync(x => x.Id == u.UserId));
        Assert.DoesNotContain("Str0ng@Pass", user.PasswordHash);
        Assert.NotEqual("4826", user.PinHash);
        Assert.True(user.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task RegistrationDoesNotRequireOtpUntilSmsIsConfigured()
    {
        var cfg = await app.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/registration-config");
        Assert.False(cfg.GetProperty("requireMobileOtp").GetBoolean());
        var admin = await AdminAsync();
        var settings = await admin.GetJson("/api/admin/settings");
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(settings.GetRawText())!;
        dict["requireMobileOtp"] = true;
        var r = await admin.Http.PutAsJsonAsync("/api/admin/settings", dict);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // cannot be enabled without an active SMS provider
    }

    private static readonly SemaphoreSlim AdminLock = new(1, 1);
    private async Task<UserClient> AdminAsync()
    {
        await AdminLock.WaitAsync();
        try
        {
            var exists = await app.WithDbAsync(db => db.Users.AnyAsync(u => u.MobileNumber == TestApp.AdminMobile));
            return exists ? await app.LoginAsync(TestApp.AdminMobile) : await app.RegisterAsync(TestApp.AdminMobile, "Super Admin");
        }
        finally { AdminLock.Release(); }
    }

    [Fact]
    public async Task WebsiteFooterIsAdminManagedAndValidated()
    {
        var admin = await AdminAsync();
        var settings = await admin.GetJson("/api/admin/settings");
        var obj = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!.AsObject();
        var website = obj["website"]!.AsObject();
        website["companyLine"] = "A product by Test Co";
        website["columns"]!.AsArray()[0]!["links"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["labelEn"] = "Blog", ["url"] = "javascript:alert(1)" });
        var bad = await admin.Http.PutAsJsonAsync("/api/admin/settings", obj);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        website["columns"]!.AsArray()[0]!["links"]!.AsArray().Last()!["url"] = "https://nijmitra.com/blog";
        website["androidSha256Fingerprints"] = new System.Text.Json.Nodes.JsonArray("aa".PadRight(64, 'b'));
        (await admin.Http.PutAsJsonAsync("/api/admin/settings", obj)).EnsureSuccessStatusCode();

        var info = await app.CreateClient().GetFromJsonAsync<JsonElement>("/api/system/info");
        Assert.Equal("A product by Test Co", info.GetProperty("website").GetProperty("companyLine").GetString());
        Assert.Contains("https://nijmitra.com/blog", info.GetProperty("website").ToString());

        var links = await app.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/assetlinks.json");
        var target = links[0].GetProperty("target");
        Assert.Equal("com.nijmitra.app", target.GetProperty("package_name").GetString());
        Assert.Equal("AA:BB:" + string.Join(':', Enumerable.Repeat("BB", 30)), target.GetProperty("sha256_cert_fingerprints")[0].GetString()); // normalised format
    }

    [Fact]
    public async Task OfficeFilesCanBePreviewedOnlyByOwner()
    {
        var (a, b) = (await app.RegisterAsync(), await app.RegisterAsync());
        var csv = System.Text.Encoding.UTF8.GetBytes("Name,City\nAmit,Delhi\n");
        var doc = await a.Upload("/api/documents", csv, "list.csv", new() { ["name"] = "List", ["category"] = "Personal" });
        var id = doc.GetProperty("id").GetGuid();
        var preview = await a.GetJson($"/api/documents/{id}/download?inline=true&view=preview");
        Assert.Equal("Amit", preview.GetProperty("sheets")[0].GetProperty("rows")[1][0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/documents/{id}/download?view=preview")).StatusCode);
        var pdf = await a.Upload("/api/documents", Samples.Pdf, "a.pdf", new() { ["name"] = "Pdf", ["category"] = "Personal" });
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Http.GetAsync($"/api/documents/{pdf.GetProperty("id").GetGuid()}/download?view=preview")).StatusCode);
    }

    [Fact] // Scenario 10 + impersonation guarantees
    public async Task AdminCannotReadUserSecrets()
    {
        var admin = await AdminAsync();
        var u = await app.RegisterAsync(name: "Private Person");
        await u.PostJson("/api/vault/setup", new { kdfIterations = 600000, kdfSalt = "c2FsdA==", wrappedKey = "d3JhcHBlZA==", recoverySalt = "cnNhbHQ=", recoveryWrappedKey = "cndyYXA=" });
        await u.PostJson("/api/vault/entries", new { title = "Bank", category = "Banking", encryptedData = "v1.aXY=.Y3Q=" });
        var detail = await admin.GetJson($"/api/admin/users/{u.UserId}");
        Assert.Equal(1, detail.GetProperty("recordCounts").GetProperty("Vault entries").GetInt32());
        Assert.DoesNotContain("v1.aXY=", detail.ToString());
        Assert.DoesNotContain("passwordHash", detail.ToString(), StringComparison.OrdinalIgnoreCase);
        // Normal users cannot reach admin APIs
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Http.GetAsync("/api/admin/users")).StatusCode);
        // Impersonation: works, is audited, user is notified, vault & chat stay blocked
        var imp = await admin.PostJson($"/api/auth/impersonate/{u.UserId}", new { });
        var impClient = app.CreateClient();
        impClient.DefaultRequestHeaders.Authorization = new("Bearer", imp.GetProperty("accessToken").GetString());
        var me = await impClient.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.True(me.GetProperty("isImpersonated").GetBoolean());
        Assert.Equal(0, me.GetProperty("permissions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await impClient.GetAsync("/api/vault/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await impClient.GetAsync("/api/chat/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await impClient.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "x", newPassword = "Abcdef@123", confirmPassword = "Abcdef@123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await impClient.GetAsync("/api/admin/users")).StatusCode);
        Assert.True(await app.WithDbAsync(db => db.AdminAuditLogs.AnyAsync(l => l.TargetUserId == u.UserId && l.Action == "user_impersonation_started")));
        Assert.True(await app.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == u.UserId && n.EventType == NotificationEvents.AdminAccess)));
        // "Return to admin": the parked admin refresh cookie restores the administrator's own session.
        using var end = new HttpRequestMessage(HttpMethod.Post, "/api/auth/impersonation/end");
        end.Headers.Authorization = new("Bearer", imp.GetProperty("accessToken").GetString());
        var endRes = await admin.Http.SendAsync(end);
        Assert.Equal(HttpStatusCode.OK, endRes.StatusCode);
        var adminToken = (await endRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        using var check = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        check.Headers.Authorization = new("Bearer", adminToken);
        var back = await (await admin.Http.SendAsync(check)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(back.GetProperty("isImpersonated").GetBoolean());
        Assert.Equal(admin.UserId, back.GetProperty("id").GetGuid());
    }
}

[Collection("api")]
public class FeatureTests(TestApp app)
{
    [Fact] // Scenario 15
    public async Task PartialPaymentsCalculateCorrectly()
    {
        var u = await app.RegisterAsync();
        var t = await u.PostJson("/api/udhar", new { direction = "Given", personName = "Rahul", amount = 1000, transactionDate = "2026-11-01", expectedReturnDate = "2026-11-30" });
        var id = t.GetProperty("id").GetGuid();
        await u.PostJson($"/api/udhar/{id}/payments", new { amount = 300, paymentDate = "2026-11-05", method = "Upi" });
        var after = await u.PostJson($"/api/udhar/{id}/payments", new { amount = 200, paymentDate = "2026-11-06", method = "Cash" });
        Assert.Equal(500m, after.GetProperty("outstanding").GetDecimal());
        Assert.Equal("PartiallySettled", after.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Http.PostAsJsonAsync($"/api/udhar/{id}/payments", new { amount = 600, paymentDate = "2026-11-07", method = "Cash" })).StatusCode);
        var settled = await u.PostJson($"/api/udhar/{id}/payments", new { amount = 500, paymentDate = "2026-11-08", method = "Cash" });
        Assert.Equal("Settled", settled.GetProperty("status").GetString());
        var summary = await u.GetJson("/api/udhar/summary");
        Assert.Equal(0m, summary.GetProperty("totalReceivable").GetDecimal());
        Assert.Equal(1000m, summary.GetProperty("amountReceived").GetDecimal());
        var pdf = await u.Http.GetAsync("/api/udhar/statement?person=Rahul&format=pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FinanceBudgetReportsAndExports()
    {
        var u = await app.RegisterAsync();
        var cats = await u.GetJson("/api/finance/categories?type=Expense");
        var food = cats.EnumerateArray().First(c => c.GetProperty("name").GetString() == "Food").GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await u.PutJson("/api/finance/budget", new { year = today.Year, month = today.Month, totalAmount = 1000, alertThresholdPercent = 80, categories = new[] { new { categoryId = food, amount = 500 } } });
        await u.PostJson("/api/finance/expenses", new { name = "Dinner", amount = 900, categoryId = food, paymentMethod = "Upi", date = today.ToString("yyyy-MM-dd") });
        await u.PostJson("/api/finance/income", new { title = "November salary", source = "Salary", amount = 50000, date = today.ToString("yyyy-MM-dd") });
        var budget = await u.GetJson($"/api/finance/budget/{today.Year}/{today.Month}");
        Assert.Equal(90m, budget.GetProperty("utilizationPercent").GetDecimal());
        Assert.True(budget.GetProperty("categories")[0].GetProperty("overspent").GetBoolean());
        Assert.True(await app.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == u.UserId && n.EventType == NotificationEvents.BudgetAlert)));
        var dash = await u.GetJson("/api/dashboard");
        Assert.Equal(50000m, dash.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(900m, dash.GetProperty("totalExpenses").GetDecimal());
        Assert.Equal(100m, dash.GetProperty("remainingBudget").GetDecimal());
        var report = await u.GetJson("/api/finance/reports?period=monthly");
        Assert.Equal(49100m, report.GetProperty("netSavings").GetDecimal());
        foreach (var f in new[] { "xlsx", "csv", "pdf" })
            Assert.Equal(HttpStatusCode.OK, (await u.Http.GetAsync($"/api/finance/expenses/export?format={f}")).StatusCode);
    }

    [Fact] // Scenario 14
    public async Task DuplicateRemindersArePrevented()
    {
        var u = await app.RegisterAsync();
        var start = DateTime.UtcNow.AddMinutes(10);
        var ev = await u.PostJson("/api/calendar/events", new { title = "Client meeting", eventType = "Meeting", startUtc = start, endUtc = start.AddHours(1), allDay = false, priority = "Normal", reminderOffsets = new[] { 10, 5 }, recurrence = "None" });
        var id = ev.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var planner = scope.ServiceProvider.GetRequiredService<NIJMITRA.Application.Journal.ReminderPlanner>();
            await planner.PlanAllAsync(default);
            await planner.PlanAllAsync(default);
        }
        var schedules = await app.WithDbAsync(db => db.ReminderSchedules.Where(s => s.SourceId == id).ToListAsync());
        Assert.Equal(2, schedules.Count);
        // Make one due and dispatch twice
        await app.WithDbAsync(db => db.ReminderSchedules.Where(s => s.SourceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FireAtUtc, DateTime.UtcNow.AddSeconds(-5))));
        using (var scope = app.Services.CreateScope())
        {
            var planner = scope.ServiceProvider.GetRequiredService<NIJMITRA.Application.Journal.ReminderPlanner>();
            await planner.DispatchDueAsync(default);
            await planner.DispatchDueAsync(default);
            await planner.PlanAllAsync(default); // re-planning must not resurrect sent slots
            await planner.DispatchDueAsync(default);
        }
        var notes = await app.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == u.UserId && n.EventType == NotificationEvents.MeetingReminder));
        Assert.Equal(2, notes); // one per offset, never duplicated
    }

    [Fact]
    public async Task ProductRegisterImportExport()
    {
        var u = await app.RegisterAsync();
        var reg = await u.PostJson("/api/product-registers", new { name = "Pharmacy", customFields = new[] { new { name = "Rack", fieldType = "Text" } } });
        var id = reg.GetProperty("id").GetGuid();
        var csv = "Product Name,Brand Name,MRP,GST %,Expiry Date,Rack\nParacetamol,Dolo,30,12,2027-01-31,A1\n,Missing name,1,1,,\n";
        using var form = new MultipartFormDataContent { { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "products.csv" } };
        var res = await (await u.Http.PostAsync($"/api/product-registers/{id}/import", form)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, res.GetProperty("imported").GetInt32());
        Assert.Equal(1, res.GetProperty("failed").GetInt32());
        var list = await u.GetJson($"/api/product-registers/{id}/products?search=dolo");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await u.Http.GetAsync($"/api/product-registers/{id}/export?format=pdf")).StatusCode);
    }

    [Fact]
    public async Task ContactSummaryShowsBalances()
    {
        var u = await app.RegisterAsync();
        var c = await u.PostJson("/api/contacts", new { name = "Rahul Verma", mobile = "9811111111" });
        var cid = c.GetProperty("id").GetGuid();
        await u.PostJson("/api/udhar", new { direction = "Given", contactId = cid, amount = 2000, transactionDate = "2026-11-01" });
        await u.PostJson("/api/udhar", new { direction = "Borrowed", contactId = cid, amount = 500, transactionDate = "2026-11-02" });
        var s = await u.GetJson($"/api/contacts/{cid}/summary");
        Assert.Equal(2000m, s.GetProperty("receivable").GetDecimal());
        Assert.Equal(500m, s.GetProperty("payable").GetDecimal());
        Assert.Equal(1500m, s.GetProperty("netBalance").GetDecimal());
        var search = await u.GetJson("/api/search?q=Rahul");
        var modules = search.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("module").GetString()).ToList();
        Assert.Contains("contacts", modules);
        Assert.Contains("udhar", modules);
    }
}

[Collection("api")]
public class NotificationTests(TestApp app)
{
    private async Task<Guid> AddEmailProviderAsync(AppDbContextAccessor? _ = null)
    {
        return await app.WithDbAsync(async db =>
        {
            var existing = await db.CommunicationProviders.FirstOrDefaultAsync(p => p.Name == "Test SMTP");
            if (existing != null) return existing.Id;
            var p = new CommunicationProvider { Name = "Test SMTP", Channel = NotificationChannel.Email, ProviderType = ProviderType.Smtp, IsActive = true, IsDefault = true,
                SettingsJson = "{\"host\":\"127.0.0.1\",\"port\":\"1\",\"security\":\"None\",\"senderEmail\":\"noreply@nijmitra.com\"}" };
            db.CommunicationProviders.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    public sealed class AppDbContextAccessor;

    [Fact] // Scenarios 12 & 13
    public async Task PolicyMatrixAndMandatorySecurityDelivery()
    {
        await AddEmailProviderAsync();
        var u = await app.RegisterAsync();
        await u.PostJson("/api/auth/email", new { email = $"u{Guid.NewGuid():N}@example.com" });
        // Email is pending verification; mark verified directly for the test
        await app.WithDbAsync(db => db.Users.Where(x => x.Id == u.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, x => x.PendingEmail).SetProperty(x => x.EmailVerified, true).SetProperty(x => x.PendingEmail, (string?)null)));
        // User turns email off entirely
        var prefs = await u.GetJson("/api/me/notification-preferences");
        var p = JsonSerializer.Deserialize<Dictionary<string, object>>(prefs.GetRawText())!;
        p["emailEnabled"] = false;
        await u.PutJson("/api/me/notification-preferences", p);
        using var scope = app.Services.CreateScope();
        var current = scope.ServiceProvider.GetRequiredService<NIJMITRA.API.Infrastructure.CurrentUserService>();
        current.RunAs(u.UserId);
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        // Meeting reminder respects the user's opt-out
        var meeting = await svc.DispatchAsync(new NotificationRequest(NotificationEvents.MeetingReminder, u.UserId, "Standup", "in 5 min", DedupeKey: Guid.NewGuid().ToString()));
        Assert.DoesNotContain(NotificationChannel.Email, meeting.QueuedChannels);
        // Security-critical event is delivered regardless of the user's preference
        var security = await svc.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, u.UserId, "Password changed", "x", DedupeKey: Guid.NewGuid().ToString()));
        Assert.Contains(NotificationChannel.Email, security.QueuedChannels);
        // Admin disables Meeting Reminder email at platform level → never queued even if user re-enables
        var adminDb = scope.ServiceProvider.GetRequiredService<NIJMITRA.Infrastructure.Persistence.AppDbContext>();
        await adminDb.NotificationPolicies.Where(x => x.EventType == NotificationEvents.MeetingReminder).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailEnabled, false));
        p["emailEnabled"] = true;
        await u.PutJson("/api/me/notification-preferences", p);
        var again = await svc.DispatchAsync(new NotificationRequest(NotificationEvents.MeetingReminder, u.UserId, "Standup", "in 5 min", DedupeKey: Guid.NewGuid().ToString()));
        Assert.DoesNotContain(NotificationChannel.Email, again.QueuedChannels);
        Assert.Contains(again.SkippedReasons, r => r.Contains("platform policy"));
        await adminDb.NotificationPolicies.Where(x => x.EventType == NotificationEvents.MeetingReminder).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailEnabled, true));
        // Duplicate dedupe key is suppressed
        var key = Guid.NewGuid().ToString();
        await svc.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, u.UserId, "t", "b", DedupeKey: key));
        var dup = await svc.DispatchAsync(new NotificationRequest(NotificationEvents.SecurityChange, u.UserId, "t", "b", DedupeKey: key));
        Assert.Empty(dup.QueuedChannels);
    }

    [Fact]
    public async Task AdminCannotDisableAllChannelsForSecurityEvents()
    {
        var admin = await app.WithDbAsync(db => db.Users.AnyAsync(u => u.MobileNumber == TestApp.AdminMobile))
            ? await app.LoginAsync(TestApp.AdminMobile) : await app.RegisterAsync(TestApp.AdminMobile, "Super Admin");
        var r = await admin.Http.PutAsJsonAsync("/api/admin/communication/policies", new[] { new { eventType = NotificationEvents.PasswordReset, inAppEnabled = true, emailEnabled = false, smsEnabled = false, pushEnabled = false, whatsAppEnabled = false, userCanOptOut = true } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact] // Scenario 17
    public async Task ProviderFailuresAreLoggedWithoutSecrets()
    {
        const string secretKey = "SUPERSECRETKEY987654321";
        var providerId = await app.WithDbAsync(async db =>
        {
            var enc = app.Services.GetRequiredService<IFieldEncryptor>();
            var p = new CommunicationProvider { Name = "Broken SMS", Channel = NotificationChannel.Sms, ProviderType = ProviderType.CustomHttpSms, IsActive = true, Priority = 1,
                SettingsJson = "{\"apiUrl\":\"http://127.0.0.1:1/send?key={{apiKey}}&to={{mobile}}\",\"httpMethod\":\"GET\",\"authType\":\"None\"}",
                EncryptedSecretsJson = enc.Encrypt("{\"apiKey\":\"" + secretKey + "\"}") };
            db.CommunicationProviders.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
        try
        {
            var u = await app.RegisterAsync();
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<NIJMITRA.API.Infrastructure.CurrentUserService>().RunAs(u.UserId);
                var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await svc.DispatchAsync(new NotificationRequest(NotificationEvents.PasswordReset, u.UserId, "Reset", "Your code is 123456", Variables: new Dictionary<string, string> { ["OTP"] = "123456" }, DedupeKey: Guid.NewGuid().ToString()));
            }
            await app.Services.GetRequiredService<NotificationDispatcher>().ProcessBatchAsync(default);
            var logs = await app.WithDbAsync(db => db.CommunicationLogs.Where(l => l.ProviderName == "Broken SMS").ToListAsync());
            Assert.NotEmpty(logs);
            Assert.All(logs, l =>
            {
                Assert.Equal(QueueStatus.Failed, l.Status);
                Assert.DoesNotContain(secretKey, l.FailureReason ?? "");
                Assert.DoesNotContain("123456", l.FailureReason ?? "");
                Assert.DoesNotContain(u.Mobile, l.MaskedRecipient);
            });
            var item = await app.WithDbAsync(db => db.NotificationQueue.Where(q => q.UserId == u.UserId && q.Channel == NotificationChannel.Sms).FirstAsync());
            Assert.Equal(QueueStatus.Pending, item.Status); // scheduled for retry
            Assert.DoesNotContain("123456", item.EncryptedBody ?? "");
        }
        finally
        {
            await app.WithDbAsync(db => db.CommunicationProviders.Where(p => p.Id == providerId).ExecuteDeleteAsync());
        }
    }
}
