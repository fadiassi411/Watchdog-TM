using ClosedXML.Excel;
namespace Watchdog.TM;

public static class Exporter
{
    public static int Export(Store store, Sensor sensor, Controller controller, DateTimeOffset from, DateTimeOffset to, string path, CancellationToken ct)
    {
        var rows = store.Samples(sensor.Id, from, to, ct);
        using var book = new XLWorkbook();
        int sheetNo = 0;
        IXLWorksheet? sheet = null;
        int row = 0;
        foreach (var sample in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (sheet == null || row >= 1048576)
            {
                sheet = book.Worksheets.Add("Samples " + (++sheetNo));
                string[] headers = ["Sensor", "Controller", "Date", "Time", "Temperature °C", "Quality / status", "Timezone", "UTC timestamp", "UTC offset"];
                for (int c = 0; c < headers.Length; c++)
                    sheet.Cell(1, c + 1).Value = headers[c];
                sheet.Row(1).Style.Font.Bold = true;
                sheet.SheetView.FreezeRows(1);
                row = 1;
            }
            row++;
            var t = sample.At.ToLocalTime();
            sheet.Cell(row, 1).Value = sensor.Name;
            sheet.Cell(row, 2).Value = controller.Name;
            sheet.Cell(row, 3).Value = t.Date;
            sheet.Cell(row, 3).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 4).Value = t.TimeOfDay.TotalDays;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "hh:mm:ss";
            if (sample.Temperature.HasValue)
                sheet.Cell(row, 5).Value = sample.Temperature.Value;
            sheet.Cell(row, 6).Value = sample.Quality;
            sheet.Cell(row, 7).Value = TimeZoneInfo.Local.Id;
            sheet.Cell(row, 8).Value = sample.At.UtcDateTime;
            sheet.Cell(row, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            sheet.Cell(row, 9).Value = t.ToString("zzz");
        }
        if (sheet == null)
        {
            sheet = book.Worksheets.Add("No samples");
            sheet.Cell(1, 1).Value = "No stored samples in selected range.";
        }
        foreach (var s in book.Worksheets)
            s.Columns(1, 9).Width = 24;
        ct.ThrowIfCancellationRequested();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.xlsx";
        try
        {
            book.SaveAs(temp);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return rows.Count;
    }
}

