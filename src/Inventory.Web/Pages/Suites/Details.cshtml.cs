using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Suites;

public class DetailsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public DetailsModel(InventoryDbContext db) => _db = db;

    public UserProfile? Suite { get; set; }

    /// <summary>Devices located in this suite (Device.SuiteId == suite.Id).</summary>
    public List<Device> Devices { get; set; } = new();

    public async Task OnGetAsync(int id)
    {
        Suite = await _db.UserProfiles
            .Where(u => u.Id == id && u.Kind == UserKind.Suite)
            .Include(u => u.Site)
            .Include(u => u.Members).ThenInclude(m => m.Devices)
            .FirstOrDefaultAsync();

        if (Suite is null) return;

        // Devices are linked to a suite via Device.SuiteId (a separate field
        // from AssignedUserId) — that's the explicit "located here" link.
        Devices = await _db.Devices
            .Where(d => d.SuiteId == id)
            .Include(d => d.DeviceType)
            .Include(d => d.Status)
            .Include(d => d.AssignedUser)
            .OrderBy(d => d.DeviceType != null ? d.DeviceType.Name : "")
            .ThenBy(d => d.Model)
            .ToListAsync();
    }
}
