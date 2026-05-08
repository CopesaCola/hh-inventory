using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages;

public class SearchModel : PageModel
{
    private const int PerSectionLimit = 25;

    private readonly InventoryDbContext _db;
    public SearchModel(InventoryDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public List<Device> Devices { get; set; } = new();
    public List<UserProfile> Users { get; set; } = new();
    public List<Site> Sites { get; set; } = new();

    public int DeviceTotal { get; set; }
    public int UserTotal { get; set; }
    public int SiteTotal { get; set; }

    public bool HasAnyResults => DeviceTotal + UserTotal + SiteTotal > 0;

    public async Task OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return;

        var (deviceQ, userQ, siteQ) = BuildQueries();
        DeviceTotal = await deviceQ.CountAsync();
        UserTotal = await userQ.CountAsync();
        SiteTotal = await siteQ.CountAsync();

        Devices = await deviceQ.OrderByDescending(d => d.LastModifiedUtc).Take(PerSectionLimit).ToListAsync();
        Users = await userQ.OrderBy(u => u.FullName).Take(PerSectionLimit).ToListAsync();
        Sites = await siteQ.OrderBy(s => s.Name).Take(PerSectionLimit).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportDevicesAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return BadRequest();
        var (q, _, _) = BuildQueries();
        var devices = await q.OrderByDescending(d => d.LastModifiedUtc).ToListAsync();
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
            LastModifiedUtc = d.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            LastModifiedBy = d.LastModifiedBy,
        });
        return File(CsvExporter.Build(rows), "text/csv", CsvExporter.MakeFilename("devices", Query));
    }

    public async Task<IActionResult> OnGetExportUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return BadRequest();
        var (_, q, _) = BuildQueries();
        var users = await q.OrderBy(u => u.FullName).ToListAsync();
        var rows = users.Select(u => new
        {
            FullName = u.FullName,
            Username = u.Username ?? "",
            Email = u.Email ?? "",
            Department = u.Department?.Name ?? "",
            Site = u.Site?.Name ?? "",
            DeviceCount = u.Devices.Count,
        });
        return File(CsvExporter.Build(rows), "text/csv", CsvExporter.MakeFilename("users", Query));
    }

    public async Task<IActionResult> OnGetExportSitesAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) return BadRequest();
        var (_, _, q) = BuildQueries();
        var sites = await q.OrderBy(s => s.Name).ToListAsync();
        var rows = sites.Select(s => new
        {
            Name = s.Name,
            Address = s.Address ?? "",
            UserCount = s.Users.Count,
            DeviceCount = s.Devices.Count,
        });
        return File(CsvExporter.Build(rows), "text/csv", CsvExporter.MakeFilename("sites", Query));
    }

    private (IQueryable<Device> devices, IQueryable<UserProfile> users, IQueryable<Site> sites) BuildQueries()
    {
        var like = $"%{Query!.Trim()}%";

        var deviceQ = _db.Devices
            .Include(d => d.AssignedUser)
            .Include(d => d.Site)
            .Include(d => d.DeviceType)
            .Include(d => d.Status)
            .Where(d =>
                EF.Functions.Like(d.SerialNumber ?? "", like) ||
                EF.Functions.Like(d.AssetTag ?? "", like) ||
                EF.Functions.Like(d.Model, like) ||
                EF.Functions.Like(d.LocationWithinSite ?? "", like) ||
                (d.DeviceType != null && EF.Functions.Like(d.DeviceType.Name, like)) ||
                (d.AssignedUser != null && EF.Functions.Like(d.AssignedUser.FullName, like)) ||
                (d.Site != null && EF.Functions.Like(d.Site.Name, like)));

        var userQ = _db.UserProfiles
            .Include(u => u.Site)
            .Include(u => u.Department)
            .Include(u => u.Devices)
            .Where(u =>
                EF.Functions.Like(u.FullName, like) ||
                EF.Functions.Like(u.Username ?? "", like) ||
                EF.Functions.Like(u.Email ?? "", like) ||
                (u.Department != null && EF.Functions.Like(u.Department.Name, like)));

        var siteQ = _db.Sites
            .Include(s => s.Users)
            .Include(s => s.Devices)
            .Where(s =>
                EF.Functions.Like(s.Name, like) ||
                EF.Functions.Like(s.Address ?? "", like));

        return (deviceQ, userQ, siteQ);
    }
}
