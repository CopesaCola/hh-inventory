using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

public class DeviceTypesModel : PageModel
{
    private readonly InventoryDbContext _db;
    public DeviceTypesModel(InventoryDbContext db) => _db = db;

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

        // Validate names are unique among submitted rows
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

        // Apply edits
        var existing = await _db.DeviceTypeOptions.ToListAsync();
        var byId = existing.ToDictionary(x => x.Id);
        foreach (var row in Items)
        {
            if (!byId.TryGetValue(row.Id, out var entity)) continue;
            entity.Name = row.Name.Trim();
            entity.DisplayOrder = row.DisplayOrder;
            entity.IsActive = row.IsActive;
        }

        // Apply delete
        string? warning = null;
        if (DeleteId is int delId)
        {
            var inUse = await _db.Devices.AnyAsync(d => d.DeviceTypeId == delId);
            if (inUse)
            {
                warning = "Couldn't delete: that type is still used by devices. Pending edits saved.";
            }
            else if (byId.TryGetValue(delId, out var toRemove))
            {
                _db.DeviceTypeOptions.Remove(toRemove);
            }
        }

        try
        {
            await _db.SaveChangesAsync();
            TempData["Message"] = warning ?? "Saved.";
            if (warning is not null) TempData["Error"] = warning;
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
        if (await _db.DeviceTypeOptions.AnyAsync(t => t.Name == trimmed))
        {
            TempData["Error"] = "A device type with this name already exists.";
            return RedirectToPage();
        }
        _db.DeviceTypeOptions.Add(new DeviceTypeOption { Name = trimmed, DisplayOrder = 9999 });
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Added '{trimmed}'.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var rows = await _db.DeviceTypeOptions
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .ToListAsync();
        Items = rows.Select(t => new RowInput
        {
            Id = t.Id,
            Name = t.Name,
            DisplayOrder = t.DisplayOrder,
            IsActive = t.IsActive,
        }).ToList();
        await LoadUsageAsync();
    }

    private async Task LoadUsageAsync()
    {
        UsageCount = await _db.Devices
            .Where(d => d.DeviceTypeId != null)
            .GroupBy(d => d.DeviceTypeId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }
}
