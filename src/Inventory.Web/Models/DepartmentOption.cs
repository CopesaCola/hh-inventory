using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public class DepartmentOption
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<UserProfile> Users { get; set; } = new();
}
