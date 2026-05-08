using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Devices;

public class DetailsModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly CustomFieldService _custom;
    public DetailsModel(InventoryDbContext db, CustomFieldService custom)
    {
        _db = db;
        _custom = custom;
    }

    public Device? Device { get; set; }
    public List<CustomFieldDefinition> CustomDefs { get; set; } = new();
    public Dictionary<string, string?> CustomValues { get; set; } = new();

    public async Task OnGetAsync(int id)
    {
        Device = await _db.Devices
            .Include(d => d.AssignedUser)
            .Include(d => d.Site)
            .Include(d => d.DeviceType)
            .Include(d => d.Status)
            .Include(d => d.AuditEntries)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (Device is not null)
        {
            CustomDefs = await _custom.GetActiveDefinitionsAsync(CustomFieldEntityType.Device);
            CustomValues = await _custom.GetValuesForAsync(CustomFieldEntityType.Device, Device.Id);
        }
    }
}
