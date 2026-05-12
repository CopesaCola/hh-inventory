using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

/// <summary>
/// LDAP / Active Directory integration used by the Settings → LDAP page.
///  - Test: binds to the server with the saved (or freshly-entered) credentials
///    and performs a 1-row search to confirm the filter and base DN work.
///  - Sync: pulls user entries matching the configured filter and creates or
///    updates UserProfile rows. Existing users are matched by Username
///    (sAMAccountName by default); nothing is deleted from the local database.
/// Uses System.DirectoryServices.Protocols, which is Windows-only — matches
/// the rest of the app (Negotiate auth, IIS hosting).
/// </summary>
[SupportedOSPlatform("windows")]
public class LdapDirectoryService
{
    private readonly InventoryDbContext _db;

    public LdapDirectoryService(InventoryDbContext db) => _db = db;

    /// <summary>Returns the singleton settings row, inserting a default if missing.</summary>
    public async Task<LdapSettings> GetOrCreateAsync()
    {
        var s = await _db.LdapSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null)
        {
            s = new LdapSettings();
            _db.LdapSettings.Add(s);
            await _db.SaveChangesAsync();
        }
        return s;
    }

    public async Task<OperationResult> TestAsync(LdapSettings cfg)
    {
        try
        {
            using var conn = OpenConnection(cfg);
            var entries = SearchUsers(conn, cfg, sizeLimit: 1);
            var sample = entries.FirstOrDefault();
            var sampleName = sample is null ? null : GetAttr(sample, cfg.UsernameAttribute);
            var msg = sample is null
                ? "Bound to server, but no users matched the filter at the base DN."
                : $"Bound to server. Sample user found: {sampleName ?? sample.DistinguishedName}.";
            return new OperationResult(true, msg);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Flatten(ex));
        }
        finally
        {
            cfg.LastTestUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Pulls users from the directory and upserts UserProfile rows. Matching is
    /// by Username (case-insensitive). Department is resolved by name against
    /// the existing DepartmentOption rows; unmapped values are left null rather
    /// than auto-created, so the Settings → Departments list stays curated.
    /// </summary>
    public async Task<SyncResult> SyncUsersAsync(LdapSettings cfg, string actingUser)
    {
        if (!cfg.IsEnabled)
            return new SyncResult { Success = false, Message = "LDAP integration is disabled. Enable it on the settings page first." };

        try
        {
            var existing = await _db.UserProfiles.ToListAsync();
            var byUsername = existing
                .Where(u => !string.IsNullOrWhiteSpace(u.Username))
                .GroupBy(u => u.Username!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var deptByName = await _db.DepartmentOptions
                .ToDictionaryAsync(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);

            int inserted = 0, updated = 0, skipped = 0, unmappedDepts = 0;

            using var conn = OpenConnection(cfg);
            var entries = SearchUsers(conn, cfg, sizeLimit: 5000);

            foreach (var entry in entries)
            {
                var username = GetAttr(entry, cfg.UsernameAttribute);
                if (string.IsNullOrWhiteSpace(username)) { skipped++; continue; }

                var fullName = GetAttr(entry, cfg.FullNameAttribute) ?? username;
                var email = GetAttr(entry, cfg.EmailAttribute);
                var deptName = GetAttr(entry, cfg.DepartmentAttribute);

                int? deptId = null;
                if (!string.IsNullOrWhiteSpace(deptName))
                {
                    if (deptByName.TryGetValue(deptName!, out var id)) deptId = id;
                    else unmappedDepts++;
                }

                if (byUsername.TryGetValue(username!, out var u))
                {
                    u.FullName = fullName.Trim();
                    if (!string.IsNullOrWhiteSpace(email)) u.Email = email.Trim();
                    if (deptId is not null) u.DepartmentId = deptId;
                    updated++;
                }
                else
                {
                    _db.UserProfiles.Add(new UserProfile
                    {
                        Username = username!.Trim(),
                        FullName = fullName.Trim(),
                        Email = email?.Trim(),
                        DepartmentId = deptId,
                    });
                    inserted++;
                }
            }

            await _db.SaveChangesAsync();

            var msg = $"{inserted} added, {updated} updated, {skipped} skipped. " +
                      $"By {actingUser}." +
                      (unmappedDepts > 0 ? $" {unmappedDepts} entries had a department not in Settings → Departments (left blank)." : "");
            cfg.LastSyncUtc = DateTime.UtcNow;
            cfg.LastSyncMessage = msg;
            await _db.SaveChangesAsync();

            return new SyncResult { Success = true, Inserted = inserted, Updated = updated, Skipped = skipped, Message = msg };
        }
        catch (Exception ex)
        {
            cfg.LastSyncUtc = DateTime.UtcNow;
            cfg.LastSyncMessage = $"Failed: {Flatten(ex)}";
            await _db.SaveChangesAsync();
            return new SyncResult { Success = false, Message = Flatten(ex) };
        }
    }

    // -------- internals --------

    private static LdapConnection OpenConnection(LdapSettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Server))
            throw new InvalidOperationException("Server is required.");

        var conn = new LdapConnection(new LdapDirectoryIdentifier(cfg.Server, cfg.Port));
        conn.SessionOptions.ProtocolVersion = 3;
        if (cfg.UseSsl) conn.SessionOptions.SecureSocketLayer = true;

        if (string.IsNullOrWhiteSpace(cfg.BindDn))
        {
            // Run as the process identity (Kerberos/NTLM). Works when the app pool
            // identity is itself trusted by AD — no password to store.
            conn.AuthType = AuthType.Negotiate;
            conn.Bind();
        }
        else
        {
            conn.AuthType = AuthType.Basic;
            var pwd = DecryptPassword(cfg.BindPasswordEncrypted) ?? "";
            conn.Bind(new NetworkCredential(cfg.BindDn, pwd));
        }

        return conn;
    }

    private static List<SearchResultEntry> SearchUsers(LdapConnection conn, LdapSettings cfg, int sizeLimit)
    {
        if (string.IsNullOrWhiteSpace(cfg.BaseDn))
            throw new InvalidOperationException("Base DN is required.");

        var attrs = new[]
        {
            cfg.UsernameAttribute, cfg.FullNameAttribute, cfg.EmailAttribute, cfg.DepartmentAttribute
        };
        var req = new SearchRequest(
            cfg.BaseDn,
            string.IsNullOrWhiteSpace(cfg.UserFilter) ? "(objectClass=user)" : cfg.UserFilter,
            SearchScope.Subtree,
            attrs);
        req.SizeLimit = sizeLimit;

        var resp = (SearchResponse)conn.SendRequest(req);
        var list = new List<SearchResultEntry>(resp.Entries.Count);
        foreach (SearchResultEntry e in resp.Entries) list.Add(e);
        return list;
    }

    private static string? GetAttr(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name)) return null;
        var attr = entry.Attributes[name];
        if (attr.Count == 0) return null;
        return attr[0]?.ToString();
    }

    private static string Flatten(Exception ex)
    {
        var msg = ex.Message;
        var inner = ex.InnerException;
        while (inner is not null) { msg += " → " + inner.Message; inner = inner.InnerException; }
        return msg;
    }

    // -------- password protection (DPAPI, LocalMachine scope) --------

    /// <summary>Encrypts a plaintext password with DPAPI so it can survive in the SQLite file.</summary>
    public static byte[]? EncryptPassword(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        return ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
    }

    /// <summary>Returns null if the blob can't be decrypted (typically: copied to another machine).</summary>
    public static string? DecryptPassword(byte[]? encrypted)
    {
        if (encrypted is null || encrypted.Length == 0) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // -------- result types --------

    public record OperationResult(bool Success, string Message);

    public class SyncResult
    {
        public bool Success { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public string Message { get; set; } = "";
    }
}
