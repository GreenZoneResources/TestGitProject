using System.Text;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Features.StatusMonitoring.Dtos;
using BulkReversal.Application.Features.Upload;
using BulkReversal.Application.Features.Upload.Dtos;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BulkReversal.Infrastructure.Reporting;

/// <summary>Excel/CSV/PDF artifacts for FR-05 (template), FR-08 (error report), FR-17 (status export).</summary>
public class ReportExportService : IReportExportService
{
    private static readonly string[] SampleRow =
    [
        "1", "NIP", "0001260805114523000456", "", "0123456789", DateTime.UtcNow.ToString("dd/MM/yyyy"),
        "50000.00", "NIP", "GTBank", "", "No value received by beneficiary", ""
    ];

    public byte[] BuildUploadTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Reversal Upload Template");

        for (var i = 0; i < UploadTemplateColumns.All.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = UploadTemplateColumns.All[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E1B8C");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // One illustrative sample row, matching the BRD Section 6 examples.
        for (var i = 0; i < SampleRow.Length; i++)
        {
            sheet.Cell(2, i + 1).Value = SampleRow[i];
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] BuildUploadTemplateCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', UploadTemplateColumns.All.Select(CsvField)));
        sb.AppendLine(string.Join(',', SampleRow.Select(CsvField)));

        // UTF-8 BOM so Excel (still the most common consumer of a downloaded .csv) detects the
        // encoding correctly instead of mis-rendering non-ASCII characters.
        return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string CsvField(string value)
    {
        value = SanitizeAgainstFormulaInjection(value);
        var needsQuoting = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>
    /// Defends against CSV/Excel "formula injection": a cell value that starts with =, +, -, or @
    /// can be interpreted as a formula by Excel/Sheets/other spreadsheet tools when the file is
    /// opened or re-imported (OWASP: CSV Injection), letting attacker-controlled data (e.g. a
    /// crafted Session ID / FT Reference or a callback failure reason from the reversal engine)
    /// execute a formula/command on whoever opens the exported report. Prefixing with a leading
    /// apostrophe forces spreadsheet applications to treat the value as plain text.
    /// </summary>
    private static string SanitizeAgainstFormulaInjection(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    public byte[] BuildInvalidRowsReport(UploadBatchResultDto result)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Invalid Rows");

        string[] headers = ["Row", "Session ID / FT Reference", "Errors"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var row in result.InvalidRows.OrderBy(r => r.RowNumber))
        {
            sheet.Cell(rowIndex, 1).Value = row.RowNumber;
            sheet.Cell(rowIndex, 2).Value = SanitizeAgainstFormulaInjection(row.SessionIdOrFtReference);
            sheet.Cell(rowIndex, 3).Value = SanitizeAgainstFormulaInjection(string.Join("; ", row.Errors));
            rowIndex++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] BuildStatusReport(IReadOnlyList<TransactionStatusDto> rows, ExportFormat format) => format switch
    {
        ExportFormat.Excel => BuildStatusReportExcel(rows),
        ExportFormat.Pdf => BuildStatusReportPdf(rows),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
    };

    private static byte[] BuildStatusReportExcel(IReadOnlyList<TransactionStatusDto> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Reversal Status");

        string[] headers = ["Session ID / FT Reference", "Type", "Amount", "Batch Ref", "Status", "External Reference", "Failure Reason", "Last Updated"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = SanitizeAgainstFormulaInjection(row.SessionIdOrFtReference);
            sheet.Cell(rowIndex, 2).Value = row.TransactionType.ToString();
            sheet.Cell(rowIndex, 3).Value = row.TransactionAmount;
            sheet.Cell(rowIndex, 4).Value = SanitizeAgainstFormulaInjection(row.BatchReference);
            sheet.Cell(rowIndex, 5).Value = row.Status?.ToString() ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = SanitizeAgainstFormulaInjection(row.ExternalReference);
            sheet.Cell(rowIndex, 7).Value = SanitizeAgainstFormulaInjection(row.FailureReason);
            sheet.Cell(rowIndex, 8).Value = row.LastUpdated.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            rowIndex++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildStatusReportPdf(IReadOnlyList<TransactionStatusDto> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Text("Failed Transaction Reversal — Status Report")
                    .SemiBold().FontSize(14);

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.3f);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                    });

                    foreach (var header in new[] { "Session ID / FT Ref", "Type", "Amount", "Batch Ref", "Status", "External Ref", "Failure Reason", "Last Updated" })
                    {
                        table.Cell().Element(HeaderCell).Text(header).Bold();
                    }

                    foreach (var row in rows)
                    {
                        table.Cell().Element(BodyCell).Text(row.SessionIdOrFtReference);
                        table.Cell().Element(BodyCell).Text(row.TransactionType.ToString());
                        table.Cell().Element(BodyCell).Text(row.TransactionAmount.ToString("N2"));
                        table.Cell().Element(BodyCell).Text(row.BatchReference);
                        table.Cell().Element(BodyCell).Text(row.Status?.ToString() ?? "-");
                        table.Cell().Element(BodyCell).Text(row.ExternalReference ?? "-");
                        table.Cell().Element(BodyCell).Text(row.FailureReason ?? "-");
                        table.Cell().Element(BodyCell).Text(row.LastUpdated.UtcDateTime.ToString("yyyy-MM-dd HH:mm"));
                    }

                    static IContainer HeaderCell(IContainer c) => c.Background(Colors.Grey.Lighten2).Padding(4).BorderBottom(1);
                    static IContainer BodyCell(IContainer c) => c.Padding(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated ").FontSize(8);
                    x.Span(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontSize(8);
                });
            });
        });

        return document.GeneratePdf();
    }
}
