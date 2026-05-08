using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Devices;

public class IndexModel : PageModel
{
    private readonly InventoryDbContext _db;
    public IndexModel(InventoryDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SiteId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DeviceTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StatusId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeRemoved { get; set; }

    public List<Device> Devices { get; set; } = new();
    public List<Site> Sites { get; set; } = new();
    public List<DeviceTypeOption> DeviceTypes { get; set; } = new();
    public List<DeviceStatusOption> Statuses { get; set; } = new();
    public int TotalMatching { get; set; }

    public async Task OnGetAsync()
    {
        Sites = await _db.Sites.OrderBy(s => s.Name).ToListAsync();
        DeviceTypes = await _db.DeviceTypeOptions.OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name).ToListAsync();
        Statuses = await _db.DeviceStatusOptions.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync();

        var q = BuildQuery();
        TotalMatching = await q.CountAsync();
        Devices = await q.OrderByDescending(d => d.LastModifiedUtc).Take(500).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var devices = await BuildQuery()
            .OrderByDescending(d => d.LastModifiedUtc)
            .ToListAsync();

        var rows = devices.Select(d => new
        {
            Type = d.DeviceType?.Name ?? "",
            Model = d.Model,
            SerialNumber = d.SerialNumber ?? "",
            AssetTag = d.AssetTag ?? "",
            Status = d.Status?.Name ?? "",
            Site = d.Site?.Name ?? "",
            AssignedUser = d.AssignedUser?.FullName ?? "",
            LocationWithinSite = d.LocationWithinSite ?? "",
            WindowsVersion = d.WindowsVersion ?? "",
            IsGrantFunded = d.IsGrantFunded ? "Yes" : "No",
            Removed = d.RemovedFromInventory ? "Yes" : "No",
            CreatedUtc = d.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            CreatedBy = d.CreatedBy,
            LastModifiedUtc = d.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            LastModifiedBy = d.LastModifiedBy,
        });

        var bytes = CsvExporter.Build(rows);
        var name = CsvExporter.MakeFilename("devices", Query);
        return File(bytes, "text/csv", name);
    }

    private IQueryable<Device> BuildQuery()
    {
        var q = _db.Devices
            .Include(d => d.AssignedUser)
            .Include(d => d.Site)
            .Include(d => d.DeviceType)
            .Include(d => d.Status)
            .AsQueryable();

        if (!IncludeRemoved) q = q.Where(d => !d.RemovedFromInventory);
        if (SiteId is not null) q = q.Where(d => d.SiteId == SiteId);
        if (DeviceTypeId is not null) q = q.Where(d => d.DeviceTypeId == DeviceTypeId);
        if (StatusId is not null) q = q.Where(d => d.StatusId == StatusId);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.Trim();
            var like = $"%{term}%";
            q = q.Where(d =>
                EF.Functions.Like(d.SerialNumber ?? "", like) ||
                EF.Functions.Like(d.AssetTag ?? "", like) ||
                EF.Functions.Like(d.Model, like) ||
                (d.DeviceType != null && EF.Functions.Like(d.DeviceType.Name, like)) ||
                (d.AssignedUser != null && EF.Functions.Like(d.AssignedUser.FullName, like)) ||
                (d.Site != null && EF.Functions.Like(d.Site.Name, like)));
        }

        return q;
    }
}
