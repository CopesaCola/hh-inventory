using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class DeviceTypeOption
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Device> Devices { get; set; } = new();
}
