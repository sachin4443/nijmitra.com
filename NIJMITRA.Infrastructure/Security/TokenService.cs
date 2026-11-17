using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Infrastructure.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = "https://nijmitra.com";
    public string Audience { get; set; } = "nijmitra-app";
    /// <summary>Base64 HMAC-SHA256 signing key (≥ 32 bytes). Supplied via env / user-secrets only.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 10;

    public SymmetricSecurityKey Key()
    {
        if (string.IsNullOrWhiteSpace(SigningKey)) throw new InvalidOperationException("Jwt:SigningKey is not configured. Set NIJMITRA_Jwt__SigningKey.");
        var bytes = Convert.FromBase64String(SigningKey);
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}

public static class AppClaims
{
    public const string SessionId = "sid";
    public const string Permission = "perm";
    public const string Impersonator = "imp";
    public const string PublicId = "pid";
    public const string Purpose = "purpose";
    public const string Stamp = "stamp";
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessTokenResult CreateAccessToken(User user, UserSession session, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(Math.Clamp(o.AccessTokenMinutes, 1, 60));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(AppClaims.SessionId, session.Id.ToString()),
            new(AppClaims.PublicId, user.PublicId),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (session.ImpersonatedByUserId is { } imp) claims.Add(new Claim(AppClaims.Impersonator, imp.ToString()));
        // Impersonated sessions never carry administrative rights.
        if (session.ImpersonatedByUserId == null)
        {
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim(AppClaims.Permission, p)));
        }
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer, Audience = o.Audience, Subject = new ClaimsIdentity(claims), IssuedAt = now, NotBefore = now, Expires = expires,
            SigningCredentials = new SigningCredentials(o.Key(), SecurityAlgorithms.HmacSha256)
        });
        return new AccessTokenResult(token, expires);
    }

    public string CreateOpaqueToken(int bytes = 48) => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));

    public string CreateChallengeToken(Guid userId, string purpose, string securityStamp, TimeSpan lifetime)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer, Audience = o.Audience + ":challenge", IssuedAt = now, NotBefore = now, Expires = now.Add(lifetime),
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(AppClaims.Purpose, purpose), new Claim(AppClaims.Stamp, securityStamp)]),
            SigningCredentials = new SigningCredentials(o.Key(), SecurityAlgorithms.HmacSha256)
        });
    }

    public (Guid UserId, string Stamp)? ValidateChallengeToken(string token, string purpose)
    {
        var o = options.Value;
        var result = _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = o.Issuer, ValidAudience = o.Audience + ":challenge", IssuerSigningKey = o.Key(),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10), LifetimeValidator = (nb, exp, _, _) => exp > clock.GetUtcNow().UtcDateTime
        }).GetAwaiter().GetResult();
        if (!result.IsValid) return null;
        if (!result.Claims.TryGetValue(AppClaims.Purpose, out var p) || (string)p != purpose) return null;
        if (!Guid.TryParse(result.Claims[JwtRegisteredClaimNames.Sub]?.ToString(), out var uid)) return null;
        return (uid, result.Claims.TryGetValue(AppClaims.Stamp, out var st) ? st.ToString()! : "");
    }
}
