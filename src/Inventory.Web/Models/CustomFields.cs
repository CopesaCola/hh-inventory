using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

public enum CustomFieldEntityType
{
    Device = 0,
    UserProfile = 1,
    Site = 2,
}

public enum CustomFieldType
{
    Text = 0,
    YesNo = 1,
}

public class CustomFieldDefinition
{
    public int Id { get; set; }

    public CustomFieldEntityType EntityType { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CustomFieldValue> Values { get; set; } = new();
}

public class CustomFieldValue
{
    public int Id { get; set; }

    public int DefinitionId { get; set; }
    public CustomFieldDefinition? Definition { get; set; }

    public CustomFieldEntityType EntityType { get; set; }

    public int EntityId { get; set; }

    [StringLength(2000)]
    public string? Value { get; set; }
}

public class FieldLabelOverride
{
    public int Id { get; set; }

    public CustomFieldEntityType EntityType { get; set; }

    [Required, StringLength(80)]
    public string FieldKey { get; set; } = "";

    [Required, StringLength(120)]
    public string DisplayName { get; set; } = "";
}
