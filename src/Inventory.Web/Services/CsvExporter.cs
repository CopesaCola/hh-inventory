using System.Globalization;
using System.Text;
using CsvHelper;

namespace Inventory.Web.Services;

public static class CsvExporter
{
    /// <summary>
    /// Serialize a sequence of records as a UTF-8 CSV with BOM (Excel-friendly).
    /// </summary>
    public static byte[] Build<T>(IEnumerable<T> rows)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.UTF8.GetPreamble());
        using (var sw = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true))
        using (var csv = new CsvWriter(sw, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(rows);
        }
        return ms.ToArray();
    }

    public static string MakeFilename(string entityName, string? query)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.IsNullOrWhiteSpace(query)) return $"{entityName}-{date}.csv";
        var safe = new string(query.Trim().Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrEmpty(safe)
            ? $"{entityName}-{date}.csv"
            : $"{entityName}-{safe}-{date}.csv";
    }
}
