using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application.Account;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Files;

namespace NIJMITRA.API.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class AccountController(AccountService account) : ControllerBase
{
    [HttpGet] public Task<MeDto> Me(CancellationToken ct) => account.GetMeAsync(ct);

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest r, CancellationToken ct) { await account.UpdateProfileAsync(r, ct); return NoContent(); }

    [HttpPut("theme")]
    public async Task<IActionResult> Theme(ThemeRequest r, CancellationToken ct) { await account.SetThemeAsync(r.Theme, ct); return NoContent(); }

    [HttpGet("dashboard-layout")]
    public async Task<IActionResult> GetLayout(CancellationToken ct) => Ok(new { layoutJson = await account.GetDashboardLayoutAsync(ct) });

    [HttpPut("dashboard-layout")]
    public async Task<IActionResult> SaveLayout(DashboardLayoutRequest r, CancellationToken ct) { await account.SaveDashboardLayoutAsync(r.LayoutJson, ct); return NoContent(); }

    [HttpPost("avatar"), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct) => Ok(new { fileId = await account.UploadAvatarAsync(await file.ToUploadAsync(), ct) });

    [HttpGet("avatar/{userId:guid}")]
    public async Task<IActionResult> Avatar(Guid userId, CancellationToken ct)
    {
        var f = await account.GetAvatarAsync(userId, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        return File(f.Content, f.ContentType);
    }

    [HttpGet("notification-preferences")] public Task<PreferencesDto> GetPrefs(CancellationToken ct) => account.GetPreferencesAsync(ct);

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> SavePrefs(PreferencesDto r, CancellationToken ct) { await account.SavePreferencesAsync(r, ct); return NoContent(); }

    [HttpPost("push-subscriptions")]
    public async Task<IActionResult> Subscribe(PushSubscribeRequest r, CancellationToken ct) { await account.SubscribePushAsync(r, ct); return NoContent(); }

    [HttpDelete("push-subscriptions")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint, CancellationToken ct) { await account.UnsubscribePushAsync(endpoint, ct); return NoContent(); }

    [HttpPost("export"), DenyImpersonation]
    public async Task<IActionResult> Export(CancellationToken ct) =>
        File(await account.ExportDataAsync(ct), "application/zip", $"nijmitra-export-{DateTime.UtcNow:yyyyMMdd}.zip");

    [HttpPost("delete-account"), DenyImpersonation]
    public async Task<DeletionRequestDto> DeleteAccount(DeleteAccountRequest r, CancellationToken ct)
    {
        var result = await account.RequestDeletionAsync(r, ct);
        Response.Cookies.Delete(AuthController.RefreshCookie, new CookieOptions { Path = "/api/auth" });
        return result;
    }
}

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController(FileService files) : ControllerBase
{
    /// <summary>Streams a file owned by the caller. inline=true is used for previews (PDF/images).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] bool inline = false, CancellationToken ct = default)
    {
        var f = await files.OpenOwnAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        return inline ? File(f.Content, f.ContentType) : File(f.Content, f.ContentType, f.FileName);
    }
}
