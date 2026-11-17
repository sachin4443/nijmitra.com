using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Account;
using NIJMITRA.Application.Auth;
using NIJMITRA.Application.Common;
using NIJMITRA.Application.Files;
using NIJMITRA.Application.Notifications;

namespace NIJMITRA.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AppOptions>(config.GetSection("App"));
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<FileService>();
        // Module services are registered by convention: every concrete *Service in the Application assembly.
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Service") && t.GetInterfaces().Length == 0
                                 && t.Namespace != null && !t.Namespace.EndsWith(".Common")))
            services.AddScoped(type);
        return services;
    }
}
