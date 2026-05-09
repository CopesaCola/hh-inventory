using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages;

public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    public int DeviceCount { get; set; }
    public int UserCount { get; set; }
    public int SiteCount { get; set; }
    public List<AuditEntry> Recent { get; set; } = new();

    public List<BreakdownRow> StatusBreakdown { get; set; } = new();
    public List<BreakdownRow> SiteBreakdown { get; set; } = new();

    public class BreakdownRow
    {
        public string Name { get; set; } = "";
        public string? Tone { get; set; }   // CSS modifier ("spare", "repair", "retired", "lost", or null for default)
        public int Count { get; set; }
    }

    public async Task OnGetAsync()
    {
        // Top-level totals. Exclude removed devices so the dashboard reflects the
        // "live" inventory the team works with day-to-day; the Devices index hides
        // removed by default for the same reason.
        var activeDevices = _db.Devices.Where(d => !d.RemovedFromInventory);

        DeviceCount = await activeDevices.CountAsync();
        UserCount = await _db.UserProfiles.CountAsync();
        SiteCount = await _db.Sites.CountAsync();

        Recent = await _db.AuditEntries
            .Include(a => a.Device)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(20)
            .ToListAsync();

        // Devices by status. Two-step: group server-side by StatusId, then resolve
        // the names client-side from a small dictionary so we don't fight EF over
        // anonymous-typed group keys.
        var statusCounts = await activeDevices
            .GroupBy(d => d.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync();
        var statusLookup = await _db.DeviceStatusOptions
            .ToDictionaryAsync(s => s.Id, s => new { s.Name, s.BadgeClass });
        StatusBreakdown = statusCounts
            .Select(g =>
            {
                string name = "Unassigned";
                string? tone = null;
                if (g.StatusId.HasValue && statusLookup.TryGetValue(g.StatusId.Value, out var s))
                {
                    name = s.Name;
                    // BadgeClass is e.g. "badge spare" — strip the leading "badge" to get the tone.
                    tone = (s.BadgeClass ?? "").Replace("badge", "").Trim();
                    if (string.IsNullOrEmpty(tone)) tone = null;
                }
                return new BreakdownRow { Name = name, Tone = tone, Count = g.Count };
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        // Devices by site, top 10 (so "IT Cage", "Aiken", etc. show up like the
        // user expects). Anything beyond the top 10 is collapsed into one "Other"
        // bar to keep the chart compact.
        var siteCounts = await activeDevices
            .GroupBy(d => d.SiteId)
            .Select(g => new { SiteId = g.Key, Count = g.Count() })
            .ToListAsync();
        var siteLookup = await _db.Sites.ToDictionaryAsync(s => s.Id, s => s.Name);
        var resolved = siteCounts
            .Select(g => new BreakdownRow
            {
                Name = g.SiteId.HasValue && siteLookup.TryGetValue(g.SiteId.Value, out var n) ? n : "Unassigned",
                Count = g.Count
            })
            .OrderByDescending(r => r.Count)
            .ToList();
        const int siteLimit = 10;
        if (resolved.Count > siteLimit)
        {
            var top = resolved.Take(siteLimit).ToList();
            var otherCount = resolved.Skip(siteLimit).Sum(r => r.Count);
            top.Add(new BreakdownRow { Name = $"Other ({resolved.Count - siteLimit} sites)", Count = otherCount });
            SiteBreakdown = top;
        }
        else
        {
            SiteBreakdown = resolved;
        }
    }
}
