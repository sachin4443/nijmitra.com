namespace NIJMITRA.Application.Common;

/// <summary>Administrator-controlled platform configuration stored in the SystemSettings table.</summary>
public class PlatformSettings
{
    public string ApplicationName { get; set; } = "NijMitra";
    public string Tagline { get; set; } = "Aapka Personal Digital Saathi";
    public string HindiTagline { get; set; } = "आपका व्यक्तिगत डिजिटल साथी";
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
    public string MaintenanceMessage { get; set; } = "NijMitra is undergoing scheduled maintenance. Please try again shortly.";
    public bool ChatEnabled { get; set; } = true;

    // Public website (landing page footer etc.) — editable from Admin → Website.
    public WebsiteSettings Website { get; set; } = new();
}

/// <summary>Content of the public website footer. Each text has an English and an optional Hindi version.</summary>
public class WebsiteSettings
{
    public string AboutEn { get; set; } = "NijMitra — your personal digital companion. A secure personal life-management platform for individuals and families.";
    public string? AboutHi { get; set; } = "NijMitra — आपका व्यक्तिगत डिजिटल साथी। व्यक्तियों और परिवारों के लिए एक सुरक्षित निजी जीवन-प्रबंधन प्लेटफ़ॉर्म।";
    /// <summary>Shown as "© {year} {CopyrightName}".</summary>
    public string CopyrightName { get; set; } = "NijMitra";
    /// <summary>Optional extra line in the footer bottom bar, e.g. "A product by …". Empty = hidden.</summary>
    public string? CompanyLine { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    /// <summary>Google Play listing; when set, the Android card on the home page links to it.</summary>
    public string? PlayStoreUrl { get; set; }
    public List<FooterColumn> Columns { get; set; } =
    [
        new() { TitleEn = "Product", TitleHi = "प्रोडक्ट", Links = [new() { LabelEn = "Features", LabelHi = "सुविधाएं", Url = "#features" }, new() { LabelEn = "Security", LabelHi = "सुरक्षा", Url = "#security" }, new() { LabelEn = "Apps", LabelHi = "ऐप्स", Url = "#apps" }] },
        new() { TitleEn = "Account", TitleHi = "अकाउंट", Links = [new() { LabelEn = "Login", LabelHi = "लॉगिन", Url = "/login" }, new() { LabelEn = "Register", LabelHi = "रजिस्टर", Url = "/register" }, new() { LabelEn = "Forgot password", LabelHi = "पासवर्ड भूल गए", Url = "/forgot-password" }] },
        new() { TitleEn = "Legal", TitleHi = "कानूनी", Links = [new() { LabelEn = "Terms of Use", LabelHi = "उपयोग की शर्तें", Url = "/terms.html" }, new() { LabelEn = "Privacy Policy", LabelHi = "गोपनीयता नीति", Url = "/privacy.html" }, new() { LabelEn = "Delete account & data", LabelHi = "अकाउंट और डेटा हटाएं", Url = "/delete-account.html" }] },
    ];
    public List<SocialLink> SocialLinks { get; set; } = [];

    /// <summary>Android app package (Trusted Web Activity) published in /.well-known/assetlinks.json.</summary>
    public string AndroidPackageName { get; set; } = "com.nijmitra.app";
    /// <summary>SHA-256 certificate fingerprints allowed to open the site full-screen: the upload key and the Google Play app-signing key.</summary>
    public List<string> AndroidSha256Fingerprints { get; set; } = ["65:C6:D0:1B:C0:55:B3:54:D0:C3:67:9C:3E:A5:2D:99:18:07:19:A3:2B:0F:71:8E:C6:03:25:6C:58:C0:7C:3A"];
}

public class FooterColumn
{
    public string TitleEn { get; set; } = "";
    public string? TitleHi { get; set; }
    public List<FooterLink> Links { get; set; } = [];
}

public class FooterLink
{
    public string LabelEn { get; set; } = "";
    public string? LabelHi { get; set; }
    public string Url { get; set; } = "";
}

public class SocialLink
{
    /// <summary>facebook, instagram, x, youtube, linkedin, whatsapp, telegram or website.</summary>
    public string Platform { get; set; } = "website";
    public string Url { get; set; } = "";
}

/// <summary>Static deployment configuration (appsettings / environment).</summary>
public class AppOptions
{
    public string PublicBaseUrl { get; set; } = "https://nijmitra.com";
    /// <summary>Mobile numbers that are granted the Super Admin role automatically (bootstrap).</summary>
    public List<string> BootstrapSuperAdminMobiles { get; set; } = [];
}
