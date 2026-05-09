using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

public interface ILabelService
{
    string Get(CustomFieldEntityType entity, string fieldKey, string defaultLabel);
}

public class LabelService : ILabelService
{
    // Per-request cache
    private readonly Dictionary<(CustomFieldEntityType, string), string> _cache;

    public LabelService(InventoryDbContext db)
    {
        _cache = db.FieldLabelOverrides
            .AsNoTracking()
            .ToDictionary(o => (o.EntityType, o.FieldKey), o => o.DisplayName);
    }

    public string Get(CustomFieldEntityType entity, string fieldKey, string defaultLabel)
        => _cache.TryGetValue((entity, fieldKey), out var custom) ? custom : defaultLabel;
}

/// <summary>List of overridable built-in fields per entity. The Settings UI uses this.</summary>
public static class OverridableLabels
{
    public static readonly Dictionary<CustomFieldEntityType, (string Key, string Default)[]> Catalog = new()
    {
        [CustomFieldEntityType.Device] = new (string, string)[]
        {
            ("DeviceType", "Device Type"),
            ("Model", "Model"),
            ("SerialNumber", "Serial Number"),
            ("AssetTag", "Asset Tag"),
            ("Status", "Status"),
            ("LocationWithinSite", "Location Within Site"),
            ("WindowsVersion", "Windows Version"),
            ("IsGrantFunded", "Grant or Dept Funded"),
            ("GrantOrDeptFund", "Grant or Dept name"),
            ("AssignedUser", "Assigned User"),
            ("Site", "Site"),
        },
        [CustomFieldEntityType.UserProfile] = new (string, string)[]
        {
            ("FullName", "Full Name"),
            ("Username", "Username"),
            ("Email", "Email"),
            ("Department", "Department"),
            ("Site", "Site"),
        },
        [CustomFieldEntityType.Site] = new (string, string)[]
        {
            ("Name", "Name"),
            ("Address", "Address"),
        },
    };
}
