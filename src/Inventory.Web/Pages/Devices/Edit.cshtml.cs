using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Devices;

public class EditModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly AuditService _audit;
    private readonly ICurrentUser _user;
    private readonly CustomFieldService _custom;

    public EditModel(InventoryDbContext db, AuditService audit, ICurrentUser user, CustomFieldService custom)
    {
        _db = db;
        _audit = audit;
        _user = user;
        _custom = custom;
    }

    public class InputModel
    {
        public int Id { get; set; }
        public int? DeviceTypeId { get; set; }
        [Required, StringLength(160)] public string Model { get; set; } = "";
        [StringLength(200)] public string? SerialNumber { get; set; }
        [StringLength(120)] public string? AssetTag { get; set; }
        public int? StatusId { get; set; }
        [StringLength(200)] public string? LocationWithinSite { get; set; }
        [StringLength(40)] public string? WindowsVersion { get; set; }
        public bool IsGrantFunded { get; set; }
        public bool RemovedFromInventory { get; set; }
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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var d = await _db.Devices.FindAsync(id);
        if (d is null) return NotFound();
        Input = new InputModel
        {
            Id = d.Id,
            DeviceTypeId = d.DeviceTypeId,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            AssetTag = d.AssetTag,
            StatusId = d.StatusId,
            LocationWithinSite = d.LocationWithinSite,
            WindowsVersion = d.WindowsVersion,
            IsGrantFunded = d.IsGrantFunded,
            RemovedFromInventory = d.RemovedFromInventory,
            AssignedUserId = d.AssignedUserId,
            SiteId = d.SiteId,
        };
        await LoadLookupsAsync();
        CustomValues = await _custom.GetValuesForAsync(CustomFieldEntityType.Device, d.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadLookupsAsync();
        if (!ModelState.IsValid) return Page();

        var d = await _db.Devices.FindAsync(Input.Id);
        if (d is null) return NotFound();

        if (string.IsNullOrWhiteSpace(Input.SerialNumber) && string.IsNullOrWhiteSpace(Input.AssetTag))
        {
            ModelState.AddModelError("", "Provide at least a Serial Number or an Asset Tag.");
            CustomValues = CustomFieldValues;
            return Page();
        }

        var changes = new Dictionary<string, (object?, object?)>();
        void Track(string field, object? before, object? after)
        {
            if (!Equals(before, after)) changes[field] = (before, after);
        }

        Track(nameof(Device.DeviceTypeId), d.DeviceTypeId, Input.DeviceTypeId);
        Track(nameof(Device.Model), d.Model, Input.Model);
        Track(nameof(Device.SerialNumber), d.SerialNumber, Input.SerialNumber);
        Track(nameof(Device.AssetTag), d.AssetTag, Input.AssetTag);
        Track(nameof(Device.StatusId), d.StatusId, Input.StatusId);
        Track(nameof(Device.LocationWithinSite), d.LocationWithinSite, Input.LocationWithinSite);
        Track(nameof(Device.WindowsVersion), d.WindowsVersion, Input.WindowsVersion);
        Track(nameof(Device.IsGrantFunded), d.IsGrantFunded, Input.IsGrantFunded);
        Track(nameof(Device.RemovedFromInventory), d.RemovedFromInventory, Input.RemovedFromInventory);
        Track(nameof(Device.AssignedUserId), d.AssignedUserId, Input.AssignedUserId);
        Track(nameof(Device.SiteId), d.SiteId, Input.SiteId);

        d.DeviceTypeId = Input.DeviceTypeId;
        d.Model = Input.Model.Trim();
        d.SerialNumber = Input.SerialNumber?.Trim();
        d.AssetTag = Input.AssetTag?.Trim();
        d.StatusId = Input.StatusId;
        d.LocationWithinSite = Input.LocationWithinSite?.Trim();
        d.WindowsVersion = Input.WindowsVersion?.Trim();
        d.IsGrantFunded = Input.IsGrantFunded;
        d.RemovedFromInventory = Input.RemovedFromInventory;
        d.AssignedUserId = Input.AssignedUserId;
        d.SiteId = Input.SiteId;
        d.LastModifiedUtc = DateTime.UtcNow;
        d.LastModifiedBy = _user.Name;

        if (changes.Count > 0)
            _audit.Record(d, AuditAction.Updated, changes);

        await _db.SaveChangesAsync();
        await _custom.SaveValuesAsync(CustomFieldEntityType.Device, d.Id, CustomFieldValues);

        TempData["Message"] = "Device updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var d = await _db.Devices.FindAsync(Input.Id);
        if (d is null) return NotFound();
        _audit.Record(d, AuditAction.Deleted);
        _db.Devices.Remove(d);
        await _db.SaveChangesAsync();
        TempData["Message"] = "Device deleted.";
        return RedirectToPage("Index");
    }

    private async Task LoadLookupsAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        DeviceTypes = await _db.DeviceTypeOptions.Where(t => t.IsActive).OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name).ToListAsync();
        Statuses = await _db.DeviceStatusOptions.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync();
        CustomDefs = await _custom.GetActiveDefinitionsAsync(CustomFieldEntityType.Device);

        if (Input.AssignedUserId is not null)
        {
            AssignedUserName = await _db.UserProfiles
                .Where(u => u.Id == Input.AssignedUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
        }
    }
}
