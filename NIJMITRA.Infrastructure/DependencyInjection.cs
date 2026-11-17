using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Infrastructure.Notifications;
using NIJMITRA.Infrastructure.Persistence;
using NIJMITRA.Infrastructure.Security;
using NIJMITRA.Infrastructure.Services;
using NIJMITRA.Infrastructure.Storage;

namespace NIJMITRA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.Configure<SecurityOptions>(config.GetSection("Security"));
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<IFieldEncryptor, FieldEncryptor>();
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IUserAgentParser, UserAgentParserService>();
        services.AddSingleton<IHtmlSanitizerService, HtmlSanitizerService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        if (string.Equals(config["Storage:MalwareScanner"], "clamav", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMalwareScanner, ClamAvMalwareScanner>();
        else
            services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();

        services.AddSingleton<IDatabaseConnectionStore, DatabaseConnectionStore>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(sp.GetRequiredService<IDatabaseConnectionStore>().ConnectionString,
                    npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName).EnableRetryOnFailure(3))
                .UseSnakeCaseNamingConvention()
                // Child rows (versions, values, payments…) are always reached through their owner-filtered parent.
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
            if (env.IsDevelopment()) options.EnableDetailedErrors();
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddHttpClient("providers", c => { c.Timeout = TimeSpan.FromSeconds(20); c.DefaultRequestHeaders.UserAgent.ParseAdd("NIJMITRA/1.0"); });
        services.AddSingleton<IChannelProvider, SmtpEmailProvider>();
        services.AddSingleton<IChannelProvider, SendGridEmailProvider>();
        services.AddSingleton<IChannelProvider, AmazonSesEmailProvider>();
        services.AddSingleton<IChannelProvider, TwoFactorSmsProvider>();
        services.AddSingleton<IChannelProvider, Msg91SmsProvider>();
        services.AddSingleton<IChannelProvider, TwilioSmsProvider>();
        services.AddSingleton<IChannelProvider, CustomHttpSmsProvider>();
        services.AddSingleton<IChannelProvider, WebPushProvider>();
        services.AddSingleton<IChannelProvider, WhatsAppCloudProvider>();
        services.AddScoped<ProviderResolver>();
        services.AddScoped<IProviderGateway, ProviderGateway>();
        services.AddSingleton<NotificationDispatcher>();
        if (config.GetValue("BackgroundJobs:Enabled", true))
            services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        return services;
    }

    /// <summary>
    /// Development convenience only: creates random JWT/encryption keys in App_Data/dev-secrets.json (git-ignored)
    /// when none are configured. Production must supply keys through environment variables.
    /// </summary>
    public static void EnsureDevelopmentSecrets(ConfigurationManager config, IHostEnvironment env)
    {
        if (!string.IsNullOrEmpty(config["Jwt:SigningKey"]) && !string.IsNullOrEmpty(config["Security:DataEncryptionKey"])) return;
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
            throw new InvalidOperationException("Jwt:SigningKey and Security:DataEncryptionKey must be configured (environment variables NIJMITRA_Jwt__SigningKey and NIJMITRA_Security__DataEncryptionKey).");
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "dev-secrets.json");
        Dictionary<string, string> values;
        if (File.Exists(file)) values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        else
        {
            values = new()
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                ["Security:DataEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
            File.WriteAllText(file, JsonSerializer.Serialize(values));
        }
        config.AddInMemoryCollection(values.Where(kv => string.IsNullOrEmpty(config[kv.Key])).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }
}
