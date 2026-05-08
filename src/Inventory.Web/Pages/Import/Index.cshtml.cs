using System.Text;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Inventory.Web.Pages.Import;

public class IndexModel : PageModel
{
    private readonly ImportService _import;
    public IndexModel(ImportService import) => _import = import;

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public ImportResult? Result { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Pick a .csv or .xlsx file to upload.");
            return Page();
        }

        var ext = Path.GetExtension(Upload.FileName).ToLowerInvariant();
        if (ext != ".csv" && ext != ".xlsx")
        {
            ModelState.AddModelError(nameof(Upload), "File must be .csv or .xlsx.");
            return Page();
        }

        await using var stream = Upload.OpenReadStream();
        Result = await _import.ImportAsync(stream, Upload.FileName);
        TempData["Message"] = $"Import complete: {Result.Inserted} inserted, {Result.Updated} updated, {Result.Skipped} skipped.";
        return Page();
    }

    public IActionResult OnGetTemplate()
    {
        var headers = "DeviceType,Model,SerialNumber,AssetTag,Status,LocationWithinSite,WindowsVersion,IsGrantFunded,AssignedUser,Site";
        var sample = "Laptop,Dell Latitude 5440,SN12345,AT-001,InUse,Room 204,11,No,Jane Doe,HQ";
        var content = headers + Environment.NewLine + sample + Environment.NewLine;
        return File(Encoding.UTF8.GetBytes(content), "text/csv", "inventory-template.csv");
    }
}
