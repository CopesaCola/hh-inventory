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

    public async Task OnGetAsync(int? userId = null, int? siteId = null, int? suiteId = null)
    {
        await LoadLookupsAsync();
        if (userId is not null) Input.AssignedUserId = userId;
        if (siteId is not null) Input.SiteId = siteId;
        if (suiteId is not null) Input.SuiteId = suiteId;
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

        // Reconcile Suite ↔ Site. A suite belongs to a site; if the form is in a
        // weird state (suite from a different site, or suite chosen with no site),
        // silently fix it rather than rejecting — the JS dropdown filter usually
        // prevents this from being reachable.
        var (siteId, suiteId) = ReconcileSiteAndSuite(Input.SiteId, Input.SuiteId);

        var now = DateTime.UtcNow;
        var device = new Device
        {
            DeviceTypeId = Input.DeviceTypeId,
            Model = Input.Model.Trim(),
            SerialNumber = Input.SerialNumber?.Trim(),
            AssetTag = Input.AssetTag?.Trim(),
            StatusId = Input.StatusId,
            WindowsVersion = Input.WindowsVersion?.Trim(),
            IsGrantFunded = Input.IsGrantFunded,
            GrantOrDeptFund = Input.IsGrantFunded ? Input.GrantOrDeptFund?.Trim() : null,
            AssignedUserId = Input.AssignedUserId,
            SiteId = siteId,
            SuiteId = suiteId,
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
        Suites = await _db.UserProfiles
            .Where(u => u.Kind == UserKind.Suite)
            .Include(u => u.Site)
            .OrderBy(u => u.FullName)
            .ToListAsync();
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

    /// <summary>
    /// If a Suite was chosen, make sure it lives at the chosen Site:
    ///  - SuiteId not in our loaded list → drop it (no such suite)
    ///  - SuiteId set, SiteId null     → auto-fill SiteId from the suite
    ///  - SuiteId set, mismatched site → drop SuiteId (Site wins)
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
