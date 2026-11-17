using System.Net;
using System.Text.RegularExpressions;

namespace NIJMITRA.Application.Notifications;

public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Replaces {{Name}} placeholders. Values are HTML-encoded when rendering HTML.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> vars, bool html)
        => Placeholder().Replace(template ?? "", m =>
        {
            var key = m.Groups[1].Value;
            var match = vars.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null) return m.Value;
            return html ? WebUtility.HtmlEncode(match.Value) : match.Value;
        });

    public static string WrapEmail(string innerHtml, string appName, string? logoUrl, string tagline)
    {
        var logo = string.IsNullOrEmpty(logoUrl)
            ? $"<span style=\"font-size:22px;font-weight:700;color:#4F46E5\">{WebUtility.HtmlEncode(appName)}</span>"
            : $"<img src=\"{WebUtility.HtmlEncode(logoUrl)}\" alt=\"{WebUtility.HtmlEncode(appName)}\" height=\"40\" style=\"display:block\"/>";
        return $"""
<!doctype html><html><body style="margin:0;background:#F4F5FB;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1E1B4B">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F4F5FB;padding:24px 0"><tr><td align="center">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid #E0E7FF">
<tr><td style="padding:22px 28px;border-bottom:3px solid #4F46E5">{logo}</td></tr>
<tr><td style="padding:28px;font-size:15px;line-height:1.6">{innerHtml}</td></tr>
<tr><td style="padding:18px 28px;background:#EEF2FF;font-size:12px;color:#4B5563">{WebUtility.HtmlEncode(appName)} — {WebUtility.HtmlEncode(tagline)}<br/>This is an automated message. Please do not reply.</td></tr>
</table></td></tr></table></body></html>
""";
    }
}
