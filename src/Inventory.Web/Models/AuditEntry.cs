using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Imported = 3,
}

public class AuditEntry
{
    public int Id { get; set; }

    public int DeviceId { get; set; }
    public Device? Device { get; set; }

    public DateTime TimestampUtc { get; set; }

    [Required, StringLength(150)]
    public string ModifiedBy { get; set; } = "";

    public AuditAction Action { get; set; }

    public string Changes { get; set; } = "";
}
