using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Users;

public class EditModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly CustomFieldService _custom;
    public EditModel(InventoryDbContext db, CustomFieldService custom)
    {
        _db = db;
        _custom = custom;
    }

    public class InputModel
    {
        public int Id { get; set; }
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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var u = await _db.UserProfiles.FirstOrDefaultAsync(x => x.Id == id && x.Kind == UserKind.Person);
        if (u is null) return NotFound();
        Input = new InputModel
        {
            Id = u.Id,
            FullName = u.FullName,
            Username = u.Username,
            Email = u.Email,
            DepartmentId = u.DepartmentId,
            SiteId = u.SiteId,
            SuiteId = u.SuiteId,
        };
        await LoadAsync();
        CustomValues = await _custom.GetValuesForAsync(CustomFieldEntityType.UserProfile, u.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) { CustomValues = CustomFieldValues; return Page(); }

        var u = await _db.UserProfiles.FirstOrDefaultAsync(x => x.Id == Input.Id && x.Kind == UserKind.Person);
        if (u is null) return NotFound();

        // Drop a SuiteId that doesn't match the chosen Site.
        var suiteId = Input.SuiteId;
        if (suiteId is not null)
        {
            var ok = Suites.Any(s => s.Id == suiteId && s.SiteId == Input.SiteId);
            if (!ok) suiteId = null;
        }

        u.FullName = Input.FullName.Trim();
        u.Username = Input.Username?.Trim();
        u.Email = Input.Email?.Trim();
        u.DepartmentId = Input.DepartmentId;
        u.SiteId = Input.SiteId;
        u.SuiteId = suiteId;

        await _db.SaveChangesAsync();
        await _custom.SaveValuesAsync(CustomFieldEntityType.UserProfile, u.Id, CustomFieldValues);

        TempData["Message"] = $"User {u.FullName} updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var u = await _db.UserProfiles.FirstOrDefaultAsync(x => x.Id == Input.Id && x.Kind == UserKind.Person);
        if (u is null) return NotFound();
        _db.UserProfiles.Remove(u);
        await _db.SaveChangesAsync();
        TempData["Message"] = $"User {u.FullName} deleted.";
        return RedirectToPage("Index");
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
