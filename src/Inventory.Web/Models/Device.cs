using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class Device
{
    public int Id { get; set; }

    public int? DeviceTypeId { get; set; }
    public DeviceTypeOption? DeviceType { get; set; }

    [StringLength(80)]
    public string? RawType { get; set; }

    [Required, StringLength(160)]
    public string Model { get; set; } = "";

    [StringLength(200)]
    public string? SerialNumber { get; set; }

    [StringLength(120)]
    public string? AssetTag { get; set; }

    public int? StatusId { get; set; }
    public DeviceStatusOption? Status { get; set; }

    [StringLength(200)]
    public string? LocationWithinSite { get; set; }

    [StringLength(40)]
    public string? WindowsVersion { get; set; }

    public bool IsGrantFunded { get; set; }

    public bool RemovedFromInventory { get; set; }

    public int? AssignedUserId { get; set; }
    public UserProfile? AssignedUser { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public DateTime CreatedUtc { get; set; }
    [StringLength(150)]
    public string CreatedBy { get; set; } = "";

    public DateTime LastModifiedUtc { get; set; }
    [StringLength(150)]
    public string LastModifiedBy { get; set; } = "";

    public List<AuditEntry> AuditEntries { get; set; } = new();
}
