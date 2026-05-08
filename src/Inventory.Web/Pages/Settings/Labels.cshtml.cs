using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

public class LabelsModel : PageModel
{
    private readonly InventoryDbContext _db;
    public LabelsModel(InventoryDbContext db) => _db = db;

    public Dictionary<(CustomFieldEntityType, string), string> Current { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> Overrides { get; set; } = new();

    public async Task OnGetAsync()
    {
        var rows = await _db.FieldLabelOverrides.AsNoTracking().ToListAsync();
        Current = rows.ToDictionary(r => (r.EntityType, r.FieldKey), r => r.DisplayName);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var existing = await _db.FieldLabelOverrides.ToListAsync();
        var existingByKey = existing.ToDictionary(o => $"{(int)o.EntityType}_{o.FieldKey}");

        // Validate: keys must be in catalog
        var validKeys = new HashSet<string>();
        foreach (var (entityType, fields) in OverridableLabels.Catalog)
        {
            foreach (var (key, _) in fields)
                validKeys.Add($"{(int)entityType}_{key}");
        }

        foreach (var (formKey, value) in Overrides)
        {
            if (!validKeys.Contains(formKey)) continue;
            var parts = formKey.Split('_', 2);
            var entityType = (CustomFieldEntityType)int.Parse(parts[0]);
            var fieldKey = parts[1];

            var trimmed = value?.Trim();
            existingByKey.TryGetValue(formKey, out var row);

            if (string.IsNullOrEmpty(trimmed))
            {
                if (row is not null) _db.FieldLabelOverrides.Remove(row);
            }
            else
            {
                if (row is null)
                {
                    _db.FieldLabelOverrides.Add(new FieldLabelOverride
                    {
                        EntityType = entityType,
                        FieldKey = fieldKey,
                        DisplayName = trimmed,
                    });
                }
                else
                {
                    row.DisplayName = trimmed;
                }
            }
        }

        await _db.SaveChangesAsync();
        TempData["Message"] = "Labels saved.";
        return RedirectToPage();
    }
}
