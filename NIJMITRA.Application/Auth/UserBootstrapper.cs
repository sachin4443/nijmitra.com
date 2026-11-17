using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Auth;

/// <summary>Creates the per-user default records (profile, preferences, categories, groups).</summary>
public static class UserBootstrapper
{
    public static readonly (string Name, string Icon, string Color)[] ExpenseCategories =
    [
        ("Food", "utensils", "#F97316"), ("Grocery", "basket", "#22C55E"), ("Shopping", "bag", "#EC4899"),
        ("Travel", "plane", "#0EA5E9"), ("Petrol", "fuel", "#EF4444"), ("Electricity", "bolt", "#EAB308"),
        ("Mobile", "phone", "#8B5CF6"), ("Internet", "wifi", "#06B6D4"), ("Rent", "home", "#6366F1"),
        ("Medical", "heart", "#F43F5E"), ("Education", "book", "#14B8A6"), ("Entertainment", "film", "#A855F7"),
        ("Other", "dots", "#64748B")
    ];

    public static readonly string[] IncomeSources = ["Salary", "Business Income", "Freelancing", "Interest", "Other Income"];
    public static readonly string[] ContactGroups = ["Family", "Friends", "Business", "Customers", "Suppliers", "Other"];

    public static void Seed(IAppDbContext db, User user, DateTime now)
    {
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, UpdatedAt = now });
        db.UserSecuritySettings.Add(new UserSecuritySettings { UserId = user.Id, UpdatedAt = now, PinChangedAt = user.PinHash != null ? now : null });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, UpdatedAt = now });
        foreach (var (name, icon, color) in ExpenseCategories)
            db.ExpenseCategories.Add(new ExpenseCategory { UserId = user.Id, Name = name, Icon = icon, Color = color, IsDefault = true, Type = CategoryType.Expense });
        foreach (var g in ContactGroups)
            db.ContactGroups.Add(new ContactGroup { UserId = user.Id, Name = g });
        db.UserPresences.Add(new UserPresence { UserId = user.Id, LastSeenAt = now, LastHeartbeatAt = now });
    }

    public static async Task EnsureBootstrapAdminAsync(IAppDbContext db, User user, IEnumerable<string> bootstrapMobiles, CancellationToken ct)
    {
        if (!bootstrapMobiles.Select(Common.TextUtil.NormalizeMobile).Contains(user.MobileNumber)) return;
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == Domain.Common.RoleNames.SuperAdmin, ct);
        if (role == null) return;
        if (!await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == role.Id, ct))
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
    }
}
