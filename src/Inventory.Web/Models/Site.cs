using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class Site
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(300)]
    public string? Address { get; set; }

    public List<UserProfile> Users { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
}
