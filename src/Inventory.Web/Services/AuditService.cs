using System.Text.Json;
using Inventory.Web.Data;
using Inventory.Web.Models;

namespace Inventory.Web.Services;

public class AuditService
{
    private readonly InventoryDbContext _db;
    private readonly ICurrentUser _user;

    public AuditService(InventoryDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public void Record(Device device, AuditAction action, IDictionary<string, (object? before, object? after)>? changes = null)
    {
        var json = changes is null
            ? "{}"
            : JsonSerializer.Serialize(changes.ToDictionary(
                kv => kv.Key,
                kv => new { before = kv.Value.before, after = kv.Value.after }));

        _db.AuditEntries.Add(new AuditEntry
        {
            Device = device,
            DeviceId = device.Id,
            TimestampUtc = DateTime.UtcNow,
            ModifiedBy = _user.Name,
            Action = action,
            Changes = json,
        });
    }
}
