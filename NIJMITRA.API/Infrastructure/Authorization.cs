using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NIJMITRA.Application.Abstractions;

namespace NIJMITRA.API.Infrastructure;

/// <summary>Requires a granular RBAC permission (claim "perm").</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PolicyName(permission))
{
    public static string PolicyName(string permission) => "perm:" + permission;
}

/// <summary>Rejects requests from sessions where an administrator is signed in as the user.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DenyImpersonationAttribute : Attribute, IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var current = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (current.IsImpersonating)
        {
            context.Result = new ObjectResult(new { status = 403, code = "impersonation_blocked", title = "This area is private to the account owner and is not available while signed in as the user." }) { StatusCode = 403 };
            return Task.CompletedTask;
        }
        return next();
    }
}

public static class ControllerExtensions
{
    public static async Task<Application.Files.UploadInput> ToUploadAsync(this IFormFile file) =>
        new(file.OpenReadStream(), file.FileName, file.Length);
}
