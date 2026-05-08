using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

public class CustomFieldsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public CustomFieldsModel(InventoryDbContext db) => _db = db;

    public class RowInput
    {
        public int Id { get; set; }
        public CustomFieldEntityType EntityType { get; set; }
        public string Name { get; set; } = "";
        public CustomFieldType FieldType { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty] public List<RowInput> Items { get; set; } = new();
    [BindProperty] public int? DeleteId { get; set; }
    [BindProperty] public string? NewName { get; set; }
    [BindProperty] public CustomFieldEntityType NewEntityType { get; set; }
    [BindProperty] public CustomFieldType NewFieldType { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate uniqueness within entity type
        var seen = new HashSet<(CustomFieldEntityType, string)>();
        foreach (var row in Items)
        {
            var trimmed = (row.Name ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                TempData["Error"] = "All rows must have a name.";
                await LoadAsync();
                return Page();
            }
            if (!seen.Add((row.EntityType, trimmed.ToLowerInvariant())))
            {
                TempData["Error"] = $"Duplicate name '{trimmed}' for {row.EntityType}.";
                await LoadAsync();
                return Page();
            }
        }

        var existing = await _db.CustomFieldDefinitions.ToListAsync();
        var byId = existing.ToDictionary(x => x.Id);
        foreach (var row in Items)
        {
            if (!byId.TryGetValue(row.Id, out var entity)) continue;
            entity.Name = row.Name.Trim();
            entity.FieldType = row.FieldType;
            entity.DisplayOrder = row.DisplayOrder;
            entity.IsActive = row.IsActive;
            // EntityType is fixed after creation
        }

        if (DeleteId is int delId && byId.TryGetValue(delId, out var toRemove))
        {
            _db.CustomFieldDefinitions.Remove(toRemove); // cascades to values
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
        if (await _db.CustomFieldDefinitions.AnyAsync(d => d.EntityType == NewEntityType && d.Name == trimmed))
        {
            TempData["Error"] = $"A field named '{trimmed}' already exists for {NewEntityType}.";
            return RedirectToPage();
        }
        _db.CustomFieldDefinitions.Add(new CustomFieldDefinition
        {
            EntityType = NewEntityType,
            Name = trimmed,
            FieldType = NewFieldType,
            DisplayOrder = 100,
        });
        await _db.SaveChangesAsync();
        TempData["Message"] = $"Added '{trimmed}'.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var rows = await _db.CustomFieldDefinitions
            .OrderBy(d => d.EntityType).ThenBy(d => d.DisplayOrder).ThenBy(d => d.Name)
            .ToListAsync();
        Items = rows.Select(d => new RowInput
        {
            Id = d.Id,
            EntityType = d.EntityType,
            Name = d.Name,
            FieldType = d.FieldType,
            DisplayOrder = d.DisplayOrder,
            IsActive = d.IsActive,
        }).ToList();
    }
}
