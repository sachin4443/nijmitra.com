using Microsoft.EntityFrameworkCore;

namespace NIJMITRA.Application.Common;

public record PageRequest(int Page = 1, int PageSize = 20)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafeSize => PageSize is < 1 or > 200 ? 20 : PageSize;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}

public static class TextUtil
{
    /// <summary>Escapes LIKE wildcards in user input and wraps for a contains-match.</summary>
    public static string LikePattern(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static string MaskMobile(string? mobile) =>
        string.IsNullOrEmpty(mobile) || mobile.Length < 4 ? "****" : new string('*', mobile.Length - 4) + mobile[^4..];

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "****";
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + (at >= 0 ? email[at..] : "");
        return email[0] + new string('*', Math.Min(at - 1, 6)) + email[at..];
    }

    public static string NormalizeMobile(string mobile)
    {
        var digits = new string(mobile.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("+")) return digits;
        if (digits.Length == 10) return "+91" + digits;
        if (digits.Length == 12 && digits.StartsWith("91")) return "+" + digits;
        return digits;
    }

    public static string? Clean(string? s, int max = 4000)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }
}
