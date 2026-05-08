using ClosedXML.Excel;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

public record HopeHealthImportResult(
    int Inserted,
    int Updated,
    int Skipped,
    Dictionary<string, (int Inserted, int Updated, int Skipped)> PerSheet,
    List<string> Warnings);

public class HopeHealthImportService
{
    private static readonly HashSet<string> SkipSheets = new(StringComparer.OrdinalIgnoreCase)
    {
        "IT Cage Master",
        "South Inventory",
    };

    private const string ItCageInHouseSheet = "IT Cage (In-House)";

    private static readonly Dictionary<string, string[]> HeaderVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["User"] = new[] { "users name", "users", "username", "user name" },
        ["MakeModel"] = new[] { "make/model" },
        ["TypeCode"] = new[] { "ws/lt", "type" },
        ["Serial"] = new[] { "serial number", "s/n", "sn" },
        ["AssetTag"] = new[] { "asset tag" },
        ["Location"] = new[] { "location" },
        ["WindowsVersion"] = new[] { "windows version" },
        ["ITStaff"] = new[] { "it employee", "it employee name" },
        ["Removed"] = new[] { "remove from inventory" },
        ["Grant"] = new[] { "grant or dept fund", "grant" },
    };

    private readonly InventoryDbContext _db;
    private readonly ICurrentUser _user;

    public HopeHealthImportService(InventoryDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<HopeHealthImportResult> ImportAsync(Stream stream, bool dryRun, bool skipItMaster, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var perSheet = new Dictionary<string, (int, int, int)>();
        var totalInserted = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var now = DateTime.UtcNow;
        var actor = _user.Name;

        using var wb = new XLWorkbook(stream);

        var existingSites = await _db.Sites.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, ct);
        var existingUsers = await _db.UserProfiles.ToListAsync(ct);
        var lookups = await LookupResolver.CreateAsync(_db, ct);

        var seen = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);

        foreach (var ws in wb.Worksheets)
        {
            ct.ThrowIfCancellationRequested();
            var sheetName = ws.Name;

            if (SkipSheets.Contains(sheetName))
            {
                warnings.Add($"Skipped sheet '{sheetName}' (excluded by config).");
                continue;
            }
            if (skipItMaster && sheetName.Equals("IT Master", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Skipped sheet 'IT Master' (skipItMaster=true).");
                continue;
            }

            int inserted, updated, skipped;

            if (sheetName.Equals(ItCageInHouseSheet, StringComparison.OrdinalIgnoreCase))
            {
                (inserted, updated, skipped) = await ImportItCageInHouseAsync(
                    ws, now, actor, existingSites, lookups, seen, warnings, ct);
            }
            else
            {
                (inserted, updated, skipped) = await ImportStandardSheetAsync(
                    ws, now, actor, existingSites, existingUsers, lookups, seen, warnings, ct);
            }

            perSheet[sheetName] = (inserted, updated, skipped);
            totalInserted += inserted;
            totalUpdated += updated;
            totalSkipped += skipped;
        }

        if (dryRun)
        {
            _db.ChangeTracker.Clear();
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        return new HopeHealthImportResult(totalInserted, totalUpdated, totalSkipped, perSheet, warnings);
    }

    private async Task<(int inserted, int updated, int skipped)> ImportStandardSheetAsync(
        IXLWorksheet ws,
        DateTime now,
        string actor,
        Dictionary<string, Site> existingSites,
        List<UserProfile> existingUsers,
        LookupResolver lookups,
        Dictionary<string, Device> seen,
        List<string> warnings,
        CancellationToken ct)
    {
        var sheetName = ws.Name;
        var headerRow = ws.FirstRowUsed();
        if (headerRow is null)
        {
            warnings.Add($"'{sheetName}': empty sheet, skipped.");
            return (0, 0, 0);
        }

        var cols = MapHeaderColumns(headerRow);
        if (!cols.ContainsKey("MakeModel") || (!cols.ContainsKey("Serial") && !cols.ContainsKey("AssetTag")))
        {
            warnings.Add($"'{sheetName}': non-standard header layout, skipped.");
            return (0, 0, 0);
        }

        var defaultSite = ResolveOrCreateSite(sheetName, existingSites);
        int inserted = 0, updated = 0, skipped = 0;
        int rowNum = headerRow.RowNumber();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            rowNum++;
            ct.ThrowIfCancellationRequested();

            var makeModel = GetCell(row, cols, "MakeModel");
            var serial = GetCell(row, cols, "Serial");
            var assetTag = GetCell(row, cols, "AssetTag");
            var typeCode = GetCell(row, cols, "TypeCode");
            var userName = GetCell(row, cols, "User");
            var location = GetCell(row, cols, "Location");
            var windowsVersion = GetCell(row, cols, "WindowsVersion");
            var itStaff = GetCell(row, cols, "ITStaff");
            var removed = GetCell(row, cols, "Removed");
            var grant = GetCell(row, cols, "Grant");

            if (string.IsNullOrWhiteSpace(makeModel) &&
                string.IsNullOrWhiteSpace(serial) &&
                string.IsNullOrWhiteSpace(assetTag))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(makeModel))
            {
                skipped++;
                warnings.Add($"'{sheetName}' row {rowNum}: missing Make/Model, skipped.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(assetTag))
            {
                skipped++;
                warnings.Add($"'{sheetName}' row {rowNum}: missing both Serial and Asset Tag, skipped.");
                continue;
            }

            var site = string.IsNullOrWhiteSpace(location)
                ? defaultSite
                : ResolveOrCreateSite(location, existingSites);

            UserProfile? user = null;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                user = ResolveOrCreateUser(userName, site, existingUsers);
            }

            var (normalizedType, rawType) = NormalizeType(typeCode);
            var typeOption = lookups.ResolveType(normalizedType);
            var statusName = string.IsNullOrWhiteSpace(removed) ? "InUse" : "Retired";
            var statusOption = lookups.ResolveStatus(statusName);
            var modifier = string.IsNullOrWhiteSpace(itStaff) ? actor : itStaff!.Trim();

            var (device, isNew) = await FindOrCreateDeviceAsync(serial, assetTag, seen, ct);

            if (isNew)
            {
                device.CreatedUtc = now;
                device.CreatedBy = modifier;
                _db.Devices.Add(device);
                _db.AuditEntries.Add(new AuditEntry
                {
                    Device = device,
                    TimestampUtc = now,
                    ModifiedBy = modifier,
                    Action = AuditAction.Imported,
                    Changes = "{}",
                });
                inserted++;
            }
            else
            {
                _db.AuditEntries.Add(new AuditEntry
                {
                    Device = device,
                    DeviceId = device.Id,
                    TimestampUtc = now,
                    ModifiedBy = modifier,
                    Action = AuditAction.Imported,
                    Changes = "{}",
                });
                updated++;
            }

            device.DeviceType = typeOption;
            device.RawType = rawType;
            device.Model = makeModel.Trim();
            if (!string.IsNullOrWhiteSpace(serial)) device.SerialNumber = serial.Trim();
            if (!string.IsNullOrWhiteSpace(assetTag)) device.AssetTag = assetTag.Trim();
            device.Status = statusOption;
            device.RemovedFromInventory = !string.IsNullOrWhiteSpace(removed);
            device.WindowsVersion = string.IsNullOrWhiteSpace(windowsVersion) ? device.WindowsVersion : windowsVersion.Trim();
            device.IsGrantFunded = !string.IsNullOrWhiteSpace(grant) || device.IsGrantFunded;
            if (user is not null) device.AssignedUser = user;
            if (site is not null) device.Site = site;
            device.LastModifiedUtc = now;
            device.LastModifiedBy = modifier;
        }

        return (inserted, updated, skipped);
    }

    private async Task<(int inserted, int updated, int skipped)> ImportItCageInHouseAsync(
        IXLWorksheet ws,
        DateTime now,
        string actor,
        Dictionary<string, Site> existingSites,
        LookupResolver lookups,
        Dictionary<string, Device> seen,
        List<string> warnings,
        CancellationToken ct)
    {
        var site = ResolveOrCreateSite("IT Cage (In-House)", existingSites);
        int inserted = 0, updated = 0, skipped = 0;
        string? currentSection = null;

        var headerLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Asset Tag", "Checkout by Intitals/Date", "Checkout Intitals/Date",
            "Checkout", "Date", "Total Stock", "Stock", "Notes", "Initials"
        };

        var spareStatus = lookups.ResolveStatus("Spare");

        foreach (var row in ws.RowsUsed())
        {
            ct.ThrowIfCancellationRequested();

            var a = row.Cell(1).GetString().Trim();
            var b = row.Cell(2).GetString().Trim();
            var d = row.Cell(4).GetString().Trim();

            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) && string.IsNullOrEmpty(d)) continue;

            if (!string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b) &&
                (string.IsNullOrEmpty(d) || headerLabels.Contains(d)))
            {
                if (!headerLabels.Contains(a)) currentSection = a;
                continue;
            }

            if (headerLabels.Contains(a) || headerLabels.Contains(d)) continue;

            if (string.IsNullOrEmpty(d)) continue;

            var model = string.IsNullOrEmpty(a) ? (currentSection ?? "Unknown") : a;
            var rawType = string.IsNullOrEmpty(b) ? GuessTypeFromSection(currentSection) : b;
            var typeOption = lookups.ResolveType(NormalizeType(rawType).Normalized);
            var assetTag = d;

            var (device, isNew) = await FindOrCreateDeviceAsync(serial: null, assetTag, seen, ct);
            if (isNew)
            {
                device.CreatedUtc = now;
                device.CreatedBy = actor;
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
                _db.AuditEntries.Add(new AuditEntry
                {
                    Device = device,
                    DeviceId = device.Id,
                    TimestampUtc = now,
                    ModifiedBy = actor,
                    Action = AuditAction.Imported,
                    Changes = "{}",
                });
                updated++;
            }

            device.DeviceType = typeOption;
            device.RawType = rawType;
            device.Model = model;
            device.AssetTag = assetTag;
            device.Status = spareStatus;
            device.RemovedFromInventory = false;
            device.Site = site;
            device.LocationWithinSite = currentSection;
            device.LastModifiedUtc = now;
            device.LastModifiedBy = actor;
        }

        return (inserted, updated, skipped);
    }

    private static string GuessTypeFromSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return "Unknown";
        var s = section.ToLowerInvariant();
        if (s.Contains("monitor")) return "Monitor";
        if (s.Contains("desktop")) return "Workstation";
        if (s.Contains("laptop")) return "Laptop";
        if (s.Contains("printer")) return "Printer";
        if (s.Contains("switch")) return "Switch";
        if (s.Contains("server")) return "Server";
        if (s.Contains("phone")) return "Phone";
        if (s.Contains("battery")) return "UPS";
        return "Unknown";
    }

    private async Task<(Device device, bool isNew)> FindOrCreateDeviceAsync(
        string? serial,
        string? assetTag,
        Dictionary<string, Device> seen,
        CancellationToken ct)
    {
        var serialKey = string.IsNullOrWhiteSpace(serial) ? null : "S:" + serial.Trim();
        var tagKey = string.IsNullOrWhiteSpace(assetTag) ? null : "T:" + assetTag.Trim();

        if (serialKey is not null && seen.TryGetValue(serialKey, out var bySerial)) return (bySerial, false);
        if (tagKey is not null && seen.TryGetValue(tagKey, out var byTag)) return (byTag, false);

        Device? existing = null;
        if (!string.IsNullOrWhiteSpace(serial))
            existing = await _db.Devices.FirstOrDefaultAsync(x => x.SerialNumber == serial, ct);
        if (existing is null && !string.IsNullOrWhiteSpace(assetTag))
            existing = await _db.Devices.FirstOrDefaultAsync(x => x.AssetTag == assetTag, ct);

        var isNew = existing is null;
        existing ??= new Device { Model = "" };
        if (serialKey is not null) seen[serialKey] = existing;
        if (tagKey is not null) seen[tagKey] = existing;
        return (existing, isNew);
    }

    private Site ResolveOrCreateSite(string name, Dictionary<string, Site> cache)
    {
        var trimmed = name.Trim();
        if (!cache.TryGetValue(trimmed, out var site))
        {
            site = new Site { Name = trimmed };
            _db.Sites.Add(site);
            cache[trimmed] = site;
        }
        return site;
    }

    private UserProfile ResolveOrCreateUser(string name, Site? site, List<UserProfile> cache)
    {
        var trimmed = name.Trim();
        var existing = cache.FirstOrDefault(u =>
            string.Equals(u.FullName, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var u = new UserProfile { FullName = trimmed, Site = site };
        _db.UserProfiles.Add(u);
        cache.Add(u);
        return u;
    }

    private static Dictionary<string, int> MapHeaderColumns(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var raw = cell.GetString().Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(raw)) continue;

            foreach (var (key, variants) in HeaderVariants)
            {
                if (map.ContainsKey(key)) continue;
                if (variants.Any(v => raw.Contains(v)))
                {
                    map[key] = cell.Address.ColumnNumber;
                    break;
                }
            }
        }
        return map;
    }

    private static string GetCell(IXLRow row, Dictionary<string, int> cols, string key)
    {
        if (!cols.TryGetValue(key, out var col)) return "";
        return row.Cell(col).GetString().Trim();
    }

    public static (string Normalized, string? Raw) NormalizeType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("Unknown", null);
        var clean = raw.Trim();
        var lower = clean.ToLowerInvariant();
        var head = lower.Split('/', '(', ' ')[0].Trim();

        var norm = head switch
        {
            "ws" or "workstation" or "desktop" or "pc" => "Workstation",
            "lt" or "laptop" or "lap" => "Laptop",
            "pntr" or "printer" or "mfp" => "Printer",
            "monitor" or "mntr" or "display" => "Monitor",
            "scanner" => "Scanner",
            "server" => "Server",
            "switch" or "sw" => "Switch",
            "phone" or "voip" => "Phone",
            "battery" or "batterybackup" or "ups" => "UPS",
            "tablet" => "Tablet",
            _ => "Unknown",
        };

        if (norm == "Unknown")
        {
            if (lower.Contains("monitor")) norm = "Monitor";
            else if (lower.Contains("printer")) norm = "Printer";
            else if (lower.Contains("switch")) norm = "Switch";
            else if (lower.Contains("battery") || lower.Contains("ups")) norm = "UPS";
            else if (lower.Contains("server")) norm = "Server";
            else if (lower.Contains("laptop")) norm = "Laptop";
            else if (lower.Contains("workstation") || lower.Contains("desktop")) norm = "Workstation";
            else norm = clean.Length <= 24 ? clean : "Unknown";
        }

        return (norm, clean);
    }
}
