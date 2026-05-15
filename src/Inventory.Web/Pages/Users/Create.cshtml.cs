using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Users;

public class CreateModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly CustomFieldService _custom;
    public CreateModel(InventoryDbContext db, CustomFieldService custom)
    {
        _db = db;
        _custom = custom;
    }

    public class InputModel
    {
        [Required, StringLength(150)] public string FullName { get; set; } = "";
        [StringLength(100)] public string? Username { get; set; }
        [StringLength(150), EmailAddress] public string? Email { get; set; }
        public int? DepartmentId { get; set; }
        public int? SiteId { get; set; }
        public int? SuiteId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> CustomFieldValues { get; set; } = new();

    public List<Site> Sites { get; set; } = new();
    public List<DepartmentOption> Departments { get; set; } = new();
    public List<UserProfile> Suites { get; set; } = new();
    public List<CustomFieldDefinition> CustomDefs { get; set; } = new();
    public Dictionary<string, string?> CustomValues { get; set; } = new();

    public async Task OnGetAsync(int? siteId = null, int? suiteId = null)
    {
        await LoadAsync();
        if (siteId is not null) Input.SiteId = siteId;
        if (suiteId is not null) Input.SuiteId = suiteId;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) { CustomValues = CustomFieldValues; return Page(); }

        // Drop a SuiteId that doesn't match the chosen Site (defense in depth —
        // the UI also filters the dropdown by site).
        var suiteId = Input.SuiteId;
        if (suiteId is not null)
        {
            var ok = Suites.Any(s => s.Id == suiteId && s.SiteId == Input.SiteId);
            if (!ok) suiteId = null;
        }

        var newUser = new UserProfile
        {
            Kind = UserKind.Person,
            FullName = Input.FullName.Trim(),
            Username = Input.Username?.Trim(),
            Email = Input.Email?.Trim(),
            DepartmentId = Input.DepartmentId,
            SiteId = Input.SiteId,
            SuiteId = suiteId,
        };
        _db.UserProfiles.Add(newUser);
        await _db.SaveChangesAsync();
        await _custom.SaveValuesAsync(CustomFieldEntityType.UserProfile, newUser.Id, CustomFieldValues);

        TempData["Message"] = $"User {Input.FullName} added.";
        return RedirectToPage("Details", new { id = newUser.Id });
    }

    private async Task LoadAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        Departments = await _db.DepartmentOptions.Where(d => d.IsActive).OrderBy(d => d.DisplayOrder).ThenBy(d => d.Name).ToListAsync();
        Suites = await _db.UserProfiles
            .Where(u => u.Kind == UserKind.Suite)
            .Include(u => u.Site)
            .OrderBy(u => u.FullName)
            .ToListAsync();
        CustomDefs = await _custom.GetActiveDefinitionsAsync(CustomFieldEntityType.UserProfile);
    }
}
