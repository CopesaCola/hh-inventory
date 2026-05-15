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
        [StringLength(40)] public string? WindowsVersion { get; set; }
        public bool IsGrantFunded { get; set; }
        [StringLength(200)] public string? GrantOrDeptFund { get; set; }
        public int? AssignedUserId { get; set; }
        public int? SiteId { get; set; }
        public int? SuiteId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> CustomFieldValues { get; set; } = new();

    public string? AssignedUserName { get; set; }
    public List<Site> Sites { get; set; } = new();
    public List<UserProfile> Suites { get; set; } = new();
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
            WindowsVersion = d.WindowsVersion,
            IsGrantFunded = d.IsGrantFunded,
            GrantOrDeptFund = d.GrantOrDeptFund,
            AssignedUserId = d.AssignedUserId,
            SiteId = d.SiteId,
            SuiteId = d.SuiteId,
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
        Track(nameof(Device.WindowsVersion), d.WindowsVersion, Input.WindowsVersion);
        Track(nameof(Device.IsGrantFunded), d.IsGrantFunded, Input.IsGrantFunded);
        var newGrantFund = Input.IsGrantFunded ? Input.GrantOrDeptFund?.Trim() : null;
        Track(nameof(Device.GrantOrDeptFund), d.GrantOrDeptFund, newGrantFund);
        Track(nameof(Device.AssignedUserId), d.AssignedUserId, Input.AssignedUserId);
        // Drop a Suite that doesn't live at the chosen Site (auto-fill Site
        // from Suite when no Site was picked). Same reconcile rule as Create.
        var (siteId, suiteId) = ReconcileSiteAndSuite(Input.SiteId, Input.SuiteId);
        Track(nameof(Device.SiteId), d.SiteId, siteId);
        Track(nameof(Device.SuiteId), d.SuiteId, suiteId);

        d.DeviceTypeId = Input.DeviceTypeId;
        d.Model = Input.Model.Trim();
        d.SerialNumber = Input.SerialNumber?.Trim();
        d.AssetTag = Input.AssetTag?.Trim();
        d.StatusId = Input.StatusId;
        d.WindowsVersion = Input.WindowsVersion?.Trim();
        d.IsGrantFunded = Input.IsGrantFunded;
        d.GrantOrDeptFund = newGrantFund;
        d.AssignedUserId = Input.AssignedUserId;
        d.SiteId = siteId;
        d.SuiteId = suiteId;
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
        Suites = await _db.UserProfiles
            .Where(u => u.Kind == UserKind.Suite)
            .Include(u => u.Site)
            .OrderBy(u => u.FullName)
            .ToListAsync();
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

    /// <summary>
    /// Suite belongs to a site. If form is in a weird state, fix silently:
    ///  - SuiteId not in our loaded list → drop it
    ///  - SuiteId set but SiteId null    → fill SiteId from the suite
    ///  - SuiteId set with mismatched site → drop SuiteId (Site wins)
    /// </summary>
    private (int? siteId, int? suiteId) ReconcileSiteAndSuite(int? siteId, int? suiteId)
    {
        if (suiteId is null) return (siteId, null);
        var suite = Suites.FirstOrDefault(s => s.Id == suiteId);
        if (suite is null) return (siteId, null);
        if (siteId is null) return (suite.SiteId, suite.Id);
        if (suite.SiteId != siteId) return (siteId, null);
        return (siteId, suiteId);
    }
}
