using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

public record ImportResult(int Inserted, int Updated, int Skipped, List<string> Errors);

public class ImportRow
{
    public string? DeviceType { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public string? Status { get; set; }
    public string? LocationWithinSite { get; set; }
    public string? WindowsVersion { get; set; }
    public string? IsGrantFunded { get; set; }
    public string? AssignedUser { get; set; }
    public string? Site { get; set; }
}

public class ImportService
{
    private static readonly string[] AcceptedHeaders =
    {
        "DeviceType", "Model", "SerialNumber", "AssetTag", "Status",
        "LocationWithinSite", "WindowsVersion", "IsGrantFunded",
        "AssignedUser", "Site"
    };

    private readonly InventoryDbContext _db;
    private readonly ICurrentUser _user;

    public ImportService(InventoryDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<ImportResult> ImportAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? ReadCsv(stream)
            : ReadXlsx(stream);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        var now = DateTime.UtcNow;
        var actor = _user.Name;

        var existingSites = await _db.Sites.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, ct);
        var existingUsers = await _db.UserProfiles.ToListAsync(ct);
        var lookups = await LookupResolver.CreateAsync(_db, ct);

        var rowNumber = 1;
        foreach (var row in rows)
        {
            rowNumber++;
            try
            {
                if (string.IsNullOrWhiteSpace(row.DeviceType) || string.IsNullOrWhiteSpace(row.Model))
                {
                    skipped++;
                    errors.Add($"Row {rowNumber}: missing required field (DeviceType, Model).");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(row.SerialNumber) && string.IsNullOrWhiteSpace(row.AssetTag))
                {
                    skipped++;
                    errors.Add($"Row {rowNumber}: must have at least Serial Number or Asset Tag.");
                    continue;
                }

                Site? site = null;
                if (!string.IsNullOrWhiteSpace(row.Site))
                {
                    if (!existingSites.TryGetValue(row.Site!, out site))
                    {
                        site = new Site { Name = row.Site!.Trim() };
                        _db.Sites.Add(site);
                        existingSites[site.Name] = site;
                    }
                }

                UserProfile? user = null;
                if (!string.IsNullOrWhiteSpace(row.AssignedUser))
                {
                    var key = row.AssignedUser!.Trim();
                    user = existingUsers.FirstOrDefault(u =>
                        string.Equals(u.FullName, key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(u.Username, key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(u.Email, key, StringComparison.OrdinalIgnoreCase));
                    if (user is null)
                    {
                        user = new UserProfile { FullName = key, Site = site };
                        _db.UserProfiles.Add(user);
                        existingUsers.Add(user);
                    }
                }

                var typeOption = lookups.ResolveType(row.DeviceType!);
                var statusOption = lookups.ResolveStatus(string.IsNullOrWhiteSpace(row.Status) ? "InUse" : row.Status!);

                var existing = await FindExistingAsync(row.SerialNumber, row.AssetTag, ct);

                if (existing is null)
                {
                    var device = new Device
                    {
                        DeviceType = typeOption,
                        Model = row.Model!.Trim(),
                        SerialNumber = row.SerialNumber?.Trim(),
                        AssetTag = row.AssetTag?.Trim(),
                        Status = statusOption,
                        LocationWithinSite = row.LocationWithinSite?.Trim(),
                        WindowsVersion = row.WindowsVersion?.Trim(),
                        IsGrantFunded = ParseBool(row.IsGrantFunded),
                        AssignedUser = user,
                        Site = site,
                        CreatedUtc = now,
                        CreatedBy = actor,
                        LastModifiedUtc = now,
                        LastModifiedBy = actor,
                    };
                    _db.Devices.Add(device);
                    _db.AuditEntries.Add(new AuditEntry
                    {
                        Device = device,
                        TimestampUtc = now,
                        ModifiedBy = actor,
                        Action = AuditAction.Imported,
                        Changes = "{}",
                    });
                    inserted++;
                }
                else
                {
                    existing.DeviceType = typeOption;
                    existing.Model = row.Model!.Trim();
                    if (!string.IsNullOrWhiteSpace(row.SerialNumber)) existing.SerialNumber = row.SerialNumber.Trim();
                    if (!string.IsNullOrWhiteSpace(row.AssetTag)) existing.AssetTag = row.AssetTag.Trim();
                    existing.Status = statusOption;
                    if (!string.IsNullOrWhiteSpace(row.LocationWithinSite)) existing.LocationWithinSite = row.LocationWithinSite.Trim();
                    if (!string.IsNullOrWhiteSpace(row.WindowsVersion)) existing.WindowsVersion = row.WindowsVersion.Trim();
                    if (!string.IsNullOrWhiteSpace(row.IsGrantFunded)) existing.IsGrantFunded = ParseBool(row.IsGrantFunded);
                    if (user is not null) existing.AssignedUser = user;
                    if (site is not null) existing.Site = site;
                    existing.LastModifiedUtc = now;
                    existing.LastModifiedBy = actor;
                    _db.AuditEntries.Add(new AuditEntry
                    {
                        DeviceId = existing.Id,
                        TimestampUtc = now,
                        ModifiedBy = actor,
                        Action = AuditAction.Imported,
                        Changes = "{}",
                    });
                    updated++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(ct);
        return new ImportResult(inserted, updated, skipped, errors);
    }

    private async Task<Device?> FindExistingAsync(string? serial, string? assetTag, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            var bySerial = await _db.Devices.FirstOrDefaultAsync(d => d.SerialNumber == serial, ct);
            if (bySerial is not null) return bySerial;
        }
        if (!string.IsNullOrWhiteSpace(assetTag))
        {
            return await _db.Devices.FirstOrDefaultAsync(d => d.AssetTag == assetTag, ct);
        }
        return null;
    }

    private static bool ParseBool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var v = raw.Trim().ToLowerInvariant();
        return v is "yes" or "y" or "true" or "1" or "x";
    }

    private static IEnumerable<ImportRow> ReadCsv(Stream stream)
    {
        var reader = new StreamReader(stream);
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = args => args.Header.Trim(),
        };
        using var csv = new CsvReader(reader, cfg);
        return csv.GetRecords<ImportRow>().ToList();
    }

    private static IEnumerable<ImportRow> ReadXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var headerRow = ws.FirstRowUsed();
        var rows = new List<ImportRow>();
        if (headerRow is null) return rows;

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (AcceptedHeaders.Contains(header, StringComparer.OrdinalIgnoreCase))
                headerMap[header] = cell.Address.ColumnNumber;
        }

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            string? Get(string h) => headerMap.TryGetValue(h, out var col)
                ? row.Cell(col).GetString().Trim()
                : null;

            rows.Add(new ImportRow
            {
                DeviceType = Get("DeviceType"),
                Model = Get("Model"),
                SerialNumber = Get("SerialNumber"),
                AssetTag = Get("AssetTag"),
                Status = Get("Status"),
                LocationWithinSite = Get("LocationWithinSite"),
                WindowsVersion = Get("WindowsVersion"),
                IsGrantFunded = Get("IsGrantFunded"),
                AssignedUser = Get("AssignedUser"),
                Site = Get("Site"),
            });
        }
        return rows;
    }
}
