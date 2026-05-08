using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class UserProfile
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string FullName { get; set; } = "";

    [StringLength(150), EmailAddress]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? Username { get; set; }

    public int? DepartmentId { get; set; }
    public DepartmentOption? Department { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public List<Device> Devices { get; set; } = new();
}
