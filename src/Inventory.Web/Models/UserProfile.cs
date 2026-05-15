using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

/// <summary>
/// What a UserProfile row represents. Suites (rooms / sub-areas of a site) are
/// modeled as UserProfile rows so they can own devices through the existing
/// AssignedUserId relationship without doubling the schema. Persons can
/// optionally belong to a Suite at their site.
/// </summary>
public enum UserKind
{
    Person = 0,
    Suite  = 1,
}

public class UserProfile
{
    public int Id { get; set; }

    /// <summary>Person (default) or Suite. Suites are stored as UserProfile rows.</summary>
    public UserKind Kind { get; set; } = UserKind.Person;

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

    /// <summary>
    /// Parent suite this person belongs to (must itself have Kind = Suite and
    /// the same SiteId). Null on Suite rows. Self-referencing FK with SetNull
    /// on delete so a deleted suite leaves its persons orphaned, not removed.
    /// </summary>
    public int? SuiteId { get; set; }
    public UserProfile? Suite { get; set; }

    /// <summary>Reverse navigation: persons assigned to this row when it's a Suite.</summary>
    public List<UserProfile> Members { get; set; } = new();

    public List<Device> Devices { get; set; } = new();
}
