using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RadioMaker.UI.Services;

/// -------------
/// Expone la info de version de la aplicacion, leida del ensamblado en
/// tiempo de ejecucion. Los valores se generan automaticamente en cada
/// compilacion desde el .csproj (ver RadioMaker.UI.csproj):
///
///     Version = 2.01.YYMMDD.HHmm
///       - 2.01      = mayor.menor
///       - YYMMDD    = fecha UTC del build
///       - HHmm      = hora UTC del build
///
/// Ejemplo: 2.01.261011.1530 = v2.01 compilada el 11/10/2026 a las 15:30 UTC.
/// -------------
public static class AppVersionInfo
{
    private static readonly Lazy<VersionData> _data = new(LoadVersionData);

    // ───── API publica ───────────────────────────────────────────────────

    /// -------------Version completa como string: "2.01.261011.1530".</summary>
    public static string Version => _data.Value.VersionString;

    /// -------------Version corta (mayor.menor): "2.01".</summary>
    public static string ShortVersion => _data.Value.ShortVersion;

    /// -------------Texto listo para mostrar en el titulo: "Version 2.01.261011.1530".</summary>
    public static string VersionText => $"Version {Version}";

    /// -------------Fecha de compilacion en formato local (dd-MM-yyyy HH:mm).</summary>
    public static string BuildDateTimeText => _data.Value.BuildDateText;

    /// -------------Texto de compilacion para la barra de titulo: "Compilacion 11-10-2026 15:30".</summary>
    public static string BuildText => $"Compilacion {BuildDateTimeText}";

    /// -------------Texto de plataforma: ".NET 8 · Windows 10.0.19045".</summary>
    public static string PlatformText
    {
        get
        {
            var netVersion = Environment.Version;
            var os = RuntimeInformation.OSDescription?.Trim() ?? "Desconocido";
            return $".NET {netVersion.Major} · {os}";
        }
    }

    /// -------------
    /// Fecha de compilacion como DateTime (UTC). Util para debug.
    /// -------------
    public static DateTime BuildDateTimeUtc => _data.Value.BuildDateTimeUtc;

    // ───── Internals ─────────────────────────────────────────────────────

    private sealed class VersionData
    {
        public string VersionString { get; init; } = "0.0.0.0";
        public string ShortVersion { get; init; } = "0.0";
        public string BuildDateText { get; init; } = "desconocida";
        public DateTime BuildDateTimeUtc { get; init; } = DateTime.MinValue;
    }

    private static VersionData LoadVersionData()
    {
        // ── 1) Obtener el FileVersion completo: "2.01.261011.1530" ────────
        string fileVersion = string.Empty;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var location = assembly.Location;

            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                var fvi = FileVersionInfo.GetVersionInfo(location);

                // ===== FIX CS7035 =====
                // FileVersion ahora tiene formato numerico valido (sin ceros
                // iniciales: "2.1.261107.36"). Para mostrar el formato "bonito"
                // en la UI ("2.01.261107.0036"), leemos ProductVersion, que
                // refleja el AssemblyInformationalVersion.
                fileVersion = !string.IsNullOrEmpty(fvi.ProductVersion)
                    ? fvi.ProductVersion
                    : fvi.FileVersion ?? string.Empty;
            }
        }
        catch { }

        // Fallback: si por algun motivo no se pudo leer el archivo (por
        // ejemplo en single-file publish o en algunos entornos), usamos
        // el AssemblyVersion que es menos preciso pero funciona.
        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            try
            {
                var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVer is not null)
                    fileVersion = $"{asmVer.Major}.{asmVer.Minor:D2}.0.0";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            return new VersionData
            {
                VersionString = "0.0.0.0",
                ShortVersion = "0.0",
                BuildDateText = "desconocida",
                BuildDateTimeUtc = DateTime.MinValue
            };
        }

        // ── 2) Parsear el string "M.m.YYMMDD.HHmm" ───────────────────────
        var parts = fileVersion.Split('.');
        string shortVersion = parts.Length >= 2
            ? $"{parts[0]}.{parts[1]}"
            : fileVersion;

        string buildDateText = "desconocida";
        DateTime buildDateUtc = DateTime.MinValue;

        if (parts.Length >= 4)
        {
            string yyMMdd = parts[2];
            string hhmm = parts[3];

            if (TryParseBuildDate(yyMMdd, hhmm, out var dt))
            {
                buildDateUtc = dt;
                // Convertimos a hora LOCAL para mostrar al usuario.
                var localDt = dt.ToLocalTime();
                buildDateText = localDt.ToString("dd-MM-yyyy HH:mm");
            }
        }

        return new VersionData
        {
            VersionString = fileVersion,
            ShortVersion = shortVersion,
            BuildDateText = buildDateText,
            BuildDateTimeUtc = buildDateUtc
        };
    }

    /// -------------
    /// Parsea YYMMDD + HHmm (ambos en UTC) a DateTime.
    /// -------------
    private static bool TryParseBuildDate(string yyMMdd, string hhmm, out DateTime result)
    {
        result = DateTime.MinValue;

        if (yyMMdd.Length != 6) return false;
        if (hhmm.Length != 4) return false;

        if (!int.TryParse(yyMMdd.Substring(0, 2), out int yy)) return false;
        if (!int.TryParse(yyMMdd.Substring(2, 2), out int mm)) return false;
        if (!int.TryParse(yyMMdd.Substring(4, 2), out int dd)) return false;
        if (!int.TryParse(hhmm.Substring(0, 2), out int hh)) return false;
        if (!int.TryParse(hhmm.Substring(2, 2), out int mn)) return false;

        try
        {
            // YY asume 20xx. Con esto alcanza para esta app.
            int year = 2000 + yy;
            result = new DateTime(year, mm, dd, hh, mn, 0, DateTimeKind.Utc);
            return true;
        }
        catch
        {
            return false;
        }
    }
}