using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using NIJMITRA.API.Hubs;
using NIJMITRA.API.Infrastructure;
using NIJMITRA.Application;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Domain.Common;
using NIJMITRA.Infrastructure;
using NIJMITRA.Infrastructure.Persistence;
using NIJMITRA.Infrastructure.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration: appsettings → appsettings.{Env} → appsettings.Local.json (git-ignored) → NIJMITRA_* env vars
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables(prefix: "NIJMITRA_");
NIJMITRA.Infrastructure.DependencyInjection.EnsureDevelopmentSecrets(builder.Configuration, builder.Environment);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(ctx.HostingEnvironment.ContentRootPath, "logs", "nijmitra-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

// ---------- Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserService>());
builder.Services.AddSingleton<IRealtimeNotifier, RealtimeNotifier>();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
NIJMITRA.API.BackgroundJobs.JobRegistration.Register(builder.Services, builder.Configuration);

builder.Services.AddControllers(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.ProducesAttribute("application/json")))
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = ctx =>
{
    var errors = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0)
        .ToDictionary(kv => char.ToLowerInvariant(kv.Key.FirstOrDefault()) + kv.Key[Math.Min(1, kv.Key.Length)..], kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());
    return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { status = 400, title = "Please correct the highlighted fields.", code = "validation_failed", errors });
});
builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 64 * 1024; o.KeepAliveInterval = TimeSpan.FromSeconds(15); o.ClientTimeoutInterval = TimeSpan.FromSeconds(45); })
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = jwt.Key(), ValidateIssuerSigningKey = true,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "name", RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(o =>
{
    foreach (var p in Permissions.All) o.AddPolicy(HasPermissionAttribute.PolicyName(p), pb => pb.RequireAuthenticatedUser().RequireClaim(AppClaims.Permission, p));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, code = "rate_limited", title = "Too many requests. Please slow down and try again shortly." }, ct);
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("refresh", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:GlobalPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("Content-Disposition")));
builder.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; });
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 110L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 110L * 1024 * 1024);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "NIJMITRA API", Version = "v1", Description = "Personal Digital Life Management Platform" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme { Type = Microsoft.OpenApi.SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
});
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------- CLI utilities: dotnet NIJMITRA.API.dll --migrate | --promote-admin <mobile>
if (args.Contains("--migrate") || args.Contains("--promote-admin"))
{
    await NIJMITRA.API.Infrastructure.Cli.RunAsync(app, args);
    return;
}

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db, app.Logger);
}

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(o =>
{
    // Never log share-link tokens or query strings.
    o.MessageTemplate = "HTTP {RequestMethod} {SafePath} responded {StatusCode} in {Elapsed:0} ms";
    o.EnrichDiagnosticContext = (diag, http) => diag.Set("SafePath", http.Request.Path.StartsWithSegments("/api/public") ? "/api/public/[redacted]" : http.Request.Path.Value);
});
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.DocumentTitle = "NIJMITRA API");
}

// Optional: serve the built PWA (copy NIJMITRA.Web/dist into wwwroot for single-site hosting).
var webRoot = app.Environment.WebRootPath;
var hasSpa = webRoot != null && File.Exists(Path.Combine(webRoot, "index.html"));
if (hasSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = c =>
        {
            var name = c.File.Name;
            c.Context.Response.Headers.CacheControl = name is "index.html" or "sw.js" or "manifest.webmanifest" or "registerSW.js"
                ? "no-cache" : c.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "public, max-age=86400";
        }
    });
}

app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AppHub>("/hubs/app");
app.MapHealthChecks("/health");
if (hasSpa) app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
