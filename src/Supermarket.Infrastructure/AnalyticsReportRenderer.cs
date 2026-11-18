using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Supermarket.Application;

namespace Supermarket.Infrastructure;

public sealed class AnalyticsReportRenderer : IAnalyticsReportRenderer
{
    static AnalyticsReportRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public AnalyticsExportFile Render(AnalyticsReportData report, string format) => format switch
    {
        "csv" => new(Csv(report), "text/csv; charset=utf-8", Name(report, "csv")),
        "xlsx" => new(Xlsx(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Name(report, "xlsx")),
        "pdf" => new(Pdf(report), "application/pdf", Name(report, "pdf")),
        _ => throw new Supermarket.Application.ApplicationException("INVALID_REPORT_FORMAT", "Use csv, xlsx, or pdf report format.", 400)
    };

    private static string Name(AnalyticsReportData report, string extension)
        => $"operational-analytics_{report.Period.From:yyyyMMdd}_{report.Period.To:yyyyMMdd}_{(report.Period.ZoneId is null ? "all-zones" : report.Period.ZoneId)}.{extension}";

    private static byte[] Csv(AnalyticsReportData report)
    {
        var text = new StringBuilder();
        void Row(params object?[] values) => text.AppendLine(string.Join(',', values.Select(CsvCell)));
        Row("Operational analytics report");
        Row("From", report.Period.From); Row("To", report.Period.To); Row("Timezone", report.Period.TimeZone);
        Row("Generated at UTC", report.Period.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        Row("Zone ID", report.Period.ZoneId?.ToString() ?? "ALL");
        Row(); Row("Notes"); foreach (var note in report.Notes) Row(note);
        Row(); Row("Summary", "Metric", "Value");
        foreach (var item in Summary(report.Summary)) Row("Summary", item.Label, item.Value);
        Row(); Row("Daily", "Date", "Events", "Incidents excluding dismissed", "Dismissed incidents", "Tasks", "Response SLA met %");
        foreach (var day in report.Days) Row("Daily", day.Date, day.Summary.Events.Total, day.Summary.Incidents.Total,
            day.Summary.Incidents.Dismissed, day.Summary.Tasks.Total, day.Summary.Tasks.ResponseSla.MetPercent);
        Row(); Row("Zones", "Zone ID", "Zone", "Events", "Incidents excluding dismissed", "Dismissed incidents", "Tasks", "Verified", "Response SLA met %");
        foreach (var zone in report.Zones) Row("Zones", zone.ZoneId, zone.ZoneName, zone.Summary.Events.Total,
            zone.Summary.Incidents.Total, zone.Summary.Incidents.Dismissed, zone.Summary.Tasks.Total,
            zone.Summary.Tasks.ByStatus.GetValueOrDefault("VERIFIED"), zone.Summary.Tasks.ResponseSla.MetPercent);
        return [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string CsvCell(object? value)
    {
        var cell = value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        if (cell.Length > 0 && "=+-@".Contains(cell[0])) cell = "'" + cell;
        return '"' + cell.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';
    }

    private static byte[] Xlsx(AnalyticsReportData report)
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Operational analytics report";
        summary.Range(1, 1, 1, 2).Merge().Style.Font.SetBold().Font.SetFontSize(16);
        var metadata = new (string, object?)[] { ("From", report.Period.From), ("To", report.Period.To),
            ("Timezone", report.Period.TimeZone), ("Generated at UTC", report.Period.GeneratedAt),
            ("Zone ID", report.Period.ZoneId?.ToString() ?? "ALL") };
        var row = 3;
        foreach (var (label, value) in metadata) { summary.Cell(row, 1).Value = label; summary.Cell(row++, 2).Value = Safe(value); }
        row++;
        summary.Cell(row, 1).Value = "Metric"; summary.Cell(row, 2).Value = "Value"; Header(summary.Range(row, 1, row, 2));
        foreach (var item in Summary(report.Summary)) { row++; summary.Cell(row, 1).Value = item.Label; summary.Cell(row, 2).Value = Safe(item.Value); }
        summary.Columns().AdjustToContents();

        var daily = workbook.Worksheets.Add("Daily");
        WriteTable(daily, ["Date", "Events", "Incidents excluding dismissed", "Dismissed incidents", "Tasks", "Response SLA met %"],
            report.Days.Select(day => new object?[] { day.Date, day.Summary.Events.Total, day.Summary.Incidents.Total,
                day.Summary.Incidents.Dismissed, day.Summary.Tasks.Total, day.Summary.Tasks.ResponseSla.MetPercent }));
        var zones = workbook.Worksheets.Add("Zones");
        WriteTable(zones, ["Zone ID", "Zone", "Events", "Incidents excluding dismissed", "Dismissed incidents", "Tasks", "Verified", "Response SLA met %"],
            report.Zones.Select(zone => new object?[] { zone.ZoneId, zone.ZoneName, zone.Summary.Events.Total, zone.Summary.Incidents.Total,
                zone.Summary.Incidents.Dismissed, zone.Summary.Tasks.Total, zone.Summary.Tasks.ByStatus.GetValueOrDefault("VERIFIED"), zone.Summary.Tasks.ResponseSla.MetPercent }));
        var notes = workbook.Worksheets.Add("Notes");
        notes.Cell(1, 1).Value = "Data notes"; Header(notes.Cell(1, 1).AsRange());
        for (var index = 0; index < report.Notes.Length; index++) notes.Cell(index + 2, 1).Value = report.Notes[index];
        notes.Column(1).Width = 120; notes.Column(1).Style.Alignment.WrapText = true;
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }

    private static void WriteTable(IXLWorksheet sheet, string[] headings, IEnumerable<object?[]> rows)
    {
        for (var column = 0; column < headings.Length; column++) sheet.Cell(1, column + 1).Value = headings[column];
        Header(sheet.Range(1, 1, 1, headings.Length));
        var row = 2;
        foreach (var values in rows) { for (var column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = Safe(values[column]); row++; }
        sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(1, Math.Max(1, row - 1));
        if (row > 2) sheet.Range(1, 1, row - 1, headings.Length).CreateTable();
    }

    private static XLCellValue Safe(object? value) => value switch
    {
        null => Blank.Value,
        Guid guid => guid.ToString(),
        DateOnly date => date.ToString("yyyy-MM-dd"),
        DateTime instant => instant.ToString("O", CultureInfo.InvariantCulture),
        int number => number,
        double number => number,
        _ => SanitizeSpreadsheet(value.ToString() ?? "")
    };
    private static string SanitizeSpreadsheet(string value) => value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
    private static void Header(IXLRange range) => range.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DDEBF7"));

    private static byte[] Pdf(AnalyticsReportData report) => Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape()); page.Margin(24); page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Column(column =>
            {
                column.Item().Text("Operational analytics report").Bold().FontSize(18);
                column.Item().Text($"{report.Period.From:yyyy-MM-dd} — {report.Period.To:yyyy-MM-dd} · {report.Period.TimeZone} · Zone: {report.Period.ZoneId?.ToString() ?? "All"}");
                column.Item().Text($"Generated {report.Period.GeneratedAt:O} UTC").FontColor(Colors.Grey.Darken1);
            });
            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(10);
                column.Item().Text("Summary").Bold().FontSize(12);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(3); columns.RelativeColumn(); });
                    foreach (var item in Summary(report.Summary)) { table.Cell().Element(Cell).Text(item.Label); table.Cell().Element(Cell).Text(Display(item.Value)); }
                });
                column.Item().Text("Daily activity").Bold().FontSize(12);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(2); for (var i = 0; i < 5; i++) columns.RelativeColumn(); });
                    PdfHeader(table, "Date", "Events", "Incidents", "Dismissed", "Tasks", "Response SLA %");
                    foreach (var day in report.Days) PdfRow(table, day.Date, day.Summary.Events.Total, day.Summary.Incidents.Total,
                        day.Summary.Incidents.Dismissed, day.Summary.Tasks.Total, day.Summary.Tasks.ResponseSla.MetPercent);
                });
                column.Item().Text("Zone breakdown").Bold().FontSize(12);
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(3); for (var i = 0; i < 6; i++) columns.RelativeColumn(); });
                    PdfHeader(table, "Zone", "Events", "Incidents", "Dismissed", "Tasks", "Verified", "Response SLA %");
                    foreach (var zone in report.Zones) PdfRow(table, zone.ZoneName, zone.Summary.Events.Total, zone.Summary.Incidents.Total,
                        zone.Summary.Incidents.Dismissed, zone.Summary.Tasks.Total, zone.Summary.Tasks.ByStatus.GetValueOrDefault("VERIFIED"), zone.Summary.Tasks.ResponseSla.MetPercent);
                });
                column.Item().Text("Data notes").Bold().FontSize(12);
                foreach (var note in report.Notes) column.Item().Text("• " + note).FontColor(Colors.Grey.Darken2);
            });
            page.Footer().AlignCenter().Text(text => { text.Span("Page "); text.CurrentPageNumber(); text.Span(" of "); text.TotalPages(); });
        });
    }).GeneratePdf();

    private static void PdfHeader(TableDescriptor table, params string[] values)
    {
        foreach (var value in values) table.Cell().Background(Colors.Blue.Lighten4).Element(Cell).Text(value).Bold();
    }
    private static void PdfRow(TableDescriptor table, params object?[] values)
    {
        foreach (var value in values) table.Cell().Element(Cell).Text(Display(value));
    }
    private static IContainer Cell(IContainer container) => container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
    private static string Display(object? value) => value switch
    {
        null => "—",
        double number => number.ToString("0.0", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "—",
        _ => value.ToString() ?? "—"
    };

    private static IEnumerable<(string Label, object? Value)> Summary(AnalyticsSummary value)
    {
        yield return ("Events detected", value.Events.Total); yield return ("Active events", value.Events.Active);
        yield return ("Latest critical events", value.Events.LatestCritical); yield return ("Event clearance average seconds", value.Events.ClearTime.AverageSeconds);
        yield return ("Incidents recorded", value.Incidents.Recorded); yield return ("Incidents excluding dismissed", value.Incidents.Total);
        yield return ("Open incidents", value.Incidents.Open); yield return ("Closed incidents", value.Incidents.Closed);
        yield return ("Dismissed incidents excluded", value.Incidents.Dismissed); yield return ("Incident resolution average seconds", value.Incidents.ResolutionTime.AverageSeconds);
        yield return ("Tasks created", value.Tasks.Total); yield return ("Verified tasks", value.Tasks.ByStatus.GetValueOrDefault("VERIFIED"));
        yield return ("Task response average seconds", value.Tasks.ResponseTime.AverageSeconds); yield return ("Task completion average seconds", value.Tasks.CompletionTime.AverageSeconds);
        yield return ("Response SLA met percent", value.Tasks.ResponseSla.MetPercent); yield return ("Resolution SLA met percent", value.Tasks.ResolutionSla.MetPercent);
    }
}
