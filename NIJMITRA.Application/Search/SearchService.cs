using Microsoft.EntityFrameworkCore;
using NIJMITRA.Application.Abstractions;
using NIJMITRA.Application.Common;
using NIJMITRA.Domain.Entities;
using NIJMITRA.Domain.Enums;

namespace NIJMITRA.Application.Search;

public record SearchHit(string Module, Guid Id, string Title, string? Subtitle, DateTime? Date, decimal? Amount, string Link);
public record SearchGroup(string Module, string Label, int Count, IReadOnlyList<SearchHit> Items);
public record SearchResponse(string Query, int TotalCount, IReadOnlyList<SearchGroup> Groups, int Page, int PageSize);

/// <summary>
/// Global search over the caller's own data only (owner query filters). Password-vault hits come from non-secret
/// metadata (title, website, application, category, tags) — usernames and passwords are encrypted client-side and
/// can never appear in search results, indexes or logs.
/// </summary>
public sealed class SearchService(IAppDbContext db, ICurrentUser current, TimeProvider clock)
{
    public static readonly Dictionary<string, string> Modules = new()
    {
        ["contacts"] = "Contacts", ["expenses"] = "Expenses", ["income"] = "Income", ["documents"] = "Documents", ["passwords"] = "Password Manager",
        ["products"] = "Products", ["tables"] = "Custom Tables", ["udhar"] = "Udhar", ["diary"] = "Diary", ["events"] = "Meetings & Events",
        ["reminders"] = "Reminders", ["chat"] = "Chat"
    };

    public async Task<SearchResponse> SearchAsync(string query, string[]? modules, int page, int pageSize, CancellationToken ct)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) return new SearchResponse(query, 0, [], 1, pageSize);
        if (query.Length > 100) query = query[..100];
        var like = TextUtil.LikePattern(query);
        var wanted = (modules is { Length: > 0 } ? modules : Modules.Keys.ToArray()).Where(Modules.ContainsKey).ToHashSet();
        var single = wanted.Count == 1;
        pageSize = Math.Clamp(pageSize, 1, 50);
        var take = single ? pageSize : 5;
        var skip = single ? (Math.Max(1, page) - 1) * pageSize : 0;
        var groups = new List<SearchGroup>();
        var uid = current.RequireUserId();

        async Task Add<T>(string key, IQueryable<T> q, Func<IQueryable<T>, IQueryable<SearchHit>> project)
        {
            if (!wanted.Contains(key)) return;
            var count = await q.CountAsync(ct);
            if (count == 0) return;
            groups.Add(new SearchGroup(key, Modules[key], count, await project(q).Skip(skip).Take(take).ToListAsync(ct)));
        }

        await Add("contacts", db.Contacts.Where(c => EF.Functions.ILike(c.Name, like) || (c.Mobile != null && EF.Functions.ILike(c.Mobile, like)) || (c.Email != null && EF.Functions.ILike(c.Email, like)) || (c.Company != null && EF.Functions.ILike(c.Company, like))),
            q => q.OrderBy(c => c.Name).Select(c => new SearchHit("contacts", c.Id, c.Name, c.Company ?? c.Mobile, c.UpdatedAt, null, "/contacts/" + c.Id)));
        await Add("expenses", db.Expenses.Where(e => EF.Functions.ILike(e.Name, like) || EF.Functions.ILike(e.Category!.Name, like) || (e.Description != null && EF.Functions.ILike(e.Description, like)) || (e.Notes != null && EF.Functions.ILike(e.Notes, like))),
            q => q.OrderByDescending(e => e.Date).Select(e => new SearchHit("expenses", e.Id, e.Name, e.Category!.Name, e.Date.ToDateTime(TimeOnly.MinValue), e.Amount, "/finance/expenses?highlight=" + e.Id)));
        await Add("income", db.IncomeRecords.Where(e => EF.Functions.ILike(e.Title, like) || EF.Functions.ILike(e.Source, like) || (e.Description != null && EF.Functions.ILike(e.Description, like))),
            q => q.OrderByDescending(e => e.Date).Select(e => new SearchHit("income", e.Id, e.Title, e.Source, e.Date.ToDateTime(TimeOnly.MinValue), e.Amount, "/finance/income?highlight=" + e.Id)));
        await Add("documents", db.Documents.Where(d => EF.Functions.ILike(d.Name, like) || EF.Functions.ILike(d.Category, like) || (d.Description != null && EF.Functions.ILike(d.Description, like)) || d.Tags.Any(t => EF.Functions.ILike(t.Tag, like))),
            q => q.OrderByDescending(d => d.UpdatedAt).Select(d => new SearchHit("documents", d.Id, d.Name, d.Category + " · " + d.Extension.ToUpper(), d.UpdatedAt, null, "/documents?open=" + d.Id)));
        await Add("passwords", db.PasswordEntries.Where(e => EF.Functions.ILike(e.Title, like) || (e.WebsiteUrl != null && EF.Functions.ILike(e.WebsiteUrl, like)) || (e.ApplicationName != null && EF.Functions.ILike(e.ApplicationName, like)) || EF.Functions.ILike(e.Category, like) || (e.Tags != null && EF.Functions.ILike(e.Tags, like))),
            q => q.OrderBy(e => e.Title).Select(e => new SearchHit("passwords", e.Id, e.Title, e.WebsiteUrl ?? e.ApplicationName ?? e.Category, e.UpdatedAt, null, "/passwords?open=" + e.Id)));
        await Add("products", db.Products.Where(p => EF.Functions.ILike(p.ProductName, like) || (p.BrandName != null && EF.Functions.ILike(p.BrandName, like)) || (p.Composition != null && EF.Functions.ILike(p.Composition, like)) || (p.ProductCode != null && EF.Functions.ILike(p.ProductCode, like)) || (p.Manufacturer != null && EF.Functions.ILike(p.Manufacturer, like))),
            q => q.OrderBy(p => p.ProductName).Select(p => new SearchHit("products", p.Id, p.ProductName, p.BrandName ?? p.Manufacturer, p.UpdatedAt, p.Mrp, "/products/" + p.RegisterId + "?open=" + p.Id)));
        await Add("tables", db.CustomTableRecords.Where(r => r.Values.Any(v => v.TextValue != null && EF.Functions.ILike(v.TextValue, like)) || (r.Notes != null && EF.Functions.ILike(r.Notes, like))),
            q => q.OrderByDescending(r => r.UpdatedAt).Select(r => new SearchHit("tables", r.Id, r.Values.Where(v => v.TextValue != null && EF.Functions.ILike(v.TextValue, like)).Select(v => v.TextValue).FirstOrDefault() ?? "Record",
                r.Table!.Name, r.UpdatedAt, null, "/tables/" + r.TableId + "?open=" + r.Id)));
        await Add("udhar", db.CreditTransactions.Where(t => EF.Functions.ILike(t.PersonName, like) || (t.Description != null && EF.Functions.ILike(t.Description, like)) || (t.ContactNumber != null && EF.Functions.ILike(t.ContactNumber, like))),
            q => q.OrderByDescending(t => t.TransactionDate).Select(t => new SearchHit("udhar", t.Id, t.PersonName, (t.Direction == CreditDirection.Given ? "Given" : "Borrowed") + (t.Description != null ? " · " + t.Description : ""),
                t.TransactionDate.ToDateTime(TimeOnly.MinValue), t.Amount - t.SettledAmount, "/udhar?open=" + t.Id)));
        await Add("diary", db.DiaryEntries.Where(d => EF.Functions.ILike(d.Title, like) || d.Tags.Any(t => EF.Functions.ILike(t.Tag, like)) || (d.Mood != null && EF.Functions.ILike(d.Mood, like))),
            q => q.OrderByDescending(d => d.EntryDate).Select(d => new SearchHit("diary", d.Id, d.Title, d.Mood, d.EntryDate.ToDateTime(d.EntryTime), null, "/diary/" + d.Id)));
        await Add("events", db.CalendarEvents.Where(e => EF.Functions.ILike(e.Title, like) || (e.Description != null && EF.Functions.ILike(e.Description, like)) || (e.Location != null && EF.Functions.ILike(e.Location, like))),
            q => q.OrderByDescending(e => e.StartUtc).Select(e => new SearchHit("events", e.Id, e.Title, e.Location ?? e.EventType.ToString(), e.StartUtc, null, "/calendar?event=" + e.Id)));
        await Add("reminders", db.Reminders.Where(r => EF.Functions.ILike(r.Title, like) || (r.Notes != null && EF.Functions.ILike(r.Notes, like)) || EF.Functions.ILike(r.Category, like)),
            q => q.OrderBy(r => r.RemindAtUtc).Select(r => new SearchHit("reminders", r.Id, r.Title, r.Category, r.RemindAtUtc, null, "/reminders")));
        // Chat: only conversations the caller participates in; deleted / expired messages excluded.
        var now = clock.GetUtcNow().UtcDateTime;
        var myConversations = db.ChatParticipants.Where(p => p.UserId == uid).Select(p => p.ConversationId);
        await Add("chat", db.ChatMessages.Where(m => myConversations.Contains(m.ConversationId) && m.DeletedAt == null && (m.ExpiresAt == null || m.ExpiresAt > now) && m.Body != null && EF.Functions.ILike(m.Body, like)),
            q => q.OrderByDescending(m => m.CreatedAt).Select(m => new SearchHit("chat", m.Id, m.Body!.Length > 120 ? m.Body.Substring(0, 120) + "…" : m.Body,
                db.Users.Where(u => u.Id == m.SenderId).Select(u => u.FullName).FirstOrDefault(), m.CreatedAt, null, "/chat/" + m.ConversationId + "?message=" + m.Id)));

        await RememberAsync(query, ct);
        return new SearchResponse(query, groups.Sum(g => g.Count), groups, Math.Max(1, page), pageSize);
    }

    private async Task RememberAsync(string query, CancellationToken ct)
    {
        var existing = await db.RecentSearches.FirstOrDefaultAsync(r => r.Query.ToLower() == query.ToLower(), ct);
        if (existing != null) existing.CreatedAt = clock.GetUtcNow().UtcDateTime;
        else db.RecentSearches.Add(new RecentSearch { Query = query, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        var old = await db.RecentSearches.OrderByDescending(r => r.CreatedAt).Skip(15).ToListAsync(ct);
        if (old.Count > 0) { db.RecentSearches.RemoveRange(old); await db.SaveChangesAsync(ct); }
    }

    public async Task<IReadOnlyList<string>> RecentAsync(CancellationToken ct) =>
        await db.RecentSearches.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(10).Select(r => r.Query).ToListAsync(ct);

    public async Task ClearRecentAsync(CancellationToken ct) => await db.RecentSearches.ExecuteDeleteAsync(ct);
}
