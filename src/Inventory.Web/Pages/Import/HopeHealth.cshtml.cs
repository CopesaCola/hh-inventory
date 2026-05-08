using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Inventory.Web.Pages.Import;

public class HopeHealthModel : PageModel
{
    private readonly HopeHealthImportService _hh;
    public HopeHealthModel(HopeHealthImportService hh) => _hh = hh;

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public bool DryRun { get; set; } = true;

    [BindProperty]
    public bool SkipItMaster { get; set; }

    public HopeHealthImportResult? Result { get; set; }
    public bool WasDryRun { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Choose a .xlsx file.");
            return Page();
        }
        if (!Upload.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Upload), "File must be .xlsx.");
            return Page();
        }

        // ClosedXML needs a seekable stream; copy to memory first.
        using var ms = new MemoryStream();
        await Upload.CopyToAsync(ms);
        ms.Position = 0;

        Result = await _hh.ImportAsync(ms, DryRun, SkipItMaster);
        WasDryRun = DryRun;

        TempData["Message"] = DryRun
            ? $"Dry run complete: {Result.Inserted} would insert, {Result.Updated} would update, {Result.Skipped} skipped."
            : $"Import complete: {Result.Inserted} inserted, {Result.Updated} updated, {Result.Skipped} skipped.";
        return Page();
    }
}
