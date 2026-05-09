using Inventory.Web.Data;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<InventoryDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<HopeHealthImportService>();
builder.Services.AddScoped<ILabelService, LabelService>();
builder.Services.AddScoped<CustomFieldService>();

builder.Services.AddRazorPages();

// Allow large workbook uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 100L * 1024 * 1024; // 100 MB
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    db.Database.EnsureCreated();
    await InventoryDbContext.EnsureSchemaAsync(db);
    await InventoryDbContext.SeedDefaultsAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/api/users/search", async (InventoryDbContext db, string? q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<object>());
    var like = $"%{q.Trim()}%";
    var users = await db.UserProfiles
        .Include(u => u.Site)
        .Where(u =>
            EF.Functions.Like(u.FullName, like) ||
            EF.Functions.Like(u.Username ?? "", like) ||
            EF.Functions.Like(u.Email ?? "", like))
        .OrderBy(u => u.FullName)
        .Take(15)
        .Select(u => new { id = u.Id, name = u.FullName, site = u.Site != null ? u.Site.Name : null })
        .ToListAsync();
    return Results.Ok(users);
}).RequireAuthorization();

app.MapGet("/api/search/suggest", async (InventoryDbContext db, string? q) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        return Results.Ok(new { devices = Array.Empty<object>(), users = Array.Empty<object>(), sites = Array.Empty<object>() });

    var like = $"%{q.Trim()}%";

    var devices = await db.Devices
        .Include(d => d.DeviceType)
        .Include(d => d.Site)
        .Where(d =>
            EF.Functions.Like(d.SerialNumber ?? "", like) ||
            EF.Functions.Like(d.AssetTag ?? "", like) ||
            EF.Functions.Like(d.Model, like) ||
            (d.DeviceType != null && EF.Functions.Like(d.DeviceType.Name, like)))
        .OrderByDescending(d => d.LastModifiedUtc)
        .Take(6)
        .Select(d => new
        {
            id = d.Id,
            name = (d.SerialNumber ?? d.AssetTag) + " — " + d.Model,
            subtitle = (d.DeviceType != null ? d.DeviceType.Name : "") + (d.Site != null ? " · " + d.Site.Name : ""),
        })
        .ToListAsync();

    var users = await db.UserProfiles
        .Include(u => u.Site)
        .Where(u =>
            EF.Functions.Like(u.FullName, like) ||
            EF.Functions.Like(u.Username ?? "", like) ||
            EF.Functions.Like(u.Email ?? "", like))
        .OrderBy(u => u.FullName)
        .Take(6)
        .Select(u => new
        {
            id = u.Id,
            name = u.FullName,
            subtitle = u.Site != null ? u.Site.Name : (u.Email ?? ""),
        })
        .ToListAsync();

    var sites = await db.Sites
        .Where(s => EF.Functions.Like(s.Name, like) || EF.Functions.Like(s.Address ?? "", like))
        .OrderBy(s => s.Name)
        .Take(6)
        .Select(s => new { id = s.Id, name = s.Name, subtitle = s.Address ?? "" })
        .ToListAsync();

    return Results.Ok(new { devices, users, sites });
}).RequireAuthorization();

app.Run();
