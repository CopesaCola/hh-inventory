using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Devices;

public class CreateModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly AuditService _audit;
    private readonly ICurrentUser _user;
    private readonly CustomFieldService _custom;

    public CreateModel(InventoryDbContext db, AuditService audit, ICurrentUser user, CustomFieldService custom)
    {
        _db = db;
        _audit = audit;
        _user = user;
        _custom = custom;
    }

    public class InputModel
    {
        public int? DeviceTypeId { get; set; }
        [Required, StringLength(160)] public string Model { get; set; } = "";
        [StringLength(200)] public string? SerialNumber { get; set; }
        [StringLength(120)] public string? AssetTag { get; set; }
        public int? StatusId { get; set; }
        [StringLength(200)] public string? LocationWithinSite { get; set; }
        [StringLength(40)] public string? WindowsVersion { get; set; }
        public bool IsGrantFunded { get; set; }
        public int? AssignedUserId { get; set; }
        public int? SiteId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> CustomFieldValues { get; set; } = new();

    public string? AssignedUserName { get; set; }
    public List<Site> Sites { get; set; } = new();
    public List<DeviceTypeOption> DeviceTypes { get; set; } = new();
    public List<DeviceStatusOption> Statuses { get; set; } = new();
    public List<CustomFieldDefinition> CustomDefs { get; set; } = new();
    public Dictionary<string, string?> CustomValues { get; set; } = new();

    public async Task OnGetAsync(int? userId = null, int? siteId = null)
    {
        await LoadLookupsAsync();
        if (userId is not null) Input.AssignedUserId = userId;
        if (siteId is not null) Input.SiteId = siteId;
        Input.StatusId ??= Statuses.FirstOrDefault()?.Id;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadLookupsAsync();
        if (!ModelState.IsValid) return Page();

        if (string.IsNullOrWhiteSpace(Input.SerialNumber) && string.IsNullOrWhiteSpace(Input.AssetTag))
        {
            ModelState.AddModelError("", "Provide at least a Serial Number or an Asset Tag.");
            return Page();
        }

        var now = DateTime.UtcNow;
        var device = new Device
        {
            DeviceTypeId = Input.DeviceTypeId,
            Model = Input.Model.Trim(),
            SerialNumber = Input.SerialNumber?.Trim(),
            AssetTag = Input.AssetTag?.Trim(),
            StatusId = Input.StatusId,
            LocationWithinSite = Input.LocationWithinSite?.Trim(),
            WindowsVersion = Input.WindowsVersion?.Trim(),
            IsGrantFunded = Input.IsGrantFunded,
            AssignedUserId = Input.AssignedUserId,
            SiteId = Input.SiteId,
            CreatedUtc = now,
            CreatedBy = _user.Name,
            LastModifiedUtc = now,
            LastModifiedBy = _user.Name,
        };

        _db.Devices.Add(device);
        _audit.Record(device, AuditAction.Created);
        await _db.SaveChangesAsync();

        await _custom.SaveValuesAsync(CustomFieldEntityType.Device, device.Id, CustomFieldValues);

        TempData["Message"] = "Device added.";
        if (Input.AssignedUserId is not null)
            return RedirectToPage("/Users/Details", new { id = Input.AssignedUserId });
        if (Input.SiteId is not null)
            return RedirectToPage("/Sites/Details", new { id = Input.SiteId });
        return RedirectToPage("Index");
    }

    private async Task LoadLookupsAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        DeviceTypes = await _db.DeviceTypeOptions.Where(t => t.IsActive).OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name).ToListAsync();
        Statuses = await _db.DeviceStatusOptions.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync();
        CustomDefs = await _custom.GetActiveDefinitionsAsync(CustomFieldEntityType.Device);
        CustomValues = CustomFieldValues.Count > 0 ? CustomFieldValues : new();

        if (Input.AssignedUserId is not null)
        {
            AssignedUserName = await _db.UserProfiles
                .Where(u => u.Id == Input.AssignedUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
        }
    }
}
