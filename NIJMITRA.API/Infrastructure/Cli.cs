using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Infrastructure.Persistence;

namespace NIJMITRA.API.Infrastructure;

/// <summary>Operational commands for deployment scripts.</summary>
public static class Cli
{
    public static async Task RunAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (args.Contains("--migrate"))
        {
            await db.Database.MigrateAsync();
            await DataSeeder.SeedAsync(db, app.Logger);
            Console.WriteLine("Database migrated and reference data seeded.");
        }
        var idx = Array.IndexOf(args, "--promote-admin");
        if (idx >= 0)
        {
            if (idx + 1 >= args.Length) { Console.Error.WriteLine("Usage: --promote-admin <mobile>"); return; }
            var mobile = TextUtil.NormalizeMobile(args[idx + 1]);
            var user = await db.Users.FirstOrDefaultAsync(u => u.MobileNumber == mobile);
            if (user == null) { Console.Error.WriteLine($"No user registered with mobile {TextUtil.MaskMobile(mobile)}. Register first."); return; }
            var role = await db.Roles.FirstAsync(r => r.Name == RoleNames.SuperAdmin);
            if (!await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == role.Id))
            {
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            Console.WriteLine($"{user.FullName} ({user.PublicId}) is now a Super Admin.");
        }
    }
}
