using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Suites;

public class CreateModel : PageModel
{
    private readonly InventoryDbContext _db;
    public CreateModel(InventoryDbContext db) => _db = db;

    public class InputModel
    {
        [Required, StringLength(150)] public string Name { get; set; } = "";
        [Required] public int? SiteId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<Site> Sites { get; set; } = new();

    public async Task OnGetAsync(int? siteId = null)
    {
        await LoadAsync();
        if (siteId is not null) Input.SiteId = siteId;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) return Page();

        // Suite name should be unique within a site so users can pick reliably.
        var dup = await _db.UserProfiles
            .AnyAsync(u => u.Kind == UserKind.Suite
                        && u.SiteId == Input.SiteId
                        && u.FullName == Input.Name);
        if (dup)
        {
            ModelState.AddModelError("Input.Name", "Another suite at this site already uses that name.");
            return Page();
        }

        var suite = new UserProfile
        {
            Kind = UserKind.Suite,
            FullName = Input.Name.Trim(),
            SiteId = Input.SiteId,
            // Username/Email/Department deliberately not used for Suites.
        };
        _db.UserProfiles.Add(suite);
        await _db.SaveChangesAsync();

        TempData["Message"] = $"Suite {suite.FullName} added.";
        return RedirectToPage("Details", new { id = suite.Id });
    }

    private async Task LoadAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
    }
}
