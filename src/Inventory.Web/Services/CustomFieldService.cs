using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

public class CustomFieldService
{
    private readonly InventoryDbContext _db;
    public CustomFieldService(InventoryDbContext db) => _db = db;

    public Task<List<CustomFieldDefinition>> GetActiveDefinitionsAsync(CustomFieldEntityType entity) =>
        _db.CustomFieldDefinitions
            .Where(d => d.EntityType == entity && d.IsActive)
            .OrderBy(d => d.DisplayOrder).ThenBy(d => d.Name)
            .ToListAsync();

    public async Task<Dictionary<string, string?>> GetValuesForAsync(CustomFieldEntityType entity, int entityId)
    {
        return await _db.CustomFieldValues
            .Where(v => v.EntityType == entity && v.EntityId == entityId)
            .ToDictionaryAsync(v => v.DefinitionId.ToString(), v => v.Value);
    }

    public async Task SaveValuesAsync(CustomFieldEntityType entity, int entityId, IDictionary<string, string?> values)
    {
        var existing = await _db.CustomFieldValues
            .Where(v => v.EntityType == entity && v.EntityId == entityId)
            .ToListAsync();
        var byDefId = existing.ToDictionary(v => v.DefinitionId);

        foreach (var (defIdStr, value) in values)
        {
            if (!int.TryParse(defIdStr, out var defId)) continue;
            if (byDefId.TryGetValue(defId, out var row))
            {
                row.Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                _db.CustomFieldValues.Add(new CustomFieldValue
                {
                    DefinitionId = defId,
                    EntityType = entity,
                    EntityId = entityId,
                    Value = value.Trim(),
                });
            }
        }
        await _db.SaveChangesAsync();
    }
}
