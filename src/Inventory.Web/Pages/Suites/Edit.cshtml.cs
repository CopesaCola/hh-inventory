using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Suites;

public class EditModel : PageModel
{
    private readonly InventoryDbContext _db;
    public EditModel(InventoryDbContext db) => _db = db;

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(150)] public string Name { get; set; } = "";
        [Required] public int? SiteId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<Site> Sites { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var s = await _db.UserProfiles.FirstOrDefaultAsync(u => u.Id == id && u.Kind == UserKind.Suite);
        if (s is null) return NotFound();
        Input = new InputModel { Id = s.Id, Name = s.FullName, SiteId = s.SiteId };
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) return Page();

        var s = await _db.UserProfiles.FirstOrDefaultAsync(u => u.Id == Input.Id && u.Kind == UserKind.Suite);
        if (s is null) return NotFound();

        var dup = await _db.UserProfiles
            .AnyAsync(u => u.Kind == UserKind.Suite
                        && u.SiteId == Input.SiteId
                        && u.FullName == Input.Name
                        && u.Id != Input.Id);
        if (dup)
        {
            ModelState.AddModelError("Input.Name", "Another suite at this site already uses that name.");
            return Page();
        }

        // If the suite moves to a different site, drop any persons that were
        // members at the old site — their SuiteId no longer makes sense.
        if (s.SiteId != Input.SiteId)
        {
            var members = await _db.UserProfiles
                .Where(u => u.SuiteId == s.Id)
                .ToListAsync();
            foreach (var m in members) m.SuiteId = null;
        }

        s.FullName = Input.Name.Trim();
        s.SiteId = Input.SiteId;
        await _db.SaveChangesAsync();

        TempData["Message"] = $"Suite {s.FullName} updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var s = await _db.UserProfiles.FirstOrDefaultAsync(u => u.Id == Input.Id && u.Kind == UserKind.Suite);
        if (s is null) return NotFound();
        // EF cascade settings: persons in the suite have SuiteId set null;
        // devices assigned to the suite have AssignedUserId set null. So
        // deleting a Suite only removes the suite row itself.
        _db.UserProfiles.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Suite {s.FullName} deleted.";
        return RedirectToPage("Index");
    }

    private async Task LoadAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
    }
}
