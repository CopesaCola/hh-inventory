using Inventory.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Pages.Settings;

/// <summary>
/// Operator tools for the SQLite database file:
///  - Backup: produce a consistent snapshot via VACUUM INTO and stream it.
///  - Restore: upload an older .db, run the same migrations the app runs at
///    startup against the uploaded file, then atomically swap it with the
///    live database. A copy of the previous live DB is retained.
///
/// Deliberately does NOT depend on InventoryDbContext via DI on this page,
/// because the restore handler closes connection pools and moves the live
/// database file out from under the running process. Keeping this page
/// pool-free avoids accidentally holding the live file open during the swap.
/// </summary>
public class DatabaseModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public DatabaseModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public string DbPath { get; set; } = "";
    public string DbSizeDisplay { get; set; } = "";
    public bool DbExists { get; set; }

    public void OnGet()
    {
        DbPath = ResolveDbPath();
        DbExists = System.IO.File.Exists(DbPath);
        DbSizeDisplay = DbExists ? FormatBytes(new FileInfo(DbPath).Length) : "(not found)";
    }

    public async Task<IActionResult> OnPostBackupAsync()
    {
        var live = ResolveDbPath();
        if (!System.IO.File.Exists(live))
        {
            TempData["Error"] = "Live database file not found.";
            return RedirectToPage();
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var fileName = $"inventory_backup_{stamp}.db";
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // VACUUM INTO produces a consistent, defragmented snapshot in one shot.
            // It works even if other connections are reading the source database.
            // SQLite's VACUUM INTO syntax expects the destination as a string literal,
            // not a bound parameter, so we inline the path with single-quote escaping.
            using (var src = new SqliteConnection($"Data Source={live}"))
            {
                await src.OpenAsync();
                using var cmd = src.CreateCommand();
                var quoted = tempPath.Replace("'", "''");
                cmd.CommandText = $"VACUUM INTO '{quoted}';";
                await cmd.ExecuteNonQueryAsync();
            }

            // Read into memory so we can delete the temp file before returning the response.
            var bytes = await System.IO.File.ReadAllBytesAsync(tempPath);
            return File(bytes, "application/x-sqlite3", fileName);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Backup failed: {ex.Message}";
            return RedirectToPage();
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { }
        }
    }

    public async Task<IActionResult> OnPostRestoreAsync(IFormFile? upload)
    {
        if (upload is null || upload.Length == 0)
        {
            TempData["Error"] = "No file uploaded.";
            return RedirectToPage();
        }

        var live = ResolveDbPath();
        var folder = Path.GetDirectoryName(live)!;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var pendingPath = Path.Combine(folder, $"inventory_pending_{stamp}.db");
        var replacedPath = Path.Combine(folder, $"inventory_replaced_{stamp}.db");

        // Save the upload so we can validate and migrate it before swapping.
        await using (var fs = new FileStream(pendingPath, FileMode.Create, FileAccess.Write))
        {
            await upload.CopyToAsync(fs);
        }

        // Validate header — every SQLite file starts with the magic string
        // "SQLite format 3\0" (16 bytes). Cheap rejection for non-SQLite uploads.
        var header = new byte[16];
        await using (var hf = System.IO.File.OpenRead(pendingPath))
        {
            var read = await hf.ReadAsync(header.AsMemory(0, 16));
            if (read < 16 || System.Text.Encoding.ASCII.GetString(header).TrimEnd('\0') != "SQLite format 3")
            {
                TryDelete(pendingPath);
                TempData["Error"] = "Uploaded file is not a SQLite database (header check failed).";
                return RedirectToPage();
            }
        }

        // Run the same startup migration pipeline on the pending file:
        //   EnsureCreated()    → creates any tables that don't yet exist
        //   EnsureSchemaAsync()→ adds any columns that don't yet exist
        //   SeedDefaultsAsync()→ seeds default lookup rows if their tables are empty
        // Disposed cleanly so no file handles remain on `pendingPath`.
        try
        {
            var opts = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite($"Data Source={pendingPath}")
                .Options;
            using var pctx = new InventoryDbContext(opts);
            pctx.Database.EnsureCreated();
            await InventoryDbContext.EnsureSchemaAsync(pctx);
            await InventoryDbContext.SeedDefaultsAsync(pctx);
        }
        catch (Exception ex)
        {
            TryDelete(pendingPath);
            TempData["Error"] = $"Migration of uploaded file failed: {ex.Message}";
            return RedirectToPage();
        }

        // Connection pooling normally holds open file handles to the live DB; clear
        // the pool so File.Move can rename it. After the swap, the next request that
        // creates a new SqliteConnection will hit the freshly-restored file.
        SqliteConnection.ClearAllPools();
        // Give Windows a beat to fully release any handle that just left the pool.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (System.IO.File.Exists(live)) System.IO.File.Move(live, replacedPath);
        }
        catch (Exception ex)
        {
            TryDelete(pendingPath);
            TempData["Error"] = $"Could not back up current database before swap: {ex.Message}. " +
                                "Stop the app and replace inventory.db manually.";
            return RedirectToPage();
        }

        try
        {
            System.IO.File.Move(pendingPath, live);
        }
        catch (Exception ex)
        {
            // Recovery: try to put the original file back so the app keeps working.
            try { System.IO.File.Move(replacedPath, live); } catch { }
            TempData["Error"] = $"Could not move migrated file into place: {ex.Message}";
            return RedirectToPage();
        }

        TempData["Message"] =
            $"Database restored. A safety copy of the previous file was saved as " +
            $"'{Path.GetFileName(replacedPath)}' in the app folder. " +
            $"Refresh the page; for absolute certainty restart the app.";
        return RedirectToPage();
    }

    private string ResolveDbPath()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        var builder = new SqliteConnectionStringBuilder(cs);
        var path = builder.DataSource;
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_env.ContentRootPath, path);
        return Path.GetFullPath(path);
    }

    private static void TryDelete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:F1} {units[i]}";
    }
}
