using System.ComponentModel.DataAnnotations;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Sites;

public class CreateModel : PageModel
{
    private readonly InventoryDbContext _db;
    public CreateModel(InventoryDbContext db) => _db = db;

    public class InputModel
    {
        [Required, StringLength(120)] public string Name { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (await _db.Sites.AnyAsync(s => s.Name == Input.Name))
        {
            ModelState.AddModelError("Input.Name", "A site with this name already exists.");
            return Page();
        }

        var newSite = new Site { Name = Input.Name.Trim() };
        _db.Sites.Add(newSite);
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Site {Input.Name} added.";
        return RedirectToPage("Details", new { id = newSite.Id });
    }
}
