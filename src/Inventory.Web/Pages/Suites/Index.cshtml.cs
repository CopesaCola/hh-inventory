using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Suites;

/// <summary>
/// Suites are stored as UserProfile rows with Kind = Suite. This page is the
/// management view for those rows; everything else (the /Users index, the
/// /Sites/Details "Users" section, etc.) filters Kind = Person.
/// </summary>
public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SiteId { get; set; }

    public List<UserProfile> Suites { get; set; } = new();
    public List<Site> Sites { get; set; } = new();

    public async Task OnGetAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        Suites = await BuildQuery().OrderBy(s => s.FullName).ToListAsync();
    }

    private IQueryable<UserProfile> BuildQuery()
    {
        var q = _db.UserProfiles
            .Where(u => u.Kind == UserKind.Suite)
            .Include(u => u.Site)
            .Include(u => u.Devices)
            .Include(u => u.Members)
            .AsQueryable();

        if (SiteId is not null) q = q.Where(u => u.SiteId == SiteId);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var like = $"%{Query.Trim()}%";
            q = q.Where(u =>
                EF.Functions.Like(u.FullName, like) ||
                (u.Site != null && EF.Functions.Like(u.Site.Name, like)));
        }
        return q;
    }
}
