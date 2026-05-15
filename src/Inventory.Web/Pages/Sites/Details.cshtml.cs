using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Sites;

public class DetailsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public DetailsModel(InventoryDbContext db) => _db = db;

    public Site? Site { get; set; }

    /// <summary>Persons at this site (Kind = Person). Suites are split into <see cref="Suites"/>.</summary>
    public List<UserProfile> Persons { get; set; } = new();

    /// <summary>Suites at this site (Kind = Suite) with their devices and members preloaded.</summary>
    public List<UserProfile> Suites { get; set; } = new();

    public async Task OnGetAsync(int id)
    {
        Site = await _db.Sites
            .Include(s => s.Devices).ThenInclude(d => d.AssignedUser)
            .Include(s => s.Devices).ThenInclude(d => d.DeviceType)
            .Include(s => s.Devices).ThenInclude(d => d.Status)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (Site is null) return;

        // Split UserProfile rows at this site by Kind so the page can render two distinct tables.
        Persons = await _db.UserProfiles
            .Where(u => u.SiteId == id && u.Kind == UserKind.Person)
            .Include(u => u.Department)
            .Include(u => u.Suite)
            .Include(u => u.Devices)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        Suites = await _db.UserProfiles
            .Where(u => u.SiteId == id && u.Kind == UserKind.Suite)
            .Include(u => u.Members)
            .Include(u => u.Devices)
            .OrderBy(u => u.FullName)
            .ToListAsync();
    }
}
