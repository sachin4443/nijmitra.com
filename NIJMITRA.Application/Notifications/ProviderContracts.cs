using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Notifications;

/// <summary>Decrypted provider configuration handed to a provider implementation at send time.</summary>
public record ProviderContext(Guid ProviderId, string Name, ProviderType Type, IReadOnlyDictionary<string, string?> Settings, IReadOnlyDictionary<string, string?> Secrets)
{
    public string? Get(string key) => Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    public string? Secret(string key) => Secrets.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    public string Require(string key) => Get(key) ?? Secret(key) ?? throw new ProviderException($"Provider setting '{key}' is not configured.");
}

public sealed class ProviderException(string message, bool transient = false) : Exception(message)
{
    public bool Transient { get; } = transient;
}

public record OutboundMessage(string Recipient, string? Subject, string Body, string? TemplateId, IReadOnlyDictionary<string, string> Variables, string EventType);

public record ProviderTestResult(bool Success, string Message);

/// <summary>A delivery provider (SMTP, SendGrid, SES, 2Factor, MSG91, Twilio, custom HTTP, Web Push, WhatsApp Cloud).</summary>
public interface IChannelProvider
{
    ProviderType Type { get; }
    Task SendAsync(ProviderContext ctx, OutboundMessage message, CancellationToken ct);
    Task<ProviderTestResult> TestConnectionAsync(ProviderContext ctx, CancellationToken ct);
}

public record ProviderField(string Key, string Label, string Type, bool Required = false, bool Secret = false, string? Default = null, string? Help = null, string[]? Options = null);

public record ProviderDefinition(ProviderType Type, NotificationChannel Channel, string DisplayName, string Description, IReadOnlyList<ProviderField> Fields);

/// <summary>Describes the configuration form of every supported provider (rendered dynamically in the admin UI).</summary>
public static class ProviderCatalog
{
    private static readonly string[] SmtpSecurity = ["Auto", "StartTls", "SslOnConnect", "None"];

    public static readonly IReadOnlyList<ProviderDefinition> All =
    [
        new(ProviderType.Smtp, NotificationChannel.Email, "SMTP / Custom SMTP", "Any SMTP server (Gmail Workspace, Zoho, Hostinger, Office 365, your own server).",
        [
            new("host", "SMTP Host", "text", true, Help: "e.g. smtp.zoho.in"),
            new("port", "SMTP Port", "number", true, Default: "587"),
            new("security", "SSL / TLS", "select", true, Default: "Auto", Options: SmtpSecurity),
            new("username", "Username", "text"),
            new("password", "Password", "password", Secret: true),
            new("senderEmail", "Sender Email", "email", true),
            new("senderName", "Sender Name", "text", Default: "NIJMITRA"),
        ]),
        new(ProviderType.SendGrid, NotificationChannel.Email, "SendGrid", "Twilio SendGrid Web API v3.",
        [
            new("apiKey", "API Key", "password", true, Secret: true),
            new("senderEmail", "Sender Email (verified in SendGrid)", "email", true),
            new("senderName", "Sender Name", "text", Default: "NIJMITRA"),
        ]),
        new(ProviderType.AmazonSes, NotificationChannel.Email, "Amazon SES", "Amazon Simple Email Service (API v2).",
        [
            new("region", "AWS Region", "text", true, Default: "ap-south-1"),
            new("accessKeyId", "Access Key ID", "text", true),
            new("secretAccessKey", "Secret Access Key", "password", true, Secret: true),
            new("senderEmail", "Sender Email (verified identity)", "email", true),
            new("senderName", "Sender Name", "text", Default: "NIJMITRA"),
        ]),
        new(ProviderType.TwoFactorIn, NotificationChannel.Sms, "2Factor.in", "Indian SMS gateway. OTP messages use the OTP API with your DLT-approved template; other messages use the transactional SMS API.",
        [
            new("apiKey", "API Key", "password", true, Secret: true),
            new("senderId", "Sender ID (6 letters, DLT approved)", "text"),
            new("otpTemplateName", "OTP Template Name", "text", Help: "Template name configured in your 2Factor panel (e.g. OTP1)."),
        ]),
        new(ProviderType.Msg91, NotificationChannel.Sms, "MSG91", "MSG91 Flow API v5. Each SMS template maps to an approved MSG91 flow/template id; variables are sent by name.",
        [
            new("authKey", "Auth Key", "password", true, Secret: true),
            new("senderId", "Sender ID", "text"),
            new("defaultTemplateId", "Default Template / Flow ID", "text", Help: "Used when an SMS template has no provider template id."),
        ]),
        new(ProviderType.Twilio, NotificationChannel.Sms, "Twilio", "Twilio Programmable Messaging.",
        [
            new("accountSid", "Account SID", "text", true),
            new("authToken", "Auth Token", "password", true, Secret: true),
            new("fromNumber", "From Number (E.164)", "text", Help: "Either a From number or a Messaging Service SID is required."),
            new("messagingServiceSid", "Messaging Service SID", "text"),
        ]),
        new(ProviderType.CustomHttpSms, NotificationChannel.Sms, "Custom HTTP SMS API", "Any HTTP SMS gateway. Use placeholders {{mobile}}, {{mobileNoPlus}}, {{message}}, {{otp}}, {{senderId}}, {{templateId}}, {{apiKey}}, {{apiSecret}} in URL, headers and body.",
        [
            new("apiUrl", "API URL", "text", true, Help: "e.g. https://api.example.com/send?to={{mobileNoPlus}}&text={{message}}"),
            new("httpMethod", "HTTP Method", "select", true, Default: "POST", Options: ["POST", "GET"]),
            new("contentType", "Body Format", "select", Default: "json", Options: ["json", "form", "none"]),
            new("authType", "Authentication Type", "select", Default: "None", Options: ["None", "Bearer", "Basic", "Header", "Query"]),
            new("authHeaderName", "Auth Header / Query Name", "text", Help: "For Header or Query authentication, e.g. authkey"),
            new("apiKey", "API Key", "password", Secret: true),
            new("apiSecret", "API Secret", "password", Secret: true),
            new("senderId", "Sender ID", "text"),
            new("templateId", "Default Template ID", "text"),
            new("headersJson", "Request Headers (JSON object)", "textarea", Help: "{\"X-Client\": \"nijmitra\"}"),
            new("bodyTemplate", "Request Body Mapping", "textarea", Help: "{\"to\":\"{{mobileNoPlus}}\",\"message\":\"{{message}}\",\"sender\":\"{{senderId}}\"}"),
            new("successContains", "Success response contains (optional)", "text"),
        ]),
        new(ProviderType.WebPushVapid, NotificationChannel.Push, "Web Push (VAPID)", "Browser and installed-PWA push notifications using the standard Web Push protocol.",
        [
            new("publicKey", "VAPID Public Key", "text", true),
            new("privateKey", "VAPID Private Key", "password", true, Secret: true),
            new("subject", "VAPID Subject", "text", true, Default: "mailto:support@nijmitra.com"),
        ]),
        new(ProviderType.WhatsAppCloud, NotificationChannel.WhatsApp, "WhatsApp Business Cloud API", "Meta WhatsApp Cloud API using pre-approved message templates. Not operational until a verified business number and access token are configured and tested.",
        [
            new("phoneNumberId", "Phone Number ID", "text", true),
            new("accessToken", "Permanent Access Token", "password", true, Secret: true),
            new("apiVersion", "Graph API Version", "text", Default: "v21.0"),
            new("languageCode", "Template Language Code", "text", Default: "en"),
        ]),
    ];

    public static ProviderDefinition Get(ProviderType type) => All.First(p => p.Type == type);
}
