namespace NIJMITRA.Application.Common;

/// <summary>Administrator-controlled platform configuration stored in the SystemSettings table.</summary>
public class PlatformSettings
{
    public string ApplicationName { get; set; } = "NIJMITRA";
    public string Tagline { get; set; } = "Your Life. Your Records. Your Control.";
    public string HindiTagline { get; set; } = "निजी जीवन का डिजिटल साथी।";
    public string? SupportEmail { get; set; }
    public string? LogoFileId { get; set; }
    public string? FaviconFileId { get; set; }

    // Sessions
    public int SessionIdleTimeoutSeconds { get; set; } = 60;
    public int SessionWarningSeconds { get; set; } = 15;
    public int SessionAbsoluteHours { get; set; } = 12;

    // Registration & security
    public bool AllowRegistration { get; set; } = true;
    public bool RequireMobileOtp { get; set; }
    public bool RequireEmailVerification { get; set; }
    public bool EnforceTwoFactor { get; set; }
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public bool NotifyUserOnAdminAccess { get; set; } = true;
    public int AccountDeletionGraceDays { get; set; } = 30;

    // Files
    public int MaxUploadMb { get; set; } = 25;
    public int UserStorageQuotaMb { get; set; } = 2048;
    public List<string> AllowedExtensions { get; set; } =
        [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".jpg", ".jpeg", ".png", ".webp", ".txt",
         ".mp3", ".m4a", ".ogg", ".wav", ".webm", ".mp4", ".mov"];

    // Operations
    public bool MaintenanceMode { get; set; }
    public string MaintenanceMessage { get; set; } = "NIJMITRA is undergoing scheduled maintenance. Please try again shortly.";
    public bool ChatEnabled { get; set; } = true;
}

/// <summary>Static deployment configuration (appsettings / environment).</summary>
public class AppOptions
{
    public string PublicBaseUrl { get; set; } = "https://nijmitra.com";
    /// <summary>Mobile numbers that are granted the Super Admin role automatically (bootstrap).</summary>
    public List<string> BootstrapSuperAdminMobiles { get; set; } = [];
}
