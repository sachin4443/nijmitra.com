using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NIJMITRA.Application.Notifications;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Infrastructure.Notifications;

internal static class SmsUtil
{
    public static string Digits(string mobile) => new(mobile.Where(char.IsDigit).ToArray());
    public static string? Var(OutboundMessage m, string key) =>
        m.Variables.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public static async Task EnsureSuccess(HttpResponseMessage res, string provider, CancellationToken ct, string? mustContain = null)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ProviderException($"{provider} returned {(int)res.StatusCode}: {SendGridEmailProvider.Trim(body)}", (int)res.StatusCode >= 500 || (int)res.StatusCode == 429);
        if (!string.IsNullOrEmpty(mustContain) && !body.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
            throw new ProviderException($"{provider} response did not indicate success: {SendGridEmailProvider.Trim(body)}");
    }
}

/// <summary>2Factor.in — OTP API for OTP messages, transactional SMS API (R1) for everything else.</summary>
public sealed class TwoFactorSmsProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.TwoFactorIn;

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var key = Uri.EscapeDataString(c.Require("apiKey"));
        var phone = SmsUtil.Digits(m.Recipient);
        var client = http.CreateClient("providers");
        var otp = SmsUtil.Var(m, "OTP");
        HttpResponseMessage res;
        if (!string.IsNullOrEmpty(otp))
        {
            var template = m.TemplateId ?? c.Get("otpTemplateName");
            var url = $"https://2factor.in/API/V1/{key}/SMS/{phone}/{Uri.EscapeDataString(otp)}" + (template != null ? "/" + Uri.EscapeDataString(template) : "");
            res = await client.GetAsync(url, ct);
        }
        else
        {
            var form = new Dictionary<string, string>
            {
                ["module"] = "TRANS_SMS", ["apikey"] = c.Require("apiKey"), ["to"] = phone,
                ["from"] = c.Get("senderId") ?? "", ["msg"] = m.Body
            };
            if (m.TemplateId != null) form["templatename"] = m.TemplateId;
            res = await client.PostAsync("https://2factor.in/API/R1/", new FormUrlEncodedContent(form), ct);
        }
        await SmsUtil.EnsureSuccess(res, "2Factor.in", ct, "Success");
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var res = await http.CreateClient("providers").GetAsync($"https://2factor.in/API/V1/{Uri.EscapeDataString(c.Require("apiKey"))}/BAL/SMS", ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return body.Contains("Success", StringComparison.OrdinalIgnoreCase)
                ? new(true, "API key valid. " + SendGridEmailProvider.Trim(body))
                : new(false, SendGridEmailProvider.Trim(body));
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}

/// <summary>MSG91 Flow API (v5). Template variables are passed by name.</summary>
public sealed class Msg91SmsProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.Msg91;

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var templateId = m.TemplateId ?? c.Get("defaultTemplateId") ?? throw new ProviderException("MSG91 requires a template / flow id.");
        var recipient = new Dictionary<string, string> { ["mobiles"] = SmsUtil.Digits(m.Recipient) };
        foreach (var kv in m.Variables) recipient[kv.Key] = kv.Value;
        var payload = new Dictionary<string, object> { ["template_id"] = templateId, ["short_url"] = "0", ["recipients"] = new[] { recipient } };
        if (c.Get("senderId") is { } sender) payload["sender"] = sender;
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow/") { Content = JsonContent.Create(payload) };
        req.Headers.Add("authkey", c.Require("authKey"));
        var res = await http.CreateClient("providers").SendAsync(req, ct);
        await SmsUtil.EnsureSuccess(res, "MSG91", ct, "success");
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var res = await http.CreateClient("providers").GetAsync($"https://control.msg91.com/api/balance.php?authkey={Uri.EscapeDataString(c.Require("authKey"))}&type=4", ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            var ok = res.IsSuccessStatusCode && decimal.TryParse(body.Trim(), out _);
            return new(ok, ok ? $"Auth key valid. Balance: {body.Trim()}" : SendGridEmailProvider.Trim(body));
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}

public sealed class TwilioSmsProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.Twilio;

    private HttpClient Client(ProviderContext c)
    {
        var client = http.CreateClient("providers");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{c.Require("accountSid")}:{c.Require("authToken")}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return client;
    }

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["To"] = m.Recipient, ["Body"] = m.Body };
        if (c.Get("messagingServiceSid") is { } svc) form["MessagingServiceSid"] = svc;
        else form["From"] = c.Get("fromNumber") ?? throw new ProviderException("Twilio requires a From number or Messaging Service SID.");
        var res = await Client(c).PostAsync($"https://api.twilio.com/2010-04-01/Accounts/{c.Require("accountSid")}/Messages.json", new FormUrlEncodedContent(form), ct);
        await SmsUtil.EnsureSuccess(res, "Twilio", ct);
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var res = await Client(c).GetAsync($"https://api.twilio.com/2010-04-01/Accounts/{c.Require("accountSid")}.json", ct);
            return res.IsSuccessStatusCode ? new(true, "Twilio credentials accepted.") : new(false, $"Twilio returned {(int)res.StatusCode}.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}

/// <summary>Fully configurable HTTP SMS gateway with placeholder mapping for URL, headers and body.</summary>
public sealed class CustomHttpSmsProvider(IHttpClientFactory http) : IChannelProvider
{
    public ProviderType Type => ProviderType.CustomHttpSms;

    private static Dictionary<string, string> Values(ProviderContext c, OutboundMessage m)
    {
        var digits = SmsUtil.Digits(m.Recipient);
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mobile"] = m.Recipient, ["mobileNoPlus"] = digits, ["mobile10"] = digits.Length > 10 ? digits[^10..] : digits,
            ["message"] = m.Body, ["otp"] = SmsUtil.Var(m, "OTP") ?? "", ["senderId"] = c.Get("senderId") ?? "",
            ["templateId"] = m.TemplateId ?? c.Get("templateId") ?? "", ["apiKey"] = c.Secret("apiKey") ?? "", ["apiSecret"] = c.Secret("apiSecret") ?? ""
        };
        foreach (var kv in m.Variables) v.TryAdd(kv.Key, kv.Value);
        return v;
    }

    private static string Fill(string template, Dictionary<string, string> values, Func<string, string> encode) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}",
            mm => values.TryGetValue(mm.Groups[1].Value, out var val) ? encode(val) : mm.Value);

    public async Task SendAsync(ProviderContext c, OutboundMessage m, CancellationToken ct)
    {
        var values = Values(c, m);
        var url = Fill(c.Require("apiUrl"), values, Uri.EscapeDataString);
        var method = (c.Get("httpMethod") ?? "POST").ToUpperInvariant() == "GET" ? HttpMethod.Get : HttpMethod.Post;
        var auth = c.Get("authType") ?? "None";
        if (auth == "Query")
            url += (url.Contains('?') ? "&" : "?") + Uri.EscapeDataString(c.Get("authHeaderName") ?? "apikey") + "=" + Uri.EscapeDataString(c.Secret("apiKey") ?? "");
        using var req = new HttpRequestMessage(method, url);
        if (method == HttpMethod.Post && c.Get("bodyTemplate") is { } bodyTpl && (c.Get("contentType") ?? "json") != "none")
        {
            if ((c.Get("contentType") ?? "json") == "form")
                req.Content = new StringContent(Fill(bodyTpl, values, Uri.EscapeDataString), Encoding.UTF8, "application/x-www-form-urlencoded");
            else
                req.Content = new StringContent(Fill(bodyTpl, values, s => JsonSerializer.Serialize(s)[1..^1]), Encoding.UTF8, "application/json");
        }
        if (c.Get("headersJson") is { } hj)
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(hj) ?? [];
            foreach (var h in headers) req.Headers.TryAddWithoutValidation(h.Key, Fill(h.Value, values, s => s));
        }
        switch (auth)
        {
            case "Bearer": req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Secret("apiKey") ?? ""); break;
            case "Basic": req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Secret("apiKey")}:{c.Secret("apiSecret")}"))); break;
            case "Header": req.Headers.TryAddWithoutValidation(c.Get("authHeaderName") ?? "Authorization", c.Secret("apiKey") ?? ""); break;
        }
        var res = await http.CreateClient("providers").SendAsync(req, ct);
        await SmsUtil.EnsureSuccess(res, "Custom SMS API", ct, c.Get("successContains"));
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderContext c, CancellationToken ct)
    {
        try
        {
            var uri = new Uri(c.Require("apiUrl").Split('?')[0].Replace("{{", "").Replace("}}", ""));
            using var req = new HttpRequestMessage(HttpMethod.Head, $"{uri.Scheme}://{uri.Authority}/");
            var res = await http.CreateClient("providers").SendAsync(req, ct);
            return new(true, $"Gateway host reachable (HTTP {(int)res.StatusCode}). Use 'Send Test SMS' to verify the full request mapping.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}
