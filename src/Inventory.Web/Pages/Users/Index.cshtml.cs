using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Users;

public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SiteId { get; set; }

    public List<UserProfile> Users { get; set; } = new();
    public List<Site> Sites { get; set; } = new();

    public async Task OnGetAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        Users = await BuildQuery().OrderBy(u => u.FullName).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var users = await BuildQuery().OrderBy(u => u.FullName).ToListAsync();
        var rows = users.Select(u => new
        {
            FullName = u.FullName,
            Username = u.Username ?? "",
            Email = u.Email ?? "",
            Department = u.Department?.Name ?? "",
            Site = u.Site?.Name ?? "",
            DeviceCount = u.Devices.Count,
        });
        var bytes = CsvExporter.Build(rows);
        var name = CsvExporter.MakeFilename("users", Query);
        return File(bytes, "text/csv", name);
    }

    private IQueryable<UserProfile> BuildQuery()
    {
        var q = _db.UserProfiles
            .Include(u => u.Site)
            .Include(u => u.Department)
            .Include(u => u.Devices)
            .AsQueryable();

        if (SiteId is not null) q = q.Where(u => u.SiteId == SiteId);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            var like = $"%{term}%";
            q = q.Where(u =>
                EF.Functions.Like(u.FullName, like) ||
                EF.Functions.Like(u.Username ?? "", like) ||
                EF.Functions.Like(u.Email ?? "", like) ||
                (u.Department != null && EF.Functions.Like(u.Department.Name, like)));
        }
        return q;
    }
}
