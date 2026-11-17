using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Domain.Common;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, TimeProvider clock)
    : DbContext(options), IAppDbContext
{
    public const string OwnerFilter = "Owner";
    public const string SoftDeleteFilter = "SoftDelete";

    /// <summary>The owner id used by the global "Owner" query filter (null ⇒ no rows match).</summary>
    private Guid? OwnerFilterId => currentUser.UserId;

    /// <summary>When true, Remove() performs a physical delete instead of a soft delete (used by purge jobs).</summary>
    public bool HardDeleteMode { get; set; }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserSecuritySettings> UserSecuritySettings => Set<UserSecuritySettings>();
    public DbSet<UserAuthenticatorSecret> UserAuthenticatorSecrets => Set<UserAuthenticatorSecret>();
    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AccountDeletionRequest> AccountDeletionRequests => Set<AccountDeletionRequest>();
    public DbSet<RecentSearch> RecentSearches => Set<RecentSearch>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<IncomeRecord> IncomeRecords => Set<IncomeRecord>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<RecurringExpenseOccurrence> RecurringExpenseOccurrences => Set<RecurringExpenseOccurrence>();
    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();
    public DbSet<DocumentShareLink> DocumentShareLinks => Set<DocumentShareLink>();
    public DbSet<PasswordVault> PasswordVaults => Set<PasswordVault>();
    public DbSet<PasswordEntry> PasswordEntries => Set<PasswordEntry>();
    public DbSet<PasswordHistory> PasswordHistory => Set<PasswordHistory>();
    public DbSet<SecureNote> SecureNotes => Set<SecureNote>();
    public DbSet<CustomTable> CustomTables => Set<CustomTable>();
    public DbSet<CustomTableColumn> CustomTableColumns => Set<CustomTableColumn>();
    public DbSet<CustomTableRecord> CustomTableRecords => Set<CustomTableRecord>();
    public DbSet<CustomTableValue> CustomTableValues => Set<CustomTableValue>();
    public DbSet<CustomTableAttachment> CustomTableAttachments => Set<CustomTableAttachment>();
    public DbSet<ProductRegister> ProductRegisters => Set<ProductRegister>();
    public DbSet<ProductCustomField> ProductCustomFields => Set<ProductCustomField>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAttachment> ProductAttachments => Set<ProductAttachment>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactAttachment> ContactAttachments => Set<ContactAttachment>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<CreditPayment> CreditPayments => Set<CreditPayment>();
    public DbSet<CreditReminder> CreditReminders => Set<CreditReminder>();
    public DbSet<CreditAttachment> CreditAttachments => Set<CreditAttachment>();
    public DbSet<DiaryEntry> DiaryEntries => Set<DiaryEntry>();
    public DbSet<DiaryTag> DiaryTags => Set<DiaryTag>();
    public DbSet<DiaryAttachment> DiaryAttachments => Set<DiaryAttachment>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<CalendarEventAttachment> CalendarEventAttachments => Set<CalendarEventAttachment>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ReminderSchedule> ReminderSchedules => Set<ReminderSchedule>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationPolicy> NotificationPolicies => Set<NotificationPolicy>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationQueueItem> NotificationQueue => Set<NotificationQueueItem>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<CommunicationProvider> CommunicationProviders => Set<CommunicationProvider>();
    public DbSet<CommunicationLog> CommunicationLogs => Set<CommunicationLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<FriendInvitation> FriendInvitations => Set<FriendInvitation>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<FriendConnection> FriendConnections => Set<FriendConnection>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    public DbSet<UserPresence> UserPresences => Set<UserPresence>();
    public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();
    public DbSet<UserReport> UserReports => Set<UserReport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.HasPostgresExtension("pg_trgm");
        mb.HasSequence<long>("user_public_seq").StartsAt(100001);
        ModelConfiguration.Configure(mb);

        foreach (var et in mb.Model.GetEntityTypes().Where(t => t.ClrType is { IsClass: true, IsAbstract: false } && t.BaseType == null))
        {
            var clr = et.ClrType;
            var owned = typeof(IOwnedEntity).IsAssignableFrom(clr);
            var soft = typeof(ISoftDelete).IsAssignableFrom(clr);
            if (owned) GetType().GetMethod(nameof(ApplyOwnerFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(clr).Invoke(this, [mb]);
            if (soft) GetType().GetMethod(nameof(ApplySoftDeleteFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(clr).Invoke(null, [mb]);
        }
    }

    private void ApplyOwnerFilter<T>(ModelBuilder mb) where T : class, IOwnedEntity
    {
        Expression<Func<T, bool>> filter = e => e.UserId == OwnerFilterId;
        mb.Entity<T>().HasQueryFilter(OwnerFilter, filter);
    }

    private static void ApplySoftDeleteFilter<T>(ModelBuilder mb) where T : class, ISoftDelete
    {
        Expression<Func<T, bool>> filter = e => !e.IsDeleted;
        mb.Entity<T>().HasQueryFilter(SoftDeleteFilter, filter);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditRules();
        return base.SaveChanges();
    }

    private void ApplyAuditRules()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var uid = currentUser.UserId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IOwnedEntity owned)
            {
                if (entry.State == EntityState.Added && owned.UserId == Guid.Empty)
                {
                    owned.UserId = uid ?? throw new InvalidOperationException("Cannot create personal data without an authenticated owner.");
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var original = (Guid)entry.Property(nameof(IOwnedEntity.UserId)).OriginalValue!;
                    if (original != owned.UserId)
                        throw new InvalidOperationException("Ownership of a record cannot be changed.");
                }
            }

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete sd && !HardDeleteMode)
            {
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                sd.DeletedAt = now;
            }

            if (entry.Entity is AuditableEntity a)
            {
                if (entry.State == EntityState.Added)
                {
                    a.CreatedAt = now; a.UpdatedAt = now; a.CreatedBy ??= uid; a.UpdatedBy = uid;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAt = now; a.UpdatedBy = uid;
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                }
            }
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    internal static readonly ValueConverter<Dictionary<string, string?>, string> DictionaryJsonConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<Dictionary<string, string?>>(v, (JsonSerializerOptions?)null) ?? new());

    internal static readonly ValueComparer<Dictionary<string, string?>> DictionaryComparer = new(
        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => JsonSerializer.Deserialize<Dictionary<string, string?>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);
}
