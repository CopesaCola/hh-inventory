using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Inventory.Web.Pages.Settings;

[SupportedOSPlatform("windows")]
public class LdapModel : PageModel
{
    private readonly InventoryDbContext _db;
    private readonly LdapDirectoryService _ldap;
    private readonly ICurrentUser _user;

    public LdapModel(InventoryDbContext db, LdapDirectoryService ldap, ICurrentUser user)
    {
        _db = db;
        _ldap = ldap;
        _user = user;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// Fresh password value typed by the user. Only used to update the encrypted
    /// blob; never read from the saved entity, never echoed into the form.
    /// </summary>
    [BindProperty]
    public string? NewPassword { get; set; }

    public bool HasStoredPassword { get; set; }
    public DateTime? LastTestUtc { get; set; }
    public string? LastTestMessage { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public string? LastSyncMessage { get; set; }

    public class InputModel
    {
        public bool IsEnabled { get; set; }

        [StringLength(200)] public string? Server { get; set; }
        [Range(1, 65535)] public int Port { get; set; } = 389;
        public bool UseSsl { get; set; }

        [StringLength(400)] public string? BindDn { get; set; }
        [StringLength(400)] public string? BaseDn { get; set; }
        [StringLength(500)] public string? UserFilter { get; set; }

        [StringLength(60)] public string FullNameAttribute   { get; set; } = "displayName";
        [StringLength(60)] public string UsernameAttribute   { get; set; } = "sAMAccountName";
        [StringLength(60)] public string EmailAttribute      { get; set; } = "mail";
        [StringLength(60)] public string DepartmentAttribute { get; set; } = "department";
    }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Saves form values; updates the stored password only if a new one was typed in.</summary>
    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var s = await _ldap.GetOrCreateAsync();
        ApplyTo(s);
        ApplyPasswordIfProvided(s);
        await _db.SaveChangesAsync();

        TempData["Message"] = "LDAP settings saved.";
        return RedirectToPage();
    }

    /// <summary>Saves the form first, then tests, so users don't have to Save+Test in two clicks.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var s = await _ldap.GetOrCreateAsync();
        ApplyTo(s);
        ApplyPasswordIfProvided(s);
        await _db.SaveChangesAsync();

        var res = await _ldap.TestAsync(s);
        s.LastTestMessage = res.Message;
        await _db.SaveChangesAsync();

        if (res.Success) TempData["Message"] = "Test succeeded: " + res.Message;
        else             TempData["Error"]   = "Test failed: " + res.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncAsync()
    {
        var s = await _ldap.GetOrCreateAsync();
        var res = await _ldap.SyncUsersAsync(s, _user.Name);

        if (res.Success) TempData["Message"] = "Sync complete: " + res.Message;
        else             TempData["Error"]   = "Sync failed: " + res.Message;
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var s = await _ldap.GetOrCreateAsync();
        Input = new InputModel
        {
            IsEnabled = s.IsEnabled,
            Server = s.Server,
            Port = s.Port,
            UseSsl = s.UseSsl,
            BindDn = s.BindDn,
            BaseDn = s.BaseDn,
            UserFilter = s.UserFilter,
            FullNameAttribute = s.FullNameAttribute,
            UsernameAttribute = s.UsernameAttribute,
            EmailAttribute = s.EmailAttribute,
            DepartmentAttribute = s.DepartmentAttribute,
        };
        HasStoredPassword = s.BindPasswordEncrypted is { Length: > 0 };
        LastTestUtc = s.LastTestUtc;
        LastTestMessage = s.LastTestMessage;
        LastSyncUtc = s.LastSyncUtc;
        LastSyncMessage = s.LastSyncMessage;
    }

    private void ApplyTo(LdapSettings s)
    {
        s.IsEnabled = Input.IsEnabled;
        s.Server = Input.Server?.Trim();
        s.Port = Input.Port;
        s.UseSsl = Input.UseSsl;
        s.BindDn = Input.BindDn?.Trim();
        s.BaseDn = Input.BaseDn?.Trim();
        s.UserFilter = string.IsNullOrWhiteSpace(Input.UserFilter) ? null : Input.UserFilter.Trim();
        s.FullNameAttribute   = string.IsNullOrWhiteSpace(Input.FullNameAttribute)   ? "displayName"    : Input.FullNameAttribute.Trim();
        s.UsernameAttribute   = string.IsNullOrWhiteSpace(Input.UsernameAttribute)   ? "sAMAccountName" : Input.UsernameAttribute.Trim();
        s.EmailAttribute      = string.IsNullOrWhiteSpace(Input.EmailAttribute)      ? "mail"           : Input.EmailAttribute.Trim();
        s.DepartmentAttribute = string.IsNullOrWhiteSpace(Input.DepartmentAttribute) ? "department"     : Input.DepartmentAttribute.Trim();
    }

    private void ApplyPasswordIfProvided(LdapSettings s)
    {
        if (!string.IsNullOrEmpty(NewPassword))
            s.BindPasswordEncrypted = LdapDirectoryService.EncryptPassword(NewPassword);
    }
}
