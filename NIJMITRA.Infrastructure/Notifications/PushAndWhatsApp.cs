using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Enums;
using WebPush;

namespace NIJMITRA.Infrastructure.Notifications;

public sealed class PushSubscriptionGoneException(string endpoint) : Exception("Push subscription expired.")
{
    public string Endpoint { get; } = endpoint;
}

/// <summary>Standard Web Push (RFC 8030 + VAPID RFC 8292). Recipient is a JSON {endpoint,p256dh,auth}.</summary>
public sealed class WebPushProvider : IChannelProvider
{
    public ProviderType Type => ProviderType.WebPushVapid;

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var sub = JsonSerializer.Deserialize<Dictionary<string, string>>(m.Recipient)!;
        var subscription = new WebPush.PushSubscription(sub["endpoint"], sub["p256dh"], sub["auth"]);
        var vapid = new VapidDetails(c.Require("subject"), c.Require("publicKey"), c.Require("privateKey"));
        var payload = JsonSerializer.Serialize(new
        {
            title = m.Subject ?? "NijMitra",
            body = m.Body,
            url = m.Variables.TryGetValue("Link", out var l) ? l : "/",
            tag = m.EventType,
            icon = "/branding/pwa-192.png",
            badge = "/branding/favicon-96.png"
        });
        try
        {
            using var client = new WebPushClient();
            await client.SendNotificationAsync(subscription, payload, vapid, ct);
        }
        catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
        {
            throw new PushSubscriptionGoneException(sub["endpoint"]);
        }
        catch (WebPushException ex)
        {
            throw new ProviderException($"Push service returned {(int)ex.StatusCode}.", (int)ex.StatusCode >= 500 || (int)ex.StatusCode == 429);
        }
    }

    public Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var pub = Base64UrlDecode(c.Require("publicKey"));
            var priv = Base64UrlDecode(c.Require("privateKey"));
            if (pub.Length != 65 || priv.Length != 32) return Task.FromResult(new ProviderTestResult(false, "VAPID keys have an invalid length (expected 65-byte public and 32-byte private key)."));
            return Task.FromResult(new ProviderTestResult(true, "VAPID keys are well-formed. Use 'Send Test Notification' to deliver to your own subscribed device."));
        }
        catch (Exception ex) { return Task.FromResult(new ProviderTestResult(false, ex.Message)); }
    }

    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        var keys = VapidHelper.GenerateVapidKeys();
        return (keys.PublicKey, keys.PrivateKey);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>Meta WhatsApp Business Cloud API using approved templates.</summary>
public sealed class WhatsAppCloudProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.WhatsAppCloud;

    private HttpClient Client(ProviderContext c)
    {
        var client = http.CreateClient("providers");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", c.Require("accessToken"));
        return client;
    }

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var template = m.TemplateId ?? throw new ProviderException("WhatsApp requires an approved template name on the notification template.");
        var parameters = m.Variables.Where(kv => kv.Key.StartsWith("wa", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key)
            .Select(kv => new { type = "text", text = kv.Value }).ToArray();
        var payload = new
        {
            messaging_product = "whatsapp",
            to = new string(m.Recipient.Where(char.IsDigit).ToArray()),
            type = "template",
            template = new
            {
                name = template,
                language = new { code = c.Get("languageCode") ?? "en" },
                components = parameters.Length == 0 ? Array.Empty<object>() : [new { type = "body", parameters }]
            }
        };
        var res = await Client(c).PostAsJsonAsync($"https://graph.facebook.com/{c.Get("apiVersion") ?? "v21.0"}/{c.Require("phoneNumberId")}/messages", payload, ct);
        await SmsUtil.EnsureSuccess(res, "WhatsApp Cloud API", ct);
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var res = await Client(c).GetAsync($"https://graph.facebook.com/{c.Get("apiVersion") ?? "v21.0"}/{c.Require("phoneNumberId")}", ct);
            return res.IsSuccessStatusCode ? new(true, "Phone number id and token accepted by Meta.") : new(false, $"Meta returned {(int)res.StatusCode}: {SendGridEmailProvider.Trim(await res.Content.ReadAsStringAsync(ct))}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}
