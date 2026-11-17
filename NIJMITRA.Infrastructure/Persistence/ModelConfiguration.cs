using Microsoft.EntityFrameworkCore;
using NIJMITRA.Domain.Entities;

namespace NIJMITRA.Infrastructure.Persistence;

internal static class ModelConfiguration
{
    public static void Configure(ModelBuilder mb)
    {
        // ---------- Identity ----------
        mb.Entity<User>(e =>
        {
            e.HasIndex(x => x.MobileNumber).IsUnique();
            e.HasIndex(x => x.PublicId).IsUnique();
            e.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL");
            e.HasIndex(x => x.Status);
            e.Property(x => x.SequenceNumber).HasDefaultValueSql("nextval('user_public_seq')");
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.MobileNumber).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.PendingEmail).HasMaxLength(256);
            e.Property(x => x.PublicId).HasMaxLength(20);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SecuritySettings).WithOne().HasForeignKey<UserSecuritySettings>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<UserProfile>().HasKey(x => x.UserId);
        mb.Entity<UserSecuritySettings>().HasKey(x => x.UserId);
        mb.Entity<UserAuthenticatorSecret>().HasIndex(x => x.UserId);
        mb.Entity<UserRecoveryCode>().HasIndex(x => x.UserId);
        mb.Entity<UserSession>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<UserDevice>().HasIndex(x => new { x.UserId, x.DeviceKey }).IsUnique();
        mb.Entity<LoginAuditLog>(e => { e.HasIndex(x => new { x.UserId, x.Timestamp }); e.HasIndex(x => x.Timestamp); });
        mb.Entity<AuditLog>(e => { e.HasIndex(x => new { x.UserId, x.Timestamp }); e.HasIndex(x => x.Timestamp); });
        mb.Entity<AdminAuditLog>().HasIndex(x => x.Timestamp);
        mb.Entity<OtpCode>().HasIndex(x => new { x.Target, x.Purpose, x.CreatedAt });
        mb.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        mb.Entity<Permission>().HasIndex(x => x.Key).IsUnique();
        mb.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });
        mb.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
        });
        mb.Entity<SystemSetting>().HasKey(x => x.Key);
        mb.Entity<AccountDeletionRequest>().HasIndex(x => new { x.Status, x.ScheduledFor });
        mb.Entity<RecentSearch>().HasIndex(x => new { x.UserId, x.CreatedAt });
        mb.Entity<StoredFile>(e => { e.HasIndex(x => x.UserId); e.HasIndex(x => x.StorageKey).IsUnique(); });

        // ---------- Finance ----------
        mb.Entity<ExpenseCategory>().HasIndex(x => new { x.UserId, x.Type, x.Name });
        mb.Entity<Expense>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<IncomeRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
        });
        mb.Entity<MonthlyBudget>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Year, x.Month }).IsUnique().HasFilter("is_deleted = false");
            e.HasMany(x => x.Categories).WithOne(c => c.MonthlyBudget).HasForeignKey(c => c.MonthlyBudgetId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<BudgetCategory>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<RecurringExpense>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Occurrences).WithOne(o => o.RecurringExpense).HasForeignKey(o => o.RecurringExpenseId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<RecurringExpenseOccurrence>(e =>
        {
            e.HasIndex(x => new { x.RecurringExpenseId, x.DueDate }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status, x.DueDate });
        });

        // ---------- Documents ----------
        mb.Entity<DocumentFolder>().HasIndex(x => new { x.UserId, x.ParentId });
        mb.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.FolderId });
            e.HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Versions).WithOne(v => v.Document).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tags).WithOne().HasForeignKey(t => t.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<DocumentVersion>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<DocumentTag>().HasIndex(x => new { x.DocumentId, x.Tag }).IsUnique();
        mb.Entity<DocumentShareLink>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Vault ----------
        mb.Entity<PasswordVault>().HasIndex(x => x.UserId).IsUnique();
        mb.Entity<PasswordEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Category });
            e.HasMany(x => x.History).WithOne().HasForeignKey(h => h.PasswordEntryId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<PasswordHistory>().HasIndex(x => new { x.PasswordEntryId, x.ChangedAt });
        mb.Entity<SecureNote>().HasIndex(x => x.UserId);

        // ---------- Registers ----------
        mb.Entity<CustomTable>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.Columns).WithOne(c => c.Table).HasForeignKey(c => c.TableId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<CustomTableColumn>().HasIndex(x => new { x.TableId, x.SortOrder });
        mb.Entity<CustomTableRecord>(e =>
        {
            e.HasIndex(x => new { x.TableId, x.CreatedAt });
            e.HasOne(x => x.Table).WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Values).WithOne(v => v.Record).HasForeignKey(v => v.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.RecordId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<CustomTableValue>(e =>
        {
            e.HasIndex(x => new { x.RecordId, x.ColumnId }).IsUnique();
            e.HasIndex(x => new { x.ColumnId, x.TextValue });
            e.HasIndex(x => new { x.ColumnId, x.NumberValue });
            e.HasIndex(x => new { x.ColumnId, x.DateValue });
            e.Property(x => x.NumberValue).HasPrecision(28, 8);
        });
        mb.Entity<CustomTableAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ProductRegister>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasMany(x => x.CustomFields).WithOne().HasForeignKey(f => f.RegisterId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<Product>(e =>
        {
            e.HasIndex(x => new { x.RegisterId, x.ProductName });
            e.HasIndex(x => x.ProductName).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne(x => x.Register).WithMany().HasForeignKey(x => x.RegisterId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.GstPercent).HasPrecision(5, 2);
            e.Property(x => x.CustomValues).HasColumnType("jsonb")
                .HasConversion(AppDbContext.DictionaryJsonConverter, AppDbContext.DictionaryComparer);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<ProductAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);

        // ---------- Contacts & Udhar ----------
        mb.Entity<ContactGroup>().HasIndex(x => x.UserId);
        mb.Entity<Contact>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Name });
            e.HasIndex(x => x.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<ContactAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<CreditTransaction>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Direction, x.Status });
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.PersonName).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.Ignore(x => x.Outstanding);
            e.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Payments).WithOne(p => p.Transaction).HasForeignKey(p => p.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<CreditReminder>().HasIndex(x => new { x.TransactionId, x.RemindAt });
        mb.Entity<CreditAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);

        // ---------- Diary & calendar ----------
        mb.Entity<DiaryEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.EntryDate });
            e.HasMany(x => x.Tags).WithOne().HasForeignKey(t => t.EntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.EntryId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<DiaryTag>().HasIndex(x => new { x.EntryId, x.Tag }).IsUnique();
        mb.Entity<DiaryAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<CalendarEvent>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StartUtc });
            e.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<CalendarEventAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Reminder>().HasIndex(x => new { x.UserId, x.RemindAtUtc });
        mb.Entity<ReminderSchedule>(e =>
        {
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.FireAtUtc });
            e.HasIndex(x => new { x.SourceType, x.SourceId });
        });

        // ---------- Notifications & communication ----------
        mb.Entity<Notification>().HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
        mb.Entity<NotificationPreference>().HasKey(x => x.UserId);
        mb.Entity<NotificationPolicy>().HasKey(x => x.EventType);
        mb.Entity<NotificationTemplate>().HasIndex(x => new { x.EventType, x.Channel }).IsUnique();
        mb.Entity<NotificationQueueItem>(e =>
        {
            e.ToTable("notification_queue");
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        mb.Entity<PushSubscription>().HasIndex(x => x.Endpoint).IsUnique();
        mb.Entity<CommunicationProvider>().HasIndex(x => new { x.Channel, x.IsActive, x.Priority });
        mb.Entity<CommunicationLog>(e => { e.HasIndex(x => x.CreatedAt); e.HasIndex(x => new { x.Channel, x.Status }); });
        mb.Entity<Announcement>().HasIndex(x => x.CreatedAt);

        // ---------- Chat ----------
        mb.Entity<FriendInvitation>().HasIndex(x => x.Code).IsUnique();
        mb.Entity<FriendRequest>(e =>
        {
            e.HasIndex(x => new { x.ToUserId, x.Status });
            e.HasIndex(x => new { x.FromUserId, x.ToUserId, x.Status });
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<FriendConnection>().HasIndex(x => new { x.UserAId, x.UserBId }).IsUnique();
        mb.Entity<ChatConversation>().HasMany(x => x.Participants).WithOne(p => p.Conversation).HasForeignKey(p => p.ConversationId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<ChatParticipant>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<ChatMessage>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasIndex(x => x.ExpiresAt).HasFilter("expires_at IS NOT NULL");
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<ChatAttachment>().HasOne(x => x.StoredFile).WithMany().HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<UserPresence>().HasKey(x => x.UserId);
        mb.Entity<BlockedUser>().HasIndex(x => new { x.UserId, x.BlockedUserId }).IsUnique();
        mb.Entity<UserReport>().HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
