using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Devices;

public class ReassignModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly AuditService _audit;
    private readonly ICurrentUser _user;

    public ReassignModel(InventoryDbContext db, AuditService audit, ICurrentUser user)
    {
        _db = db;
        _audit = audit;
        _user = user;
    }

    public Device? Device { get; set; }

    [BindProperty] public int DeviceId { get; set; }
    [BindProperty] public int? AssignedUserId { get; set; }
    [BindProperty] public int? SiteId { get; set; }

    // For "return to where I came from"
    [BindProperty(SupportsGet = true)] public int? ReturnUserId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ReturnSiteId { get; set; }

    public string? AssignedUserName { get; set; }
    public List<Site> Sites { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Device = await _db.Devices
            .Include(d => d.AssignedUser)
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (Device is null) return NotFound();

        DeviceId = Device.Id;
        AssignedUserId = Device.AssignedUserId;
        SiteId = Device.SiteId;
        await LoadLookupsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var d = await _db.Devices.FindAsync(DeviceId);
        if (d is null) return NotFound();

        var changes = new Dictionary<string, (object?, object?)>();
        if (d.AssignedUserId != AssignedUserId)
            changes[nameof(Device.AssignedUserId)] = (d.AssignedUserId, AssignedUserId);
        if (d.SiteId != SiteId)
            changes[nameof(Device.SiteId)] = (d.SiteId, SiteId);

        d.AssignedUserId = AssignedUserId;
        d.SiteId = SiteId;
        d.LastModifiedUtc = DateTime.UtcNow;
        d.LastModifiedBy = _user.Name;

        if (changes.Count > 0)
            _audit.Record(d, AuditAction.Updated, changes);

        await _db.SaveChangesAsync();
        TempData["Message"] = "Device reassigned.";

        if (ReturnUserId is not null)
            return RedirectToPage("/Users/Details", new { id = ReturnUserId });
        if (ReturnSiteId is not null)
            return RedirectToPage("/Sites/Details", new { id = ReturnSiteId });
        return RedirectToPage("Details", new { id = d.Id });
    }

    private async Task LoadLookupsAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();

        if (AssignedUserId is not null)
        {
            AssignedUserName = await _db.UserProfiles
                .Where(u => u.Id == AssignedUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
        }
    }
}
