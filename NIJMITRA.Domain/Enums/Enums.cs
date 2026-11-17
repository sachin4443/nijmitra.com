namespace NIJMITRA.Domain.Enums;

public enum UserStatus { Active = 1, Inactive = 2, Suspended = 3, PendingDeletion = 4, Deleted = 5 }
public enum OtpPurpose { RegistrationMobile = 1, EmailVerification = 2, PasswordReset = 3, MobileChange = 4 }
public enum ThemePreference { System = 0, Light = 1, Dark = 2 }

public enum PaymentMethod { Cash = 1, Upi = 2, DebitCard = 3, CreditCard = 4, NetBanking = 5, Wallet = 6, Cheque = 7, Other = 8 }
public enum CategoryType { Expense = 1, Income = 2 }
public enum RecurrenceFrequency { Daily = 1, Weekly = 2, Monthly = 3, Quarterly = 4, Yearly = 5, Custom = 6 }
public enum OccurrenceStatus { Pending = 1, Paid = 2, Overdue = 3, Skipped = 4 }

public enum CustomFieldType
{
    Text = 1, LongText = 2, Number = 3, Decimal = 4, Currency = 5, Date = 6, DateTime = 7, Boolean = 8,
    Email = 9, Phone = 10, Url = 11, Dropdown = 12, MultiSelect = 13, FileAttachment = 14, Image = 15, Formula = 16
}

public enum CreditDirection { Given = 1, Borrowed = 2 }
public enum CreditStatus { Open = 1, PartiallySettled = 2, Settled = 3 }

public enum CalendarEventType { Meeting = 1, Appointment = 2, Birthday = 3, Task = 4, BillPayment = 5, FollowUp = 6, Personal = 7 }
public enum EventPriority { Low = 1, Normal = 2, High = 3, Urgent = 4 }
public enum EventRecurrence { None = 0, Daily = 1, Weekly = 2, Monthly = 3, Yearly = 4 }
public enum ReminderSourceType { CalendarEvent = 1, Reminder = 2, RecurringExpense = 3, CreditTransaction = 4, CreditReminder = 5 }
public enum ScheduleStatus { Pending = 1, Sent = 2, Cancelled = 3, Failed = 4 }

public enum NotificationChannel { InApp = 1, Email = 2, Sms = 3, Push = 4, WhatsApp = 5 }
public enum QueueStatus { Pending = 1, Processing = 2, Sent = 3, Failed = 4, Dead = 5, Skipped = 6 }
public enum ProviderType
{
    Smtp = 1, SendGrid = 2, AmazonSes = 3,
    TwoFactorIn = 20, Msg91 = 21, Twilio = 22, CustomHttpSms = 23,
    WebPushVapid = 40,
    WhatsAppCloud = 60
}
public enum AnnouncementTarget { AllUsers = 1, SelectedUsers = 2, ActiveUsers = 3, Role = 4 }

public enum FriendRequestStatus { Pending = 1, Accepted = 2, Rejected = 3, Cancelled = 4 }
public enum ChatMessageType { Text = 1, Image = 2, Audio = 3, Video = 4, File = 5, System = 6 }
public enum ReportStatus { Open = 1, Reviewed = 2, ActionTaken = 3, Dismissed = 4 }
public enum DeletionRequestStatus { Pending = 1, Cancelled = 2, Completed = 3 }
