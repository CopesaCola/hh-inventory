using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Sites;

public class EditModel : PageModel
{
    private readonly InventoryDbContext _db;
    public EditModel(InventoryDbContext db) => _db = db;

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(120)] public string Name { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var s = await _db.Sites.FindAsync(id);
        if (s is null) return NotFound();
        Input = new InputModel { Id = s.Id, Name = s.Name };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var s = await _db.Sites.FindAsync(Input.Id);
        if (s is null) return NotFound();

        if (s.Name != Input.Name && await _db.Sites.AnyAsync(x => x.Name == Input.Name && x.Id != s.Id))
        {
            ModelState.AddModelError("Input.Name", "Another site already uses this name.");
            return Page();
        }

        s.Name = Input.Name.Trim();
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Site {s.Name} updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var s = await _db.Sites.FindAsync(Input.Id);
        if (s is null) return NotFound();
        _db.Sites.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Site {s.Name} deleted.";
        return RedirectToPage("Index");
    }
}
