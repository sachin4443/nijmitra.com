using Microsoft.AspNetCore.Mvc;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Admin;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.Controllers;

[Route("api/admin"), DenyImpersonation]
public class AdminController(AdminService svc, AuthService auth, ICurrentUser current, IWebHostEnvironment env) : AppControllerBase
{
    [HttpGet("dashboard"), HasPermission(Permissions.DashboardView)] public Task<AdminDashboardDto> Dashboard(CancellationToken ct) => svc.DashboardAsync(ct);

    [HttpGet("users"), HasPermission(Permissions.UsersView)] public Task<PagedResult<AdminUserRow>> Users([FromQuery] AdminUserQuery q, CancellationToken ct) => svc.UsersAsync(q, ct);
    [HttpGet("users/{id:guid}"), HasPermission(Permissions.UsersView)] public Task<AdminUserDetail> User(Guid id, CancellationToken ct) => svc.UserAsync(id, ct);
    [HttpGet("users/{id:guid}/login-history"), HasPermission(Permissions.UsersView)]
    public Task<PagedResult<LoginHistoryDto>> History(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => svc.LoginHistoryAsync(id, page, pageSize, ct);
    [HttpPost("users/{id:guid}/status"), HasPermission(Permissions.UsersManage)] public async Task<IActionResult> Status(Guid id, StatusChangeRequest r, CancellationToken ct) { await svc.ChangeStatusAsync(id, r, ct); return NoContent(); }
    [HttpPost("users/{id:guid}/unlock"), HasPermission(Permissions.UsersManage)] public async Task<IActionResult> Unlock(Guid id, CancellationToken ct) { await svc.UnlockAsync(id, ct); return NoContent(); }
    [HttpPost("users/{id:guid}/reset-password"), HasPermission(Permissions.UsersManage)] public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct) => Ok(new { temporaryPassword = await svc.ResetPasswordAsync(id, ct) });
    [HttpPost("users/{id:guid}/reset-2fa"), HasPermission(Permissions.UsersManage)] public async Task<IActionResult> Reset2fa(Guid id, CancellationToken ct) { await svc.ResetTwoFactorAsync(id, ct); return NoContent(); }
    [HttpPost("users/{id:guid}/revoke-sessions"), HasPermission(Permissions.UsersManage)] public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) => Ok(new { revoked = await svc.RevokeSessionsAsync(id, ct) });
    [HttpPut("users/{id:guid}/roles"), HasPermission(Permissions.RolesManage)] public async Task<IActionResult> Roles(Guid id, RolesRequest r, CancellationToken ct) { await svc.SetRolesAsync(id, r, ct); return NoContent(); }

    /// <summary>Starts "sign in as user": the admin's refresh cookie is parked in nm_rt_admin and restored on exit.</summary>
    [HttpPost("users/{id:guid}/impersonate"), HasPermission(Permissions.UsersImpersonate)]
    public async Task<IActionResult> Impersonate(Guid id, CancellationToken ct)
    {
        var tokens = await svc.ImpersonateAsync(id, ct);
        var opts = new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || env.IsProduction(), SameSite = SameSiteMode.Strict, Path = "/api/auth", Expires = tokens.RefreshTokenExpiresAt };
        if (Request.Cookies[AuthController.RefreshCookie] is { } adminRt) Response.Cookies.Append(AuthController.AdminRefreshCookie, adminRt, opts);
        Response.Cookies.Append(AuthController.RefreshCookie, tokens.RefreshToken, opts);
        return Ok(new { accessToken = tokens.AccessToken, expiresAt = tokens.AccessTokenExpiresAt });
    }

    [HttpGet("roles"), HasPermission(Permissions.UsersView)] public Task<IReadOnlyList<RoleDto>> RoleList(CancellationToken ct) => svc.RolesAsync(ct);
    [HttpGet("permissions"), HasPermission(Permissions.RolesManage)] public Task<IReadOnlyList<PermissionDto>> PermissionList(CancellationToken ct) => svc.PermissionsAsync(ct);
    [HttpPost("roles"), HasPermission(Permissions.RolesManage)] public Task<RoleDto> CreateRole(RoleRequest r, CancellationToken ct) => svc.SaveRoleAsync(null, r, ct);
    [HttpPut("roles/{id:guid}"), HasPermission(Permissions.RolesManage)] public Task<RoleDto> UpdateRole(Guid id, RoleRequest r, CancellationToken ct) => svc.SaveRoleAsync(id, r, ct);
    [HttpDelete("roles/{id:guid}"), HasPermission(Permissions.RolesManage)] public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct) { await svc.DeleteRoleAsync(id, ct); return NoContent(); }

    [HttpGet("settings"), HasPermission(Permissions.SettingsManage)] public Task<PlatformSettings> Settings(CancellationToken ct) => svc.GetSettingsAsync(ct);
    [HttpPut("settings"), HasPermission(Permissions.SettingsManage)] public Task<PlatformSettings> SaveSettings(PlatformSettings s, CancellationToken ct) => svc.SaveSettingsAsync(s, ct);
    [HttpPost("branding/{kind}"), HasPermission(Permissions.SettingsManage), RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<PlatformSettings> Branding(string kind, IFormFile file, CancellationToken ct) => await svc.UploadBrandingAsync(kind == "favicon" ? "favicon" : "logo", await file.ToUploadAsync(), ct);
    [HttpDelete("branding/{kind}"), HasPermission(Permissions.SettingsManage)] public Task<PlatformSettings> ResetBranding(string kind, CancellationToken ct) => svc.ResetBrandingAsync(kind, ct);

    [HttpGet("audit"), HasPermission(Permissions.AuditView)] public Task<PagedResult<AuditRow>> Audit([FromQuery] AuditQuery q, CancellationToken ct) => svc.AuditAsync(q, ct);
    [HttpGet("reports"), HasPermission(Permissions.ReportsManage)] public Task<PagedResult<ReportRow>> Reports([FromQuery] ReportStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => svc.ReportsAsync(status, page, pageSize, ct);
    [HttpPost("reports/{id:guid}"), HasPermission(Permissions.ReportsManage)] public async Task<IActionResult> Report(Guid id, [FromQuery] ReportStatus status, CancellationToken ct) { await svc.UpdateReportAsync(id, status, ct); return NoContent(); }
}

public record DbConnectionRequest(string? Host, int? Port, string? Database, string? Username, string? Password, bool Ssl, string? ConnectionString, bool ApplyMigrations = true);

/// <summary>Admin-editable PostgreSQL connection (Super Admin / database.manage only). The password is never returned.</summary>
[Route("api/admin/database"), DenyImpersonation, HasPermission(Permissions.DatabaseManage)]
public class DatabaseAdminController(IDatabaseConnectionStore store, IAuditService audit, ICurrentUser current, IServiceProvider sp) : AppControllerBase
{
    [HttpGet] public DatabaseConnectionInfo Get() => store.Describe();

    private string Build(DbConnectionRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.ConnectionString)) return r.ConnectionString.Trim();
        var existing = new Npgsql.NpgsqlConnectionStringBuilder(store.ConnectionString);
        var b = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = r.Host ?? existing.Host, Port = r.Port ?? existing.Port, Database = r.Database ?? existing.Database, Username = r.Username ?? existing.Username,
            Password = string.IsNullOrEmpty(r.Password) ? existing.Password : r.Password, SslMode = r.Ssl ? Npgsql.SslMode.Require : Npgsql.SslMode.Prefer
        };
        return b.ConnectionString;
    }

    [HttpPost("test")]
    public async Task<DatabaseTestResult> Test(DbConnectionRequest r, CancellationToken ct)
    {
        string cs;
        try { cs = Build(r); } catch (ArgumentException ex) { return new DatabaseTestResult(false, ex.Message, null); }
        return await store.TestAsync(cs, ct);
    }

    [HttpPut]
    public async Task<IActionResult> Save(DbConnectionRequest r, CancellationToken ct)
    {
        string cs;
        try { cs = Build(r); } catch (ArgumentException ex) { throw new ValidationAppException("connectionString", ex.Message); }
        var test = await store.TestAsync(cs, ct);
        if (!test.Success) throw new ValidationAppException("connectionString", "Connection failed: " + test.Message);
        var adminId = current.RequireUserId();
        await audit.LogAdminAsync(adminId, "database_connection_changed", null, $"{new Npgsql.NpgsqlConnectionStringBuilder(cs).Host}/{new Npgsql.NpgsqlConnectionStringBuilder(cs).Database}", ct);
        store.Save(cs);
        if (r.ApplyMigrations)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database, ct);
            await DataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<ILogger<DatabaseAdminController>>(), ct);
        }
        return Ok(new { message = "Database connection updated. New requests now use this database." + (r.ApplyMigrations ? " Schema migrated." : ""), info = store.Describe() });
    }
}

[Route("api/admin/communication"), DenyImpersonation]
public class CommunicationAdminController(CommunicationAdminService svc) : AppControllerBase
{
    [HttpGet("provider-definitions"), HasPermission(Permissions.CommunicationManage)] public IReadOnlyList<ProviderDefinition> Definitions() => svc.Definitions();
    [HttpGet("providers"), HasPermission(Permissions.CommunicationManage)] public Task<IReadOnlyList<ProviderDto>> Providers(CancellationToken ct) => svc.ProvidersAsync(ct);
    [HttpPost("providers"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderDto> Create(ProviderRequest r, CancellationToken ct) => svc.SaveProviderAsync(null, r, ct);
    [HttpPut("providers/{id:guid}"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderDto> Update(Guid id, ProviderRequest r, CancellationToken ct) => svc.SaveProviderAsync(id, r, ct);
    [HttpDelete("providers/{id:guid}"), HasPermission(Permissions.CommunicationManage)] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteProviderAsync(id, ct); return NoContent(); }
    [HttpPost("providers/{id:guid}/active"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderDto> Active(Guid id, [FromQuery] bool value, CancellationToken ct) => svc.SetActiveAsync(id, value, ct);
    [HttpPost("providers/{id:guid}/test-connection"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderDto> TestConn(Guid id, CancellationToken ct) => svc.TestConnectionAsync(id, ct);
    [HttpPost("providers/{id:guid}/test-send"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderTestResult> TestSend(Guid id, TestSendRequest r, CancellationToken ct) => svc.SendTestAsync(id, r, ct);
    [HttpPost("vapid-keys"), HasPermission(Permissions.CommunicationManage)] public object Vapid() => svc.GenerateVapidKeys();

    [HttpGet("policies"), HasPermission(Permissions.CommunicationManage)] public Task<IReadOnlyList<PolicyDto>> Policies(CancellationToken ct) => svc.PoliciesAsync(ct);
    [HttpPut("policies"), HasPermission(Permissions.CommunicationManage)] public Task<IReadOnlyList<PolicyDto>> SavePolicies(List<PolicyUpdate> r, CancellationToken ct) => svc.SavePoliciesAsync(r, ct);

    [HttpGet("templates"), HasPermission(Permissions.CommunicationManage)] public Task<IReadOnlyList<TemplateDto>> Templates(CancellationToken ct) => svc.TemplatesAsync(ct);
    [HttpPost("templates"), HasPermission(Permissions.CommunicationManage)] public Task<TemplateDto> CreateTemplate(TemplateRequest r, CancellationToken ct) => svc.SaveTemplateAsync(null, r, ct);
    [HttpPut("templates/{id:guid}"), HasPermission(Permissions.CommunicationManage)] public Task<TemplateDto> UpdateTemplate(Guid id, TemplateRequest r, CancellationToken ct) => svc.SaveTemplateAsync(id, r, ct);
    [HttpDelete("templates/{id:guid}"), HasPermission(Permissions.CommunicationManage)] public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct) { await svc.DeleteTemplateAsync(id, ct); return NoContent(); }
    [HttpPost("templates/preview"), HasPermission(Permissions.CommunicationManage)] public Task<TemplatePreview> Preview(TemplateRequest r, CancellationToken ct) => svc.PreviewAsync(r, ct);
    [HttpPost("templates/test"), HasPermission(Permissions.CommunicationManage)] public Task<ProviderTestResult> TestTemplate(TemplateRequest r, [FromQuery] string recipient, CancellationToken ct) => svc.TestTemplateAsync(r, recipient, ct);

    [HttpGet("logs"), HasPermission(Permissions.CommunicationLogsView)] public Task<PagedResult<LogRow>> Logs([FromQuery] LogQuery q, CancellationToken ct) => svc.LogsAsync(q, ct);
    [HttpGet("queue"), HasPermission(Permissions.CommunicationLogsView)] public Task<QueueStats> Queue(CancellationToken ct) => svc.QueueStatsAsync(ct);

    [HttpGet("announcements"), HasPermission(Permissions.AnnouncementsSend)] public Task<PagedResult<AnnouncementDto>> Announcements([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => svc.AnnouncementsAsync(page, pageSize, ct);
    [HttpPost("announcements"), HasPermission(Permissions.AnnouncementsSend)] public Task<AnnouncementDto> Announce(AnnouncementRequest r, CancellationToken ct) => svc.SendAnnouncementAsync(r, ct);
}
