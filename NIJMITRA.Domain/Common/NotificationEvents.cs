namespace NIJMITRA.Domain.Common;

/// <summary>Stable keys for every notification event the platform can emit.</summary>
public static class NotificationEvents
{
    public const string RegistrationOtp = "registration_otp";
    public const string EmailVerification = "email_verification";
    public const string PasswordReset = "password_reset";
    public const string Welcome = "welcome";
    public const string LoginAlert = "login_alert";
    public const string SecurityChange = "security_change";
    public const string MeetingReminder = "meeting_reminder";
    public const string ExpenseReminder = "expense_reminder";
    public const string BudgetAlert = "budget_alert";
    public const string UdharReminder = "udhar_reminder";
    public const string GeneralReminder = "general_reminder";
    public const string FriendRequest = "friend_request";
    public const string ChatMessage = "chat_message";
    public const string AccountSuspension = "account_suspension";
    public const string Announcement = "announcement";
    public const string AdminAccess = "admin_access";

    public static readonly string[] All =
    [
        RegistrationOtp, EmailVerification, PasswordReset, Welcome, LoginAlert, SecurityChange,
        MeetingReminder, ExpenseReminder, BudgetAlert, UdharReminder, GeneralReminder,
        FriendRequest, ChatMessage, AccountSuspension, Announcement, AdminAccess
    ];
}

public static class Permissions
{
    public const string DashboardView = "admin.dashboard.view";
    public const string UsersView = "admin.users.view";
    public const string UsersManage = "admin.users.manage";
    public const string UsersImpersonate = "admin.users.impersonate";
    public const string RolesManage = "admin.roles.manage";
    public const string SettingsManage = "admin.settings.manage";
    public const string DatabaseManage = "admin.database.manage";
    public const string CommunicationManage = "admin.communication.manage";
    public const string CommunicationLogsView = "admin.communication.logs";
    public const string AnnouncementsSend = "admin.announcements.send";
    public const string AuditView = "admin.audit.view";
    public const string ReportsManage = "admin.reports.manage";

    public static readonly string[] All =
    [
        DashboardView, UsersView, UsersManage, UsersImpersonate, RolesManage, SettingsManage, DatabaseManage,
        CommunicationManage, CommunicationLogsView, AnnouncementsSend, AuditView, ReportsManage
    ];
}

public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string SupportAdmin = "SupportAdmin";
}
