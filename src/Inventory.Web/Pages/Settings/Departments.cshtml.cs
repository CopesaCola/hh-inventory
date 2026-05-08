using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

public class DepartmentsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public DepartmentsModel(InventoryDbContext db) => _db = db;

    public class RowInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty] public List<RowInput> Items { get; set; } = new();
    [BindProperty] public int? DeleteId { get; set; }
    [BindProperty] public string? NewName { get; set; }

    public Dictionary<int, int> UsageCount { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadUsageAsync();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Items)
        {
            var trimmed = (row.Name ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                TempData["Error"] = "All rows must have a name.";
                await LoadAsync();
                return Page();
            }
            if (!seen.Add(trimmed))
            {
                TempData["Error"] = $"Duplicate name '{trimmed}' in submitted rows.";
                await LoadAsync();
                return Page();
            }
        }

        var existing = await _db.DepartmentOptions.ToListAsync();
        var byId = existing.ToDictionary(x => x.Id);
        foreach (var row in Items)
        {
            if (!byId.TryGetValue(row.Id, out var entity)) continue;
            entity.Name = row.Name.Trim();
            entity.DisplayOrder = row.DisplayOrder;
            entity.IsActive = row.IsActive;
        }

        if (DeleteId is int delId && byId.TryGetValue(delId, out var toRemove))
        {
            // FK has SetNull, so user.DepartmentId will null out automatically
            _db.DepartmentOptions.Remove(toRemove);
        }

        try
        {
            await _db.SaveChangesAsync();
            TempData["Message"] = "Saved.";
        }
        catch (DbUpdateException ex)
        {
            TempData["Error"] = "Save failed: " + (ex.InnerException?.Message ?? ex.Message);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var trimmed = (NewName ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            TempData["Error"] = "Name is required.";
            return RedirectToPage();
        }
        if (await _db.DepartmentOptions.AnyAsync(d => d.Name == trimmed))
        {
            TempData["Error"] = "A department with this name already exists.";
            return RedirectToPage();
        }
        _db.DepartmentOptions.Add(new DepartmentOption { Name = trimmed, DisplayOrder = 9999 });
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Added '{trimmed}'.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var rows = await _db.DepartmentOptions
            .OrderBy(d => d.DisplayOrder).ThenBy(d => d.Name)
            .ToListAsync();
        Items = rows.Select(d => new RowInput
        {
            Id = d.Id,
            Name = d.Name,
            DisplayOrder = d.DisplayOrder,
            IsActive = d.IsActive,
        }).ToList();
        await LoadUsageAsync();
    }

    private async Task LoadUsageAsync()
    {
        UsageCount = await _db.UserProfiles
            .Where(u => u.DepartmentId != null)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }
}
