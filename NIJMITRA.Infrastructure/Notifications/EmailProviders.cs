using System.Net.Http.Headers;
using System.Net.Http.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Infrastructure.Notifications;

public sealed class SmtpEmailProvider : IChannelProvider
{
    public ProviderType Type => ProviderType.Smtp;

    private static SecureSocketOptions Security(ProviderContext c) => c.Get("security") switch
    {
        "StartTls" => SecureSocketOptions.StartTls,
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        "None" => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };

    private static async Task<SmtpClient> ConnectAsync(ProviderContext c, CancellationToken ct)
    {
        var client = new SmtpClient { Timeout = 20000 };
        await client.ConnectAsync(c.Require("host"), int.Parse(c.Get("port") ?? "587"), Security(c), ct);
        if (c.Get("username") is { } user) await client.AuthenticateAsync(user, c.Secret("password") ?? "", ct);
        return client;
    }

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(c.Get("senderName") ?? "NIJMITRA", c.Require("senderEmail")));
        msg.To.Add(MailboxAddress.Parse(m.Recipient));
        msg.Subject = m.Subject ?? "NIJMITRA";
        msg.Body = new BodyBuilder { HtmlBody = m.Body }.ToMessageBody();
        try
        {
            using var client = await ConnectAsync(c, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not ProviderException)
        {
            throw new ProviderException($"SMTP error: {ex.Message}", ex is not AuthenticationException);
        }
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(c, ct);
            await client.DisconnectAsync(true, ct);
            return new(true, "Connected and authenticated successfully.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}

public sealed class SendGridEmailProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.SendGrid;

    private HttpClient Client(ProviderContext c)
    {
        var client = http.CreateClient("providers");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", c.Require("apiKey"));
        return client;
    }

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = m.Recipient } } } },
            from = new { email = c.Require("senderEmail"), name = c.Get("senderName") ?? "NIJMITRA" },
            subject = m.Subject ?? "NIJMITRA",
            content = new[] { new { type = "text/html", value = m.Body } }
        };
        var res = await Client(c).PostAsJsonAsync("https://api.sendgrid.com/v3/mail/send", payload, ct);
        if (!res.IsSuccessStatusCode)
            throw new ProviderException($"SendGrid returned {(int)res.StatusCode}: {Trim(await res.Content.ReadAsStringAsync(ct))}", (int)res.StatusCode >= 500 || (int)res.StatusCode == 429);
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var res = await Client(c).GetAsync("https://api.sendgrid.com/v3/scopes", ct);
            return res.IsSuccessStatusCode ? new(true, "API key accepted by SendGrid.") : new(false, $"SendGrid returned {(int)res.StatusCode}.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    internal static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}

public sealed class AmazonSesEmailProvider : IChannelProvider
{
    public ProviderType Type => ProviderType.AmazonSes;

    private static AmazonSimpleEmailServiceV2Client Client(ProviderContext c) =>
        new(new BasicAWSCredentials(c.Require("accessKeyId"), c.Require("secretAccessKey")), RegionEndpoint.GetBySystemName(c.Get("region") ?? "ap-south-1"));

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        using var client = Client(c);
        try
        {
            await client.SendEmailAsync(new SendEmailRequest
            {
                FromEmailAddress = $"{c.Get("senderName") ?? "NIJMITRA"} <{c.Require("senderEmail")}>",
                Destination = new Destination { ToAddresses = [m.Recipient] },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = m.Subject ?? "NIJMITRA", Charset = "UTF-8" },
                        Body = new Body { Html = new Content { Data = m.Body, Charset = "UTF-8" } }
                    }
                }
            }, ct);
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            throw new ProviderException($"Amazon SES error: {ex.ErrorCode} {ex.Message}", ex.StatusCode >= System.Net.HttpStatusCode.InternalServerError);
        }
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            using var client = Client(c);
            var acct = await client.GetAccountAsync(new GetAccountRequest(), ct);
            return new(true, $"Connected. Sending enabled: {acct.SendingEnabled}; production access: {acct.ProductionAccessEnabled}.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}
