using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using RadioMaker.Core.Logging;

namespace RadioMaker.UI.Services;

/// <summary>
/// Formatos soportados para exportar el log.
/// </summary>
public enum LogExportFormat
{
    Txt = 0,
    Csv = 1,
    Html = 2,
    Xlsx = 3,
    Pdf = 4
}

/// <summary>
/// Exporta el log de la aplicacion a diferentes formatos.
///
/// Formatos soportados:
///   - TXT: texto plano con columnas alineadas.
///   - CSV: separador punto y coma (;) para compatibilidad con Excel ES.
///   - HTML: pagina con tabla y estilos embebidos.
///   - XLSX: libro de Excel con encabezados formateados (ClosedXML).
///   - PDF: documento con tabla (QuestPDF).
///
/// Columnas siempre presentes: Fecha, Hora, Nivel, Categoria, Mensaje.
///
/// NOTA: la licencia de QuestPDF se configura UNA SOLA VEZ en
/// Program.Main al arrancar la app. No hay que setearla aca.
/// </summary>
public static class LogExporter
{
    /// <summary>Extension por formato (con punto).</summary>
    public static string GetExtension(LogExportFormat format) => format switch
    {
        LogExportFormat.Txt => ".txt",
        LogExportFormat.Csv => ".csv",
        LogExportFormat.Html => ".html",
        LogExportFormat.Xlsx => ".xlsx",
        LogExportFormat.Pdf => ".pdf",
        _ => ".txt"
    };

    /// <summary>Nombre legible del formato.</summary>
    public static string GetDisplayName(LogExportFormat format) => format switch
    {
        LogExportFormat.Txt => "Texto plano (.txt)",
        LogExportFormat.Csv => "CSV (.csv)",
        LogExportFormat.Html => "Pagina HTML (.html)",
        LogExportFormat.Xlsx => "Excel (.xlsx)",
        LogExportFormat.Pdf => "Documento PDF (.pdf)",
        _ => format.ToString()
    };

    /// <summary>Filtro de archivos para el dialogo nativo de guardar.</summary>
    public static string GetFilePickerPattern(LogExportFormat format) => format switch
    {
        LogExportFormat.Txt => "*.txt",
        LogExportFormat.Csv => "*.csv",
        LogExportFormat.Html => "*.html",
        LogExportFormat.Xlsx => "*.xlsx",
        LogExportFormat.Pdf => "*.pdf",
        _ => "*.*"
    };

    /// <summary>
    /// Exporta las entradas al archivo indicado en el formato especificado.
    /// Devuelve true si la exportacion fue exitosa.
    /// </summary>
    public static bool Export(IReadOnlyList<LogEntry> entries, string filePath, LogExportFormat format)
    {
        if (entries is null) return false;
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            switch (format)
            {
                case LogExportFormat.Txt:
                    ExportTxt(entries, filePath);
                    break;

                case LogExportFormat.Csv:
                    ExportCsv(entries, filePath);
                    break;

                case LogExportFormat.Html:
                    ExportHtml(entries, filePath);
                    break;

                case LogExportFormat.Xlsx:
                    ExportXlsx(entries, filePath);
                    break;

                case LogExportFormat.Pdf:
                    ExportPdf(entries, filePath);
                    break;

                default:
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // TXT

    private static void ExportTxt(IReadOnlyList<LogEntry> entries, string path)
    {
        // Ancho fijo por columna, calculado a partir del contenido.
        int dateW = 10;   // "yyyy-MM-dd"
        int timeW = 8;    // "HH:mm:ss"
        int levelW = 5;   // "DEBUG" / "ERROR" = 5 chars
        int catW = Math.Max(12, entries.Count > 0
            ? Math.Min(30, entries.Max(e => e.Category?.Length ?? 0))
            : 12);

        var sb = new StringBuilder();

        // Encabezado
        sb.Append(PadRight("Fecha", dateW)).Append("  ");
        sb.Append(PadRight("Hora", timeW)).Append("  ");
        sb.Append(PadRight("Nivel", levelW)).Append("  ");
        sb.Append(PadRight("Categoria", catW)).Append("  ");
        sb.Append("Mensaje");
        sb.AppendLine();

        sb.Append(new string('-', dateW)).Append("  ");
        sb.Append(new string('-', timeW)).Append("  ");
        sb.Append(new string('-', levelW)).Append("  ");
        sb.Append(new string('-', catW)).Append("  ");
        sb.AppendLine(new string('-', 60));

        foreach (var e in entries)
        {
            sb.Append(PadRight(e.Timestamp.ToString("yyyy-MM-dd"), dateW)).Append("  ");
            sb.Append(PadRight(e.Timestamp.ToString("HH:mm:ss"), timeW)).Append("  ");
            sb.Append(PadRight(e.LevelText, levelW)).Append("  ");
            sb.Append(PadRight(Truncate(e.Category ?? string.Empty, catW), catW)).Append("  ");
            sb.AppendLine(e.Message);
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    // ─────────────────────────────────────────────────────────────────────
    // CSV

    private static void ExportCsv(IReadOnlyList<LogEntry> entries, string path)
    {
        var sb = new StringBuilder();

        // BOM UTF-8 para que Excel respete los acentos.
        sb.Append('\uFEFF');

        // Encabezado
        sb.AppendLine("Fecha;Hora;Nivel;Categoria;Mensaje");

        foreach (var e in entries)
        {
            sb.Append(EscapeCsv(e.Timestamp.ToString("yyyy-MM-dd"))).Append(';');
            sb.Append(EscapeCsv(e.Timestamp.ToString("HH:mm:ss"))).Append(';');
            sb.Append(EscapeCsv(e.LevelText)).Append(';');
            sb.Append(EscapeCsv(e.Category ?? string.Empty)).Append(';');
            sb.Append(EscapeCsv(e.Message));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Si contiene separador, comillas o salto de linea, entrecomillar y duplicar quotes.
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    // ─────────────────────────────────────────────────────────────────────
    // HTML

    private static void ExportHtml(IReadOnlyList<LogEntry> entries, string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"es\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>RadioMaker - Log ({DateTime.Now:yyyy-MM-dd HH:mm})</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f8f9fb; color: #0d0d0d; margin: 24px; }");
        sb.AppendLine("  h1 { font-size: 20px; margin: 0 0 4px; }");
        sb.AppendLine("  .meta { color: #6b7280; font-size: 12px; margin-bottom: 20px; }");
        sb.AppendLine("  table { border-collapse: collapse; width: 100%; background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
        sb.AppendLine("  th { background: #4d6bfe; color: white; text-align: left; padding: 8px 10px; font-size: 12px; font-weight: 600; }");
        sb.AppendLine("  td { padding: 6px 10px; font-size: 12px; border-bottom: 1px solid #eef0f4; vertical-align: top; }");
        sb.AppendLine("  tr:last-child td { border-bottom: none; }");
        sb.AppendLine("  tr:hover td { background: #f4f6fa; }");
        sb.AppendLine("  .hora { font-family: ui-monospace, Consolas, monospace; color: #6b7280; white-space: nowrap; }");
        sb.AppendLine("  .nivel { font-family: ui-monospace, Consolas, monospace; font-weight: bold; font-size: 11px; }");
        sb.AppendLine("  .debug   { color: #6b7280; }");
        sb.AppendLine("  .info    { color: #4d6bfe; }");
        sb.AppendLine("  .warning { color: #f59e0b; }");
        sb.AppendLine("  .error   { color: #ef4444; }");
        sb.AppendLine("  .categoria { color: #6b7280; font-size: 11px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>Log de RadioMaker</h1>");
        sb.AppendLine($"<div class=\"meta\">Exportado el {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {entries.Count} entrada(s)</div>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead>");
        sb.AppendLine("<tr>");
        sb.AppendLine("<th>Fecha</th><th>Hora</th><th>Nivel</th><th>Categoria</th><th>Mensaje</th>");
        sb.AppendLine("</tr>");
        sb.AppendLine("</thead>");
        sb.AppendLine("<tbody>");

        foreach (var e in entries)
        {
            string levelClass = e.Level switch
            {
                LogLevel.Debug => "debug",
                LogLevel.Info => "info",
                LogLevel.Warning => "warning",
                LogLevel.Error => "error",
                _ => ""
            };

            sb.Append("<tr>");
            sb.Append($"<td class=\"hora\">{HtmlEscape(e.Timestamp.ToString("yyyy-MM-dd"))}</td>");
            sb.Append($"<td class=\"hora\">{HtmlEscape(e.Timestamp.ToString("HH:mm:ss"))}</td>");
            sb.Append($"<td class=\"nivel {levelClass}\">{HtmlEscape(e.LevelText)}</td>");
            sb.Append($"<td class=\"categoria\">{HtmlEscape(e.Category ?? string.Empty)}</td>");
            sb.Append($"<td>{HtmlEscape(e.Message)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string HtmlEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    // ─────────────────────────────────────────────────────────────────────
    // XLSX (ClosedXML)

    private static void ExportXlsx(IReadOnlyList<LogEntry> entries, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Log");

        // Encabezados
        ws.Cell(1, 1).Value = "Fecha";
        ws.Cell(1, 2).Value = "Hora";
        ws.Cell(1, 3).Value = "Nivel";
        ws.Cell(1, 4).Value = "Categoria";
        ws.Cell(1, 5).Value = "Mensaje";

        // Estilo de encabezados
        var headerRange = ws.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4D6BFE");
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        // Filas de datos
        int row = 2;
        foreach (var e in entries)
        {
            ws.Cell(row, 1).Value = e.Timestamp.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = e.Timestamp.ToString("HH:mm:ss");
            ws.Cell(row, 3).Value = e.LevelText;
            ws.Cell(row, 4).Value = e.Category ?? string.Empty;
            ws.Cell(row, 5).Value = e.Message;

            // Color de la celda de nivel segun severidad.
            var nivelCell = ws.Cell(row, 3);
            nivelCell.Style.Font.Bold = true;
            nivelCell.Style.Font.FontColor = e.Level switch
            {
                LogLevel.Debug => XLColor.FromHtml("#6B7280"),
                LogLevel.Info => XLColor.FromHtml("#4D6BFE"),
                LogLevel.Warning => XLColor.FromHtml("#F59E0B"),
                LogLevel.Error => XLColor.FromHtml("#EF4444"),
                _ => XLColor.Black
            };

            row++;
        }

        // Anchos de columna
        ws.Column(1).Width = 12;   // Fecha
        ws.Column(2).Width = 10;   // Hora
        ws.Column(3).Width = 8;    // Nivel
        ws.Column(4).Width = 18;   // Categoria
        ws.Column(5).Width = 90;   // Mensaje

        // Congelar la fila de encabezado.
        ws.SheetView.FreezeRows(1);

        // Autofiltro en el encabezado.
        ws.Range(1, 1, Math.Max(1, row - 1), 5).SetAutoFilter();

        // Guardar
        wb.SaveAs(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // PDF (QuestPDF)

    private static void ExportPdf(IReadOnlyList<LogEntry> entries, string path)
    {
        // NOTA: QuestPDF.Settings.License se configura UNA SOLA VEZ en
        // Program.Main al arrancar. No lo seteamos aca porque:
        //   1) QuestPDF.Settings.License no es thread-safe al setear.
        //   2) Si dos exportaciones corrian concurrentemente, podia dar
        //      race conditions.
        //   3) Es un setting global: con setearlo una vez alcanza.

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                // ===== CROSS-PLATFORM =====
                // Fonts.Lato viene empaquetada con QuestPDF y existe en Windows,
                // Linux y macOS. "Helvetica" no está en Linux y rompe el PDF.
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Lato));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Text("Log de RadioMaker")
                        .FontSize(16).Bold().FontColor("#0D0D0D");

                    col.Item().PaddingTop(2).Text(
                            $"Exportado el {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ·  " +
                            $"{entries.Count} entrada(s)")
                        .FontSize(9).FontColor("#6B7280");

                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor("#E5E5E5");
                });

                // Contenido: tabla de entradas
                page.Content().PaddingTop(10).Table(table =>
                {
                    // Definir columnas.
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);    // Fecha
                        columns.ConstantColumn(50);    // Hora
                        columns.ConstantColumn(45);    // Nivel
                        columns.ConstantColumn(70);    // Categoria
                        columns.RelativeColumn();      // Mensaje
                    });

                    // Encabezado de la tabla
                    table.Header(header =>
                    {
                        header.Cell().Background("#4D6BFE").Padding(4)
                            .Text("Fecha").FontColor(Colors.White).Bold().FontSize(9);
                        header.Cell().Background("#4D6BFE").Padding(4)
                            .Text("Hora").FontColor(Colors.White).Bold().FontSize(9);
                        header.Cell().Background("#4D6BFE").Padding(4)
                            .Text("Nivel").FontColor(Colors.White).Bold().FontSize(9);
                        header.Cell().Background("#4D6BFE").Padding(4)
                            .Text("Categoria").FontColor(Colors.White).Bold().FontSize(9);
                        header.Cell().Background("#4D6BFE").Padding(4)
                            .Text("Mensaje").FontColor(Colors.White).Bold().FontSize(9);
                    });

                    // Filas
                    foreach (var e in entries)
                    {
                        string levelColor = e.Level switch
                        {
                            LogLevel.Debug => "#6B7280",
                            LogLevel.Info => "#4D6BFE",
                            LogLevel.Warning => "#F59E0B",
                            LogLevel.Error => "#EF4444",
                            _ => "#0D0D0D"
                        };

                        table.Cell().Padding(3).Text(e.Timestamp.ToString("yyyy-MM-dd")).FontSize(8);
                        table.Cell().Padding(3).Text(e.Timestamp.ToString("HH:mm:ss")).FontSize(8);
                        table.Cell().Padding(3).Text(e.LevelText).FontColor(levelColor).Bold().FontSize(8);
                        table.Cell().Padding(3).Text(e.Category ?? string.Empty).FontSize(8);
                        table.Cell().Padding(3).Text(e.Message).FontSize(8);
                    }
                });

                // Footer con numero de pagina
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor("#6B7280"));
                    text.Span("Pagina ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        })
        .GeneratePdf(path);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers

    private static string PadRight(string value, int width)
    {
        if (value.Length >= width) return value;
        return value + new string(' ', width - value.Length);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 1) + "…";
    }

    private static string Inv(CultureInfo culture, IFormattable value)
        => value.ToString(null, culture);
}