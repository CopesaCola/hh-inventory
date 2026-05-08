using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Sites;

public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public List<Site> Sites { get; set; } = new();

    public async Task OnGetAsync()
    {
        Sites = await BuildQuery().OrderBy(s => s.Name).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var sites = await BuildQuery().OrderBy(s => s.Name).ToListAsync();
        var rows = sites.Select(s => new
        {
            Name = s.Name,
            Address = s.Address ?? "",
            UserCount = s.Users.Count,
            DeviceCount = s.Devices.Count,
        });
        var bytes = CsvExporter.Build(rows);
        var name = CsvExporter.MakeFilename("sites", Query);
        return File(bytes, "text/csv", name);
    }

    private IQueryable<Site> BuildQuery()
    {
        var q = _db.Sites
            .Include(s => s.Users)
            .Include(s => s.Devices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            var like = $"%{term}%";
            q = q.Where(s =>
                EF.Functions.Like(s.Name, like) ||
                EF.Functions.Like(s.Address ?? "", like));
        }
        return q;
    }
}
