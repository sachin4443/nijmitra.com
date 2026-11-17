using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Auth;

namespace NIJMITRA.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, ICurrentUser current, IWebHostEnvironment env) : ControllerBase
{
    public const string RefreshCookie = "nm_rt";
    public const string AdminRefreshCookie = "nm_rt_admin";
    public const string CsrfHeader = "X-Requested-With";

    [HttpGet("registration-config")]
    public Task<RegistrationConfigDto> RegistrationConfig(CancellationToken ct) => auth.GetRegistrationConfigAsync(ct);

    [HttpPost("register/send-otp"), EnableRateLimiting("auth")]
    public async Task<IActionResult> SendRegistrationOtp(SendOtpRequest r, CancellationToken ct)
    {
        await auth.SendRegistrationOtpAsync(r.Mobile, ct);
        return Ok(new { message = "Verification code sent." });
    }

    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest r, CancellationToken ct)
    {
        await auth.RegisterAsync(r, ct);
        return Ok(new { message = "Registration successful. Please sign in." });
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest r, CancellationToken ct) => Respond(await auth.LoginAsync(r, ct));

    [HttpPost("login/2fa"), EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginTwoFactor(TwoFactorLoginRequest r, [FromHeader(Name = "X-Device-Key")] string? deviceKey, CancellationToken ct)
        => Respond(await auth.VerifyTwoFactorLoginAsync(r, deviceKey, ct));

    [HttpPost("login/2fa/setup"), EnableRateLimiting("auth")]
    public Task<TwoFactorSetupDto> LoginTwoFactorSetup(ChallengeRequest r, CancellationToken ct) => auth.ChallengeSetupAsync(r.ChallengeToken, ct);

    [HttpPost("login/2fa/enable"), EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginTwoFactorEnable(ChallengeEnableRequest r, [FromHeader(Name = "X-Device-Key")] string? deviceKey, CancellationToken ct)
        => Respond(await auth.ChallengeEnableAsync(r, deviceKey, ct));

    [HttpPost("refresh"), EnableRateLimiting("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        RequireCsrfHeader();
        try
        {
            var tokens = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct);
            SetRefreshCookie(RefreshCookie, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
            return Ok(new { accessToken = tokens.AccessToken, expiresAt = tokens.AccessTokenExpiresAt });
        }
        catch (Application.Common.UnauthorizedAppException)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
            throw;
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? reason, CancellationToken ct)
    {
        RequireCsrfHeader();
        var why = reason == "idle" ? "idle_timeout" : "user_logout";
        await auth.LogoutAsync(current.SessionId, Request.Cookies[RefreshCookie], why, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTime.UnixEpoch));
        if (Request.Cookies[AdminRefreshCookie] is { } admin)
        {
            await auth.LogoutAsync(null, admin, why, ct);
            Response.Cookies.Delete(AdminRefreshCookie, CookieOptions(DateTime.UnixEpoch));
        }
        return Ok(new { message = "Signed out." });
    }

    /// <summary>Genuine user activity heartbeat sent by the client's idle tracker.</summary>
    [Authorize, HttpPost("activity")]
    public IActionResult Activity() => Ok(new { ok = true });

    [HttpPost("password-reset/request"), EnableRateLimiting("auth")]
    public async Task<IActionResult> RequestReset(PasswordResetRequest r, CancellationToken ct) => Ok(new { message = await auth.RequestPasswordResetAsync(r.Mobile, ct) });

    [HttpPost("password-reset/confirm"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ConfirmReset(PasswordResetConfirm r, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(r, ct);
        return Ok(new { message = "Password updated. Please sign in." });
    }

    private IActionResult Respond(LoginResult result)
    {
        if (result.Tokens is { } t)
        {
            SetRefreshCookie(RefreshCookie, t.RefreshToken, t.RefreshTokenExpiresAt);
            return Ok(new { status = result.Status, accessToken = t.AccessToken, expiresAt = t.AccessTokenExpiresAt, recoveryCodes = result.RecoveryCodes });
        }
        return Ok(new { status = result.Status, challengeToken = result.ChallengeToken, message = result.Message });
    }

    internal void SetRefreshCookie(string name, string value, DateTime expires) => Response.Cookies.Append(name, value, CookieOptions(expires));

    internal CookieOptions CookieOptions(DateTime expires) => new()
    {
        HttpOnly = true, Secure = Request.IsHttps || !env.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = "/api/auth", Expires = expires, IsEssential = true
    };

    /// <summary>Cookie-authenticated endpoints require a custom header, which cross-site forms cannot send (CSRF defence).</summary>
    internal void RequireCsrfHeader()
    {
        if (Request.Headers[CsrfHeader] != "NIJMITRA") throw new Application.Common.ForbiddenException("Missing anti-forgery header.", "csrf");
    }
}

[ApiController]
[Authorize]
[Route("api/auth")]
public class SecurityController(AuthService auth, ICurrentUser current) : ControllerBase
{
    [HttpGet("sessions")] public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => auth.GetSessionsAsync(ct);

    [HttpDelete("sessions/{id:guid}"), DenyImpersonation]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct) { await auth.RevokeSessionAsync(id, ct); return NoContent(); }

    [HttpPost("sessions/revoke-others"), DenyImpersonation]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct) => Ok(new { revoked = await auth.RevokeOtherSessionsAsync(ct) });

    [HttpGet("devices")] public Task<IReadOnlyList<DeviceDto>> Devices(CancellationToken ct) => auth.GetDevicesAsync(ct);

    [HttpDelete("devices/{id:guid}"), DenyImpersonation]
    public async Task<IActionResult> RemoveDevice(Guid id, CancellationToken ct) { await auth.RemoveDeviceAsync(id, ct); return NoContent(); }

    [HttpGet("login-history")]
    public Task<Application.Common.PagedResult<LoginHistoryDto>> LoginHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => auth.GetLoginHistoryAsync(current.RequireUserId(), page, pageSize, ct);

    [HttpPost("2fa/setup"), DenyImpersonation] public Task<TwoFactorSetupDto> Setup(CancellationToken ct) => auth.BeginTwoFactorSetupAsync(ct);

    [HttpPost("2fa/enable"), DenyImpersonation]
    public async Task<RecoveryCodesDto> Enable(CodeRequest r, CancellationToken ct) => new(await auth.EnableTwoFactorAsync(r.Code, ct));

    [HttpPost("2fa/disable"), DenyImpersonation]
    public async Task<IActionResult> Disable(DisableTwoFactorRequest r, CancellationToken ct) { await auth.DisableTwoFactorAsync(r, ct); return NoContent(); }

    [HttpPost("2fa/recovery-codes"), DenyImpersonation]
    public async Task<RecoveryCodesDto> RecoveryCodes(CodeRequest r, CancellationToken ct) => new(await auth.RegenerateRecoveryCodesAsync(r.Code, ct));

    [HttpPost("change-password"), DenyImpersonation]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest r, CancellationToken ct) { await auth.ChangePasswordAsync(r, ct); return NoContent(); }

    [HttpPost("change-pin"), DenyImpersonation]
    public async Task<IActionResult> ChangePin(ChangePinRequest r, CancellationToken ct) { await auth.ChangePinAsync(r, ct); return NoContent(); }

    [HttpPost("reauthenticate"), DenyImpersonation, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Reauthenticate(ReauthRequest r, CancellationToken ct) { await auth.ReauthenticateAsync(r, ct); return NoContent(); }

    [HttpPost("email"), DenyImpersonation]
    public async Task<IActionResult> UpdateEmail(EmailUpdateRequest r, CancellationToken ct) => Ok(new { message = await auth.UpdateEmailAsync(r.Email, ct) });

    [HttpPost("email/verify"), DenyImpersonation]
    public async Task<IActionResult> VerifyEmail(EmailVerifyRequest r, CancellationToken ct) { await auth.VerifyEmailAsync(r.Code, ct); return NoContent(); }

    [HttpPost("email/resend"), DenyImpersonation, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendEmail(CancellationToken ct) => Ok(new { message = await auth.ResendEmailVerificationAsync(ct) });
}
