using System.Security.Claims;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Infrastructure.Security;

namespace NIJMITRA.API.Infrastructure;

/// <summary>Reads the caller identity exclusively from the validated JWT. Background jobs can scope an explicit user.</summary>
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUser
{
    private Guid? _override;

    public void RunAs(Guid userId) => _override = userId;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => _override ?? (Guid.TryParse(Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null);
    public Guid? SessionId => _override != null ? null : Guid.TryParse(Principal?.FindFirstValue(AppClaims.SessionId), out var id) ? id : null;
    public Guid? ImpersonatorId => _override != null ? null : Guid.TryParse(Principal?.FindFirstValue(AppClaims.Impersonator), out var id) ? id : null;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}
