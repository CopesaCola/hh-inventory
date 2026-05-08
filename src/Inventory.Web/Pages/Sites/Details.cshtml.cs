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

    public async Task OnGetAsync(int id)
    {
        Site = await _db.Sites
            .Include(s => s.Users).ThenInclude(u => u.Devices)
            .Include(s => s.Users).ThenInclude(u => u.Department)
            .Include(s => s.Devices).ThenInclude(d => d.AssignedUser)
            .Include(s => s.Devices).ThenInclude(d => d.DeviceType)
            .Include(s => s.Devices).ThenInclude(d => d.Status)
            .FirstOrDefaultAsync(s => s.Id == id);
    }
}
