using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Enums;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.Infrastructure;

/// <summary>Converts exceptions into RFC 7807 problem responses without leaking internals.</summary>
public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Client went away (navigation, closed tab) — not a server error.
            if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 499;
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            var (status, code, title, errors) = ex switch
            {
                ValidationAppException v => (400, v.Code, v.Message, v.Errors),
                NotFoundException n => (404, n.Code, n.Message, null),
                ForbiddenException f => (403, f.Code, f.Message, null),
                ReauthenticationRequiredException r => (403, r.Code, r.Message, null),
                ConflictException c => (409, c.Code, c.Message, null),
                UnauthorizedAppException u => (401, u.Code, u.Message, null),
                DbUpdateConcurrencyException => (409, "concurrency", "The record was changed by someone else. Reload and try again.", null),
                OperationCanceledException when ctx.RequestAborted.IsCancellationRequested => (499, "cancelled", "Request cancelled.", null),
                BadHttpRequestException b => (b.StatusCode, "bad_request", "The request was invalid.", null),
                _ => (500, "server_error", "Something went wrong. Please try again.", (IDictionary<string, string[]>?)null)
            };
            if (status >= 500) logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            var problem = new ProblemDetails { Status = status, Title = title, Type = $"https://nijmitra.com/errors/{code}" };
            problem.Extensions["code"] = code;
            problem.Extensions["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier;
            if (errors is { Count: > 0 }) problem.Extensions["errors"] = errors;
            if (status == 500 && env.IsDevelopment()) problem.Detail = ex.Message;
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsJsonAsync(problem);
        }
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(self), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            h["Cache-Control"] = "no-store";
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }
        else
        {
            h["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
                "connect-src 'self' wss: https:; media-src 'self' blob:; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; worker-src 'self'; manifest-src 'self'";
        }
        return next(ctx);
    }
}

/// <summary>
/// Server-side session enforcement on every authenticated request: revoked / expired sessions are rejected,
/// sessions idle for longer than the administrator's timeout are terminated (refresh tokens revoked), and
/// genuine user activity extends the session. Requests marked "X-Activity: passive" (polling, heartbeats)
/// do not count as activity.
/// </summary>
public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ICurrentUser current, AppDbContext db, ISettingsService settings, AuthService auth, TimeProvider clock)
    {
        if (current.SessionId is not { } sid || current.UserId is not { } uid) { await next(ctx); return; }
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.UserSessions.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == sid && s.UserId == uid);
        if (session == null || !session.IsActive(now))
        {
            await Reject(ctx, "session_revoked", "Your session has ended. Please sign in again.");
            return;
        }
        if (session.User!.Status != UserStatus.Active)
        {
            await auth.ExpireSessionAsync(session, "account_inactive", ctx.RequestAborted);
            await Reject(ctx, "account_inactive", "Your account is not active.");
            return;
        }
        var s = await settings.GetAsync();
        var idle = (now - session.LastActivityAt).TotalSeconds;
        if (idle > Math.Max(30, s.SessionIdleTimeoutSeconds) + 5)
        {
            await auth.ExpireSessionAsync(session, "idle_timeout", ctx.RequestAborted);
            await Reject(ctx, "session_expired", "You were signed out after a period of inactivity.");
            return;
        }
        var passive = string.Equals(ctx.Request.Headers["X-Activity"], "passive", StringComparison.OrdinalIgnoreCase);
        if (!passive && idle > 2)
        {
            var ids = session.ParentSessionId is { } parent ? new[] { sid, parent } : [sid];
            await db.UserSessions.Where(x => ids.Contains(x.Id) && x.RevokedAt == null)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastActivityAt, now));
        }
        db.ChangeTracker.Clear();
        await next(ctx);
    }

    private static async Task Reject(HttpContext ctx, string code, string message)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new { status = 401, title = message, code });
    }
}

/// <summary>Blocks non-admin traffic while maintenance mode is on.</summary>
public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    private static readonly string[] AlwaysAllowed = ["/api/auth", "/api/admin", "/api/system", "/api/me", "/health", "/hubs"];

    public async Task InvokeAsync(HttpContext ctx, ISettingsService settings)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api") && !AlwaysAllowed.Any(p => path.StartsWithSegments(p)))
        {
            var s = await settings.GetAsync();
            if (s.MaintenanceMode && !ctx.User.HasClaim(c => c.Type == "perm"))
            {
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsJsonAsync(new { status = 503, title = s.MaintenanceMessage, code = "maintenance" });
                return;
            }
        }
        await next(ctx);
    }
}
