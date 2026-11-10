using System.Globalization;
using System.Text;
using RMVoice.Core.Models;

namespace RMVoice.Core.Services;

/// <summary>
/// Lee y escribe los .txt de regiones con el formato:
///
///   start_region_table
///   00:00:00.0 00:00:01.5 00
///   00:00:01.5 00:00:02.9 01
///   ...
///   end_region_table
///
/// El índice puede ser negativo (usado por la tabla de temperaturas bajo cero).
/// </summary>
public static class RegionTableParser
{
    private const string StartMarker = "start_region_table";
    private const string EndMarker = "end_region_table";

    public static List<AudioRegion> Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("No se encontró la tabla de regiones.", filePath);

        return ParseLines(File.ReadAllLines(filePath));
    }

    public static List<AudioRegion> ParseLines(IEnumerable<string> lines)
    {
        var result = new List<AudioRegion>();
        var inside = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//"))
                continue;

            if (line.Equals(StartMarker, StringComparison.OrdinalIgnoreCase))
            {
                inside = true;
                continue;
            }

            if (line.Equals(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                inside = false;
                continue;
            }

            if (!inside)
                continue;

            var parts = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;

            if (!TimeFormat.TryParse(parts[0], out var start))
                continue;

            if (!TimeFormat.TryParse(parts[1], out var end))
                continue;

            if (!int.TryParse(parts[2], NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                              CultureInfo.InvariantCulture, out var index))
                continue;

            result.Add(new AudioRegion
            {
                Index = index,
                StartSeconds = start,
                EndSeconds = end
            });
        }

        return result.OrderBy(r => r.Index).ToList();
    }

    public static void Save(string filePath, IEnumerable<AudioRegion> regions)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var sb = new StringBuilder();
        sb.AppendLine(StartMarker);

        foreach (var region in regions.OrderBy(r => r.Index))
        {
            sb.Append(TimeFormat.ToTimeStringPrecise(region.StartSeconds));
            sb.Append(' ');
            sb.Append(TimeFormat.ToTimeStringPrecise(region.EndSeconds));
            sb.Append(' ');
            sb.AppendLine(region.Index.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0'));
        }

        sb.AppendLine(EndMarker);

        File.WriteAllText(filePath, sb.ToString());
    }
}
