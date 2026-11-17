using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Application.Abstractions;

public interface IAppDbContext
{
    // Identity & administration
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<UserSecuritySettings> UserSecuritySettings { get; }
    DbSet<UserAuthenticatorSecret> UserAuthenticatorSecrets { get; }
    DbSet<UserRecoveryCode> UserRecoveryCodes { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<LoginAuditLog> LoginAuditLogs { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AdminAuditLog> AdminAuditLogs { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<AccountDeletionRequest> AccountDeletionRequests { get; }
    DbSet<RecentSearch> RecentSearches { get; }
    DbSet<StoredFile> StoredFiles { get; }

    // Finance
    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<IncomeRecord> IncomeRecords { get; }
    DbSet<MonthlyBudget> MonthlyBudgets { get; }
    DbSet<BudgetCategory> BudgetCategories { get; }
    DbSet<RecurringExpense> RecurringExpenses { get; }
    DbSet<RecurringExpenseOccurrence> RecurringExpenseOccurrences { get; }

    // Documents
    DbSet<DocumentFolder> DocumentFolders { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<DocumentTag> DocumentTags { get; }
    DbSet<DocumentShareLink> DocumentShareLinks { get; }

    // Password vault
    DbSet<PasswordVault> PasswordVaults { get; }
    DbSet<PasswordEntry> PasswordEntries { get; }
    DbSet<PasswordHistory> PasswordHistory { get; }
    DbSet<SecureNote> SecureNotes { get; }

    // Registers
    DbSet<CustomTable> CustomTables { get; }
    DbSet<CustomTableColumn> CustomTableColumns { get; }
    DbSet<CustomTableRecord> CustomTableRecords { get; }
    DbSet<CustomTableValue> CustomTableValues { get; }
    DbSet<CustomTableAttachment> CustomTableAttachments { get; }
    DbSet<ProductRegister> ProductRegisters { get; }
    DbSet<ProductCustomField> ProductCustomFields { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductAttachment> ProductAttachments { get; }

    // People & money
    DbSet<ContactGroup> ContactGroups { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<ContactAttachment> ContactAttachments { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }
    DbSet<CreditPayment> CreditPayments { get; }
    DbSet<CreditReminder> CreditReminders { get; }
    DbSet<CreditAttachment> CreditAttachments { get; }

    // Journal & calendar
    DbSet<DiaryEntry> DiaryEntries { get; }
    DbSet<DiaryTag> DiaryTags { get; }
    DbSet<DiaryAttachment> DiaryAttachments { get; }
    DbSet<CalendarEvent> CalendarEvents { get; }
    DbSet<CalendarEventAttachment> CalendarEventAttachments { get; }
    DbSet<Reminder> Reminders { get; }
    DbSet<ReminderSchedule> ReminderSchedules { get; }

    // Notifications & communication
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<NotificationPolicy> NotificationPolicies { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationQueueItem> NotificationQueue { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<CommunicationProvider> CommunicationProviders { get; }
    DbSet<CommunicationLog> CommunicationLogs { get; }
    DbSet<Announcement> Announcements { get; }

    // Friends & chat
    DbSet<FriendInvitation> FriendInvitations { get; }
    DbSet<FriendRequest> FriendRequests { get; }
    DbSet<FriendConnection> FriendConnections { get; }
    DbSet<ChatConversation> ChatConversations { get; }
    DbSet<ChatParticipant> ChatParticipants { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<ChatAttachment> ChatAttachments { get; }
    DbSet<UserPresence> UserPresences { get; }
    DbSet<BlockedUser> BlockedUsers { get; }
    DbSet<UserReport> UserReports { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
