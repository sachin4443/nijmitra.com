using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Infrastructure.Persistence;

/// <summary>Idempotent seeding of RBAC, notification policies and default templates (never user data).</summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Permissions
        var descriptions = new Dictionary<string, string>
        {
            [Permissions.DashboardView] = "View admin dashboard and statistics",
            [Permissions.UsersView] = "View users, login history and security status",
            [Permissions.UsersManage] = "Activate, suspend, unlock users and reset passwords",
            [Permissions.UsersImpersonate] = "Sign in as a user (audited)",
            [Permissions.RolesManage] = "Manage roles and permissions",
            [Permissions.SettingsManage] = "Manage platform settings and branding",
            [Permissions.DatabaseManage] = "Change the database connection",
            [Permissions.CommunicationManage] = "Manage providers, templates and notification policies",
            [Permissions.CommunicationLogsView] = "View communication logs",
            [Permissions.AnnouncementsSend] = "Send announcements",
            [Permissions.AuditView] = "View audit logs",
            [Permissions.ReportsManage] = "Review user reports",
        };
        var perms = await db.Permissions.ToDictionaryAsync(p => p.Key, ct);
        foreach (var key in Permissions.All.Where(k => !perms.ContainsKey(k)))
        {
            var p = new Permission { Key = key, Description = descriptions.GetValueOrDefault(key) };
            db.Permissions.Add(p); perms[key] = p;
        }
        await db.SaveChangesAsync(ct);

        async Task EnsureRole(string name, string description, IEnumerable<string> keys)
        {
            var role = await db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Name == name, ct);
            if (role != null) return; // never overwrite admin customisations
            role = new Role { Name = name, Description = description, IsSystem = true };
            foreach (var k in keys) role.RolePermissions.Add(new RolePermission { PermissionId = perms[k].Id });
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);
        }
        await EnsureRole(RoleNames.SuperAdmin, "Full platform control", Permissions.All);
        await EnsureRole(RoleNames.Admin, "Platform administration", Permissions.All.Except([Permissions.DatabaseManage, Permissions.RolesManage, Permissions.UsersImpersonate]));
        await EnsureRole(RoleNames.SupportAdmin, "User support (read-mostly)",
            [Permissions.DashboardView, Permissions.UsersView, Permissions.CommunicationLogsView, Permissions.AuditView, Permissions.ReportsManage]);

        // Notification control matrix (defaults from the product specification)
        var policies = new List<NotificationPolicy>
        {
            P(NotificationEvents.RegistrationOtp, "Registration OTP", email: true, sms: true, push: false, critical: true),
            P(NotificationEvents.EmailVerification, "Email Verification", email: true, sms: false, push: false, critical: true),
            P(NotificationEvents.PasswordReset, "Password Reset", email: true, sms: true, push: false, critical: true),
            P(NotificationEvents.Welcome, "Welcome", email: true, sms: false, push: false),
            P(NotificationEvents.LoginAlert, "Login Alert", email: true, sms: false, push: true),
            P(NotificationEvents.SecurityChange, "Security Change", email: true, sms: false, push: true, critical: true),
            P(NotificationEvents.MeetingReminder, "Meeting Reminder", email: true, sms: false, push: true),
            P(NotificationEvents.ExpenseReminder, "Expense / Bill Reminder", email: true, sms: false, push: true),
            P(NotificationEvents.BudgetAlert, "Budget Overspending Alert", email: true, sms: false, push: true),
            P(NotificationEvents.UdharReminder, "Udhar Reminder", email: true, sms: true, push: true),
            P(NotificationEvents.GeneralReminder, "General Reminder", email: true, sms: false, push: true),
            P(NotificationEvents.FriendRequest, "Friend Request", email: true, sms: false, push: true),
            P(NotificationEvents.ChatMessage, "New Chat Message", email: false, sms: false, push: true),
            P(NotificationEvents.AccountSuspension, "Account Suspension", email: true, sms: true, push: false, critical: true),
            P(NotificationEvents.Announcement, "Announcement", email: true, sms: false, push: true),
            P(NotificationEvents.AdminAccess, "Administrator Account Access", email: true, sms: false, push: true, critical: true),
        };
        var existing = await db.NotificationPolicies.Select(p => p.EventType).ToListAsync(ct);
        db.NotificationPolicies.AddRange(policies.Where(p => !existing.Contains(p.EventType)));
        await db.SaveChangesAsync(ct);

        // Default templates
        var existingTpl = await db.NotificationTemplates.Select(t => new { t.EventType, t.Channel }).ToListAsync(ct);
        foreach (var t in DefaultTemplates(now).Where(t => !existingTpl.Any(e => e.EventType == t.EventType && e.Channel == t.Channel)))
            db.NotificationTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reference data seeded");
    }

    private static NotificationPolicy P(string evt, string name, bool email, bool sms, bool push, bool critical = false) => new()
    {
        EventType = evt, DisplayName = name, InAppEnabled = true, EmailEnabled = email, SmsEnabled = sms, PushEnabled = push,
        IsSecurityCritical = critical, UserCanOptOut = !critical, UpdatedAt = DateTime.UtcNow
    };

    private static IEnumerable<NotificationTemplate> DefaultTemplates(DateTime now)
    {
        NotificationTemplate E(string evt, string subject, string body) => new() { EventType = evt, Channel = NotificationChannel.Email, Subject = subject, Body = body, UpdatedAt = now };
        NotificationTemplate S(string evt, string body) => new() { EventType = evt, Channel = NotificationChannel.Sms, Body = body, UpdatedAt = now };
        NotificationTemplate Pu(string evt, string title, string body) => new() { EventType = evt, Channel = NotificationChannel.Push, Subject = title, Body = body, UpdatedAt = now };

        yield return E(NotificationEvents.RegistrationOtp, "{{OTP}} is your {{ApplicationName}} verification code",
            "<p>Hello,</p><p>Your {{ApplicationName}} verification code is:</p><p style=\"font-size:28px;font-weight:700;letter-spacing:6px;color:#4F46E5\">{{OTP}}</p><p>This code expires in {{ExpiryMinutes}} minutes. Never share it with anyone.</p>");
        yield return S(NotificationEvents.RegistrationOtp, "{{OTP}} is your {{ApplicationName}} verification code. Valid for {{ExpiryMinutes}} minutes. Do not share it with anyone.");
        yield return E(NotificationEvents.EmailVerification, "Verify your email for {{ApplicationName}}",
            "<p>Hello {{UserName}},</p><p>Use this code to verify your email address:</p><p style=\"font-size:28px;font-weight:700;letter-spacing:6px;color:#4F46E5\">{{OTP}}</p><p>The code expires in {{ExpiryMinutes}} minutes.</p>");
        yield return E(NotificationEvents.PasswordReset, "Reset your {{ApplicationName}} password",
            "<p>Hello {{UserName}},</p><p>We received a request to reset your password. Your code is:</p><p style=\"font-size:28px;font-weight:700;letter-spacing:6px;color:#4F46E5\">{{OTP}}</p><p>If you did not request this, you can ignore this email — your password will not change.</p>");
        yield return S(NotificationEvents.PasswordReset, "{{OTP}} is your {{ApplicationName}} password reset code. Valid for {{ExpiryMinutes}} minutes. Do not share it.");
        yield return E(NotificationEvents.Welcome, "Welcome to {{ApplicationName}}",
            "<p>Hello {{UserName}},</p><p>Welcome to <b>{{ApplicationName}}</b> — your personal digital companion for money, documents, passwords, contacts, diary and more.</p><p>For extra protection, enable two-step verification from <b>Settings → Security</b>.</p><p><a href=\"{{Link}}\" style=\"background:#4F46E5;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none\">Open {{ApplicationName}}</a></p>");
        yield return E(NotificationEvents.LoginAlert, "New sign-in to your {{ApplicationName}} account",
            "<p>Hello {{UserName}},</p><p>Your account was just signed in to.</p><ul><li>Browser: {{Browser}}</li><li>IP address: {{IpAddress}}</li><li>Time: {{EventDate}} {{EventTime}}</li></ul><p>If this wasn't you, change your password immediately and sign out other devices from Settings → Security.</p>");
        yield return Pu(NotificationEvents.LoginAlert, "New sign-in", "{{Body}}");
        yield return E(NotificationEvents.SecurityChange, "{{Title}}", "<p>Hello {{UserName}},</p><p>{{Body}}</p><p>If you did not make this change, contact support immediately.</p>");
        yield return E(NotificationEvents.MeetingReminder, "Reminder: {{Title}}", "<p>Hello {{UserName}},</p><p><b>{{Title}}</b></p><p>{{Body}}</p><p>{{EventDate}} {{EventTime}}</p>");
        yield return Pu(NotificationEvents.MeetingReminder, "{{Title}}", "{{Body}}");
        yield return E(NotificationEvents.ExpenseReminder, "Bill reminder: {{Title}}", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
        yield return Pu(NotificationEvents.ExpenseReminder, "Bill reminder", "{{Body}}");
        yield return E(NotificationEvents.BudgetAlert, "Budget alert", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
        yield return E(NotificationEvents.UdharReminder, "Udhar reminder: {{Title}}", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
        yield return S(NotificationEvents.UdharReminder, "{{ApplicationName}}: {{Body}}");
        yield return Pu(NotificationEvents.UdharReminder, "Udhar reminder", "{{Body}}");
        yield return E(NotificationEvents.GeneralReminder, "Reminder: {{Title}}", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
        yield return E(NotificationEvents.FriendRequest, "New friend request on {{ApplicationName}}", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
        yield return Pu(NotificationEvents.ChatMessage, "{{Title}}", "{{Body}}");
        yield return E(NotificationEvents.AccountSuspension, "Your {{ApplicationName}} account status", "<p>Hello {{UserName}},</p><p>{{Body}}</p><p>Contact support if you believe this is a mistake.</p>");
        yield return S(NotificationEvents.AccountSuspension, "{{ApplicationName}}: {{Body}}");
        yield return E(NotificationEvents.Announcement, "{{Title}}", "<p>Hello {{UserName}},</p>{{Body}}");
        yield return E(NotificationEvents.AdminAccess, "Your account was accessed by support", "<p>Hello {{UserName}},</p><p>{{Body}}</p>");
    }
}
