using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

/// <summary>
/// Per-import cache that resolves lookup names to their FK rows, creating new options as needed.
/// Used by both import services so types/depts that show up during import become real options
/// editable from the Settings page afterwards.
/// </summary>
public class LookupResolver
{
    private readonly InventoryDbContext _db;
    private readonly Dictionary<string, DeviceTypeOption> _types;
    private readonly Dictionary<string, DeviceStatusOption> _statuses;
    private readonly Dictionary<string, DepartmentOption> _depts;

    private LookupResolver(
        InventoryDbContext db,
        Dictionary<string, DeviceTypeOption> types,
        Dictionary<string, DeviceStatusOption> statuses,
        Dictionary<string, DepartmentOption> depts)
    {
        _db = db;
        _types = types;
        _statuses = statuses;
        _depts = depts;
    }

    public static async Task<LookupResolver> CreateAsync(InventoryDbContext db, CancellationToken ct = default)
    {
        var types = await db.DeviceTypeOptions.ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase, ct);
        var statuses = await db.DeviceStatusOptions.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, ct);
        var depts = await db.DepartmentOptions.ToDictionaryAsync(d => d.Name, StringComparer.OrdinalIgnoreCase, ct);
        return new LookupResolver(db, types, statuses, depts);
    }

    public DeviceTypeOption ResolveType(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "Unknown";
        if (!_types.TryGetValue(trimmed, out var t))
        {
            t = new DeviceTypeOption { Name = trimmed, DisplayOrder = 100 + _types.Count * 10 };
            _db.DeviceTypeOptions.Add(t);
            _types[trimmed] = t;
        }
        return t;
    }

    public DeviceStatusOption ResolveStatus(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "InUse";
        if (!_statuses.TryGetValue(trimmed, out var s))
        {
            s = new DeviceStatusOption { Name = trimmed, BadgeClass = "badge", DisplayOrder = 100 + _statuses.Count * 10 };
            _db.DeviceStatusOptions.Add(s);
            _statuses[trimmed] = s;
        }
        return s;
    }

    public DepartmentOption? ResolveDepartmentOrNull(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        if (!_depts.TryGetValue(trimmed, out var d))
        {
            d = new DepartmentOption { Name = trimmed, DisplayOrder = 100 + _depts.Count * 10 };
            _db.DepartmentOptions.Add(d);
            _depts[trimmed] = d;
        }
        return d;
    }
}
