using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class DeviceStatusOption
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = "";

    [StringLength(40)]
    public string? BadgeClass { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Device> Devices { get; set; } = new();
}
