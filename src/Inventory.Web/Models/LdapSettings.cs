using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models;

/// <summary>
/// Singleton row (Id = 1) holding the app's LDAP / Active Directory connection
/// configuration. There is intentionally only one row; the page model fetches
/// or creates it on demand.
/// </summary>
public class LdapSettings
{
    public int Id { get; set; } = 1;

    /// <summary>If false, the Settings page still works but Sync is blocked.</summary>
    public bool IsEnabled { get; set; }

    [StringLength(200)] public string? Server { get; set; }       // e.g. "dc01.hopehealth.local"
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }                              // 636 for LDAPS

    /// <summary>
    /// Bind DN or UPN used to authenticate to the directory. Leave blank to bind
    /// using the running process identity via Negotiate (Kerberos/NTLM) — handy
    /// when the app pool runs as a service account already trusted by AD.
    /// </summary>
    [StringLength(400)] public string? BindDn { get; set; }

    /// <summary>
    /// Bind password, DPAPI-encrypted with LocalMachine scope. Never round-tripped
    /// to the UI; the form only accepts a new value, encrypts on save, and stores
    /// the ciphertext here.
    /// </summary>
    public byte[]? BindPasswordEncrypted { get; set; }

    [StringLength(400)] public string? BaseDn { get; set; }       // e.g. "OU=Users,DC=hopehealth,DC=local"

    /// <summary>LDAP filter for the users to sync. Defaults to enabled person accounts.</summary>
    [StringLength(500)] public string? UserFilter { get; set; }
        = "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";

    // Attribute mappings — defaults are the standard AD names; exposed in the UI
    // so non-AD LDAP servers (OpenLDAP, FreeIPA) can be retargeted.
    [StringLength(60)] public string FullNameAttribute   { get; set; } = "displayName";
    [StringLength(60)] public string UsernameAttribute   { get; set; } = "sAMAccountName";
    [StringLength(60)] public string EmailAttribute      { get; set; } = "mail";
    [StringLength(60)] public string DepartmentAttribute { get; set; } = "department";

    // Status fields surfaced on the Settings page so the user can see whether
    // their last action succeeded.
    public DateTime? LastTestUtc { get; set; }
    [StringLength(400)] public string? LastTestMessage { get; set; }

    public DateTime? LastSyncUtc { get; set; }
    [StringLength(400)] public string? LastSyncMessage { get; set; }
}
