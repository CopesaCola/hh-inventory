using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Users;

public class DetailsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public DetailsModel(InventoryDbContext db) => _db = db;

    public UserProfile? User { get; set; }

    public async Task OnGetAsync(int id)
    {
        User = await _db.UserProfiles
            .Include(u => u.Site)
            .Include(u => u.Department)
            .Include(u => u.Suite)
            .Include(u => u.Devices).ThenInclude(d => d.Site)
            .Include(u => u.Devices).ThenInclude(d => d.DeviceType)
            .Include(u => u.Devices).ThenInclude(d => d.Status)
            .FirstOrDefaultAsync(u => u.Id == id);
    }
}
