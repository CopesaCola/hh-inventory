using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

public class StatusesModel : PageModel
{
    private readonly InventoryDbContext _db;
    public StatusesModel(InventoryDbContext db) => _db = db;

    public class RowInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? BadgeClass { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty] public List<RowInput> Items { get; set; } = new();
    [BindProperty] public int? DeleteId { get; set; }
    [BindProperty] public string? NewName { get; set; }
    [BindProperty] public string? NewBadgeClass { get; set; }

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

        var existing = await _db.DeviceStatusOptions.ToListAsync();
        var byId = existing.ToDictionary(x => x.Id);
        foreach (var row in Items)
        {
            if (!byId.TryGetValue(row.Id, out var entity)) continue;
            entity.Name = row.Name.Trim();
            entity.BadgeClass = string.IsNullOrWhiteSpace(row.BadgeClass) ? "badge" : row.BadgeClass;
            entity.DisplayOrder = row.DisplayOrder;
            entity.IsActive = row.IsActive;
        }

        string? warning = null;
        if (DeleteId is int delId)
        {
            var inUse = await _db.Devices.AnyAsync(d => d.StatusId == delId);
            if (inUse)
                warning = "Couldn't delete: that status is still used by devices. Pending edits saved.";
            else if (byId.TryGetValue(delId, out var toRemove))
                _db.DeviceStatusOptions.Remove(toRemove);
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
        if (await _db.DeviceStatusOptions.AnyAsync(s => s.Name == trimmed))
        {
            TempData["Error"] = "A status with this name already exists.";
            return RedirectToPage();
        }
        _db.DeviceStatusOptions.Add(new DeviceStatusOption
        {
            Name = trimmed,
            BadgeClass = string.IsNullOrWhiteSpace(NewBadgeClass) ? "badge" : NewBadgeClass,
            DisplayOrder = 9999,
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Added '{trimmed}'.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var rows = await _db.DeviceStatusOptions
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .ToListAsync();
        Items = rows.Select(s => new RowInput
        {
            Id = s.Id,
            Name = s.Name,
            BadgeClass = s.BadgeClass,
            DisplayOrder = s.DisplayOrder,
            IsActive = s.IsActive,
        }).ToList();
        await LoadUsageAsync();
    }

    private async Task LoadUsageAsync()
    {
        UsageCount = await _db.Devices
            .Where(d => d.StatusId != null)
            .GroupBy(d => d.StatusId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }
}
