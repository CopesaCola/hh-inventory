using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages;

public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    public int DeviceCount { get; set; }
    public int UserCount { get; set; }
    public int SiteCount { get; set; }
    public List<AuditEntry> Recent { get; set; } = new();

    public async Task OnGetAsync()
    {
        DeviceCount = await _db.Devices.CountAsync();
        UserCount = await _db.UserProfiles.CountAsync();
        SiteCount = await _db.Sites.CountAsync();
        Recent = await _db.AuditEntries
            .Include(a => a.Device)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(20)
            .ToListAsync();
    }
}
