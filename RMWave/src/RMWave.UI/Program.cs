using Avalonia;
using ManagedBass;
using RMWave.Core.Audio;
using RMWave.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;

namespace RMWave.UI;

class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        // ===== INTEGRACIÓN CON RADIOMAKER =====
        // Precargar DLLs nativas de BASS antes de cualquier código que las use.
        // El loader sube directorios para encontrar "runtimes/{rid}/native/"
        // en la raíz de RadioMaker cuando corremos desde Addons/RMWave/.
        NativeLibraryLoader.EnsureLoaded();

        PrintBanner();

        // Filtrar flags de integración que agrega el host (ej: RadioMaker).
        var effectiveArgs = FilterInformativeFlags(args);

        // Detectar --open-file <path>: abrir ese archivo en la UI al arrancar.
        // También aceptamos un .rmw suelto como primer argumento (double-click
        // desde el explorador de Windows).
        StartupFilePath = ExtractStartupFilePath(effectiveArgs, out var remainingArgs);

        if (remainingArgs.Count > 0)
        {
            await RunCliAsync(remainingArgs.ToArray());
        }
        else
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    /// <summary>
    /// Ruta de un .rmw que debe abrirse en la UI al arrancar (o null).
    /// La consume MainWindow en su evento Opened.
    /// </summary>
    public static string? StartupFilePath { get; private set; }

    private static readonly HashSet<string> InformativeFlags =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--from-radiomaker",
            "--from-host",
        };

    private static List<string> FilterInformativeFlags(string[] args)
    {
        var result = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (InformativeFlags.Contains(arg))
                continue;
            result.Add(arg);
        }
        return result;
    }

    /// <summary>
    /// Extrae la ruta a abrir desde los argumentos. Acepta dos formas:
    ///   --open-file "X.rmw"     (forma explícita)
    ///   "X.rmw"                 (primer arg con extensión .rmw)
    /// Los args consumidos NO van al listado de "remainingArgs".
    /// </summary>
    private static string? ExtractStartupFilePath(
        List<string> args,
        out List<string> remainingArgs)
    {
        remainingArgs = new List<string>(args.Count);
        string? startupFile = null;

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, "--open-file", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    startupFile = args[i + 1];
                    i++; // saltar el path
                }
                continue;
            }

            // Primer arg suelto que apunte a un .rmw existente ? abrir en UI.
            if (startupFile is null &&
                arg.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(arg))
            {
                startupFile = arg;
                continue;
            }

            remainingArgs.Add(arg);
        }

        return startupFile;
    }

    // ==========================================
    // BANNER
    // ==========================================

    private static void PrintBanner()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var buildDate = System.IO.File.GetLastWriteTime(assembly.Location);

            string versionStr = version != null
                ? $"{version.Major}.{version.Minor:D2}.{version.Build:D2}"
                : "1.00.00";

            Console.WriteLine("***************************************************");
            Console.WriteLine("** RadioMaker Wave Editor.");
            Console.WriteLine($"** Version {versionStr} - Ultima compilacion: {buildDate:yyyy-MM-dd}");
            Console.WriteLine("---------------------------------------------------");
            Console.WriteLine();
        }
        catch
        {
            Console.WriteLine("***************************************************");
            Console.WriteLine("** RadioMaker Wave Editor.");
            Console.WriteLine("---------------------------------------------------");
            Console.WriteLine();
        }
    }

    // ==========================================
    // MODO CLI
    // ==========================================

    private static async Task RunCliAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        // El primer argumento es el comando (con o sin prefijo -).
        string command = args[0].ToLower().TrimStart('-');

        switch (command)
        {
            case "reproducir":
            case "play":
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Debes especificar el archivo del proyecto.");
                        Console.WriteLine("Uso: RMWave -reproducir <proyecto.rmw>");
                        return;
                    }

                    string filePath = args[1];
                    string? deviceName = null;
                    bool silent = false;

                    // Parsear flags opcionales.
                    for (int i = 2; i < args.Length; i++)
                    {
                        string flag = args[i].ToLower().TrimStart('-');

                        if (flag == "silent" || flag == "silencio")
                        {
                            silent = true;
                        }
                        else if (flag == "dispositivo" || flag == "device")
                        {
                            // El siguiente argumento es el nombre del dispositivo.
                            if (i + 1 < args.Length)
                            {
                                i++;
                                deviceName = args[i];
                            }
                            else
                            {
                                Console.WriteLine("Error: Debes especificar el nombre del dispositivo.");
                                Console.WriteLine("Uso: RMWave -reproducir <proyecto.rmw> -dispositivo <nombre>");
                                return;
                            }
                        }
                    }

                    await PlayProjectAsync(filePath, deviceName, silent);
                    break;
                }

            case "exportar":
            case "export":
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Error: Debes especificar el archivo de entrada y salida.");
                        Console.WriteLine("Uso: RMWave -exportar <proyecto.rmw> <salida.wav|mp3>");
                        return;
                    }

                    await ExportProjectAsync(args[1], args[2]);
                    break;
                }

            case "info":
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Debes especificar el archivo del proyecto.");
                        Console.WriteLine("Uso: RMWave -info <proyecto.rmw>");
                        return;
                    }

                    ShowProjectInfo(args[1]);
                    break;
                }

            case "listarmarcadores":
            case "listmarkers":
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Uso: RMWave -listarmarcadores <proyecto.rmw>");
                        return;
                    }
                    ListMarkers(args[1]);
                    break;
                }

            case "reproducirmarcador":
            case "playmarker":
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Uso: RMWave -reproducirmarcador <proyecto.rmw> <nombre_marcador> [-dispositivo X] [-silent]");
                        return;
                    }
                    var (devName, isSilent) = ParsePlaybackFlags(args, 3);
                    await PlayMarkerAsync(args[1], args[2], devName, isSilent);
                    break;
                }

            case "reproducirrango":
            case "playrange":
                {
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Uso: RMWave -reproducirrango <proyecto.rmw> <inicio> <fin> [-dispositivo X] [-silent]");
                        return;
                    }
                    if (!ParseCliTime(args[2], out double rangeStart) || !ParseCliTime(args[3], out double rangeEnd))
                    {
                        Console.WriteLine("Error: Formato de tiempo inválido. Use segundos (90) o hh:mm:ss (0:01:30).");
                        return;
                    }
                    var (devName, isSilent) = ParsePlaybackFlags(args, 4);
                    await PlayRangeAsync(args[1], rangeStart, rangeEnd, devName, isSilent);
                    break;
                }

            case "importarmarcadores":
            case "importmarkers":
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Uso: RMWave -importarmarcadores <proyecto.rmw> <regiones.txt> [salida.rmw]");
                        return;
                    }
                    string outputPath = args.Length >= 4 ? args[3] : args[1];
                    ImportMarkersCli(args[1], args[2], outputPath);
                    break;
                }

            default:
                Console.WriteLine($"Comando desconocido: {args[0]}");
                Console.WriteLine();
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RMWave - RadioMaker Wave Editor");
        Console.WriteLine();
        Console.WriteLine("Uso general:");
        Console.WriteLine("  RMWave                                                    Abre la interfaz gráfica");
        Console.WriteLine();
        Console.WriteLine("Reproducción:");
        Console.WriteLine("  RMWave -reproducir <proyecto.rmw> [-dispositivo X] [-silent]");
        Console.WriteLine("  RMWave -reproducirmarcador <proyecto.rmw> <nombre> [-dispositivo X] [-silent]");
        Console.WriteLine("  RMWave -reproducirrango <proyecto.rmw> <inicio> <fin> [-dispositivo X] [-silent]");
        Console.WriteLine();
        Console.WriteLine("Marcadores:");
        Console.WriteLine("  RMWave -listarmarcadores <proyecto.rmw>");
        Console.WriteLine("  RMWave -importarmarcadores <proyecto.rmw> <regiones.txt> [salida.rmw]");
        Console.WriteLine();
        Console.WriteLine("Exportación:");
        Console.WriteLine("  RMWave -exportar <proyecto.rmw> <salida.wav|mp3>");
        Console.WriteLine();
        Console.WriteLine("Información:");
        Console.WriteLine("  RMWave -info <proyecto.rmw>");
        Console.WriteLine();
        Console.WriteLine("Opciones:");
        Console.WriteLine("  -dispositivo <nombre>   Selecciona el dispositivo de salida de audio");
        Console.WriteLine("  -silent                 No muestra información en la consola");
        Console.WriteLine();
        Console.WriteLine("Formatos de tiempo: segundos (90), mm:ss (1:30), hh:mm:ss (0:01:30)");
        Console.WriteLine("Durante la reproducción: ESC cancela");
    }

    // ==========================================
    // REPRODUCIR
    // ==========================================

    private static async Task PlayProjectAsync(string filePath, string? deviceName, bool silent)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine($"Error: El archivo no existe: {filePath}");
            return;
        }

        var project = LoadProjectFromFile(filePath);
        if (project == null) return;

        if (!silent)
        {
            Console.WriteLine($"Reproduciendo: {System.IO.Path.GetFileName(filePath)}");
            Console.WriteLine($"Duración: {FormatDuration(project.Duration)}");
        }

        await PlaySegmentAsync(project, 0, project.Duration, deviceName, silent);
    }

    // ==========================================
    // EXPORTAR
    // ==========================================

    private static async Task ExportProjectAsync(string filePath, string outputPath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine($"Error: El archivo no existe: {filePath}");
            return;
        }

        try
        {
            var project = LoadProjectFromFile(filePath);
            if (project == null) return;

            Console.WriteLine($"Proyecto cargado: {project.Tracks.Count} pista(s), duracion: {FormatDuration(project.Duration)}");
            Console.WriteLine($"Exportando a: {outputPath}");

            using var engine = new TimelinePlayer(ownsBass: true);

            if (!engine.Init())
            {
                Console.WriteLine("Error: No se pudo inicializar el motor de audio.");
                return;
            }

            engine.LoadProject(project);

            var extension = System.IO.Path.GetExtension(outputPath).ToLower();

            if (extension == ".mp3")
            {
                await engine.RenderToMp3Async(
                    outputPath,
                    progress => Console.Write($"\r  Exportando MP3: {progress * 100:F0}%"),
                    status => Console.WriteLine($"\n  {status}"));
            }
            else
            {
                await engine.RenderToFileAsync(
                    outputPath,
                    progress => Console.Write($"\r  Exportando WAV: {progress * 100:F0}%"),
                    status => Console.WriteLine($"\n  {status}"));
            }

            Console.WriteLine();
            Console.WriteLine("Exportacion completada.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // ==========================================
    // INFO
    // ==========================================

    private static void ShowProjectInfo(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine($"Error: El archivo no existe: {filePath}");
            return;
        }

        try
        {
            var project = LoadProjectFromFile(filePath);
            if (project == null) return;

            Console.WriteLine("Informacion del proyecto:");
            Console.WriteLine($"  Pistas:       {project.Tracks.Count}");
            Console.WriteLine($"  Duracion:     {FormatDuration(project.Duration)}");
            Console.WriteLine($"  Sample Rate:  {project.SampleRate} Hz");
            Console.WriteLine();

            int totalClips = 0;

            foreach (var track in project.Tracks)
            {
                totalClips += track.Clips.Count;

                Console.WriteLine($"  Pista: {track.Name}");
                Console.WriteLine($"    Volumen: {track.Volume:F2}  |  Pan: {track.Pan:F2}  |  Mute: {track.Mute}  |  Solo: {track.Solo}");
                Console.WriteLine($"    Filas: {track.RowCount}  |  Clips: {track.Clips.Count}");

                foreach (var clip in track.Clips)
                {
                    Console.WriteLine($"      [{clip.Row}] {clip.Name}");
                    Console.WriteLine($"          Inicio: {clip.StartTime:F2}s  |  Duracion: {clip.Duration:F2}s  |  Offset: {clip.SourceOffset:F2}s");
                    Console.WriteLine($"          Gain: {clip.Gain:F2}  |  Fade In: {clip.FadeIn:F2}s  |  Fade Out: {clip.FadeOut:F2}s");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"  Total de clips: {totalClips}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // ==========================================
    // CARGA DE PROYECTO
    // ==========================================

    private static Project? LoadProjectFromFile(string filePath)
    {
        try
        {
            var json = System.IO.File.ReadAllText(filePath);
            var project = System.Text.Json.JsonSerializer.Deserialize<Project>(json);

            if (project == null)
            {
                Console.WriteLine("Error: No se pudo deserializar el proyecto.");
                return null;
            }

            int missingFiles = 0;
            foreach (var track in project.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (!System.IO.File.Exists(clip.FilePath))
                    {
                        Console.WriteLine($"  Advertencia: Archivo no encontrado: {clip.FilePath}");
                        missingFiles++;
                    }
                }
            }

            if (missingFiles > 0)
            {
                Console.WriteLine($"  {missingFiles} archivo(s) de audio no encontrado(s).");
            }

            return project;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error cargando proyecto: {ex.Message}");
            return null;
        }
    }

    // ==========================================
    // LISTAR MARCADORES
    // ==========================================

    private static void ListMarkers(string filePath)
    {
        var project = LoadProjectFromFile(filePath);
        if (project == null) return;

        if (project.Markers.Count == 0)
        {
            Console.WriteLine("El proyecto no tiene marcadores.");
            return;
        }

        Console.WriteLine($"Marcadores del proyecto ({project.Markers.Count}):");
        Console.WriteLine();

        var ordered = project.Markers.OrderBy(m => m.Time).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            double nextTime = (i + 1 < ordered.Count) ? ordered[i + 1].Time : project.Duration;
            double segDuration = Math.Max(0, nextTime - m.Time);

            Console.WriteLine($"  {i + 1,3}. {m.Name,-20}  Inicio: {FormatDuration(m.Time)}   Duración: {FormatDuration(segDuration)}");
        }
    }

    // ==========================================
    // REPRODUCIR MARCADOR
    // ==========================================

    private static async Task PlayMarkerAsync(string filePath, string markerName, string? deviceName, bool silent)
    {
        var project = LoadProjectFromFile(filePath);
        if (project == null) return;

        // Buscar el marcador por nombre (sin distinguir mayúsculas/minúsculas).
        var marker = project.Markers.FirstOrDefault(m =>
            string.Equals(m.Name.Trim(), markerName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (marker == null)
        {
            Console.WriteLine($"Error: No se encontró el marcador '{markerName}'.");
            if (project.Markers.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Marcadores disponibles:");
                foreach (var m in project.Markers.OrderBy(x => x.Time))
                    Console.WriteLine($"  {m.Name}  ({FormatDuration(m.Time)})");
            }
            return;
        }

        // El segmento va desde este marcador hasta el siguiente (o hasta el fin).
        var nextMarker = project.Markers
            .Where(m => m.Time > marker.Time)
            .OrderBy(m => m.Time)
            .FirstOrDefault();

        double startTime = marker.Time;
        double endTime = nextMarker != null ? nextMarker.Time : project.Duration;

        if (!silent)
        {
            Console.WriteLine($"Reproduciendo marcador: '{marker.Name}'");
            Console.WriteLine($"Desde {FormatDuration(startTime)} hasta {FormatDuration(endTime)}");
        }

        await PlaySegmentAsync(project, startTime, endTime, deviceName, silent);
    }

    // ==========================================
    // REPRODUCIR RANGO
    // ==========================================

    private static async Task PlayRangeAsync(string filePath, double startTime, double endTime, string? deviceName, bool silent)
    {
        var project = LoadProjectFromFile(filePath);
        if (project == null) return;

        if (endTime <= startTime)
        {
            Console.WriteLine("Error: El fin del rango debe ser mayor que el inicio.");
            return;
        }

        if (!silent)
        {
            Console.WriteLine($"Reproduciendo rango: {FormatDuration(startTime)} a {FormatDuration(endTime)}");
        }

        await PlaySegmentAsync(project, startTime, endTime, deviceName, silent);
    }

    // ==========================================
    // REPRODUCIR SEGMENTO (helper compartido)
    // ==========================================

    private static async Task PlaySegmentAsync(Project project, double startTime, double endTime, string? deviceName, bool silent)
    {
        using var engine = new TimelinePlayer(ownsBass: true);

        if (!engine.Init())
        {
            Console.WriteLine("Error: No se pudo inicializar el motor de audio.");
            return;
        }

        if (deviceName != null)
        {
            int deviceId = FindDeviceByName(deviceName);
            if (deviceId >= 0)
                engine.SetDevice(deviceId);
            else if (!silent)
                Console.WriteLine($"Advertencia: Dispositivo '{deviceName}' no encontrado. Usando dispositivo por defecto.");
        }

        engine.LoadProject(project);

        // Limitar el fin al rango del proyecto.
        if (endTime <= 0 || endTime > project.Duration)
            endTime = project.Duration;

        // Posicionar en el inicio del segmento.
        if (startTime > 0.05)
            engine.Seek(startTime);

        if (!engine.Play())
        {
            Console.WriteLine("Error: No se pudo iniciar la reproducción.");
            return;
        }

        while (engine.IsPlaying)
        {
            var position = engine.GetPositionSeconds();

            // Fin del segmento.
            if (position >= endTime - 0.05)
            {
                engine.Stop();
                break;
            }

            if (!silent)
                Console.Write($"\rTranscurrido: {FormatDuration(position)}   ");

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    engine.Stop();
                    if (!silent) Console.WriteLine("\nReproducción cancelada.");
                    return;
                }
            }

            await Task.Delay(50);
        }

        if (!silent)
            Console.WriteLine("\nReproducción finalizada.");
    }

    // ==========================================
    // IMPORTAR MARCADORES (por lote)
    // ==========================================

    private static void ImportMarkersCli(string projectPath, string regionFilePath, string outputPath)
    {
        if (!System.IO.File.Exists(projectPath))
        {
            Console.WriteLine($"Error: El proyecto no existe: {projectPath}");
            return;
        }
        if (!System.IO.File.Exists(regionFilePath))
        {
            Console.WriteLine($"Error: El archivo de regiones no existe: {regionFilePath}");
            return;
        }

        var project = LoadProjectFromFile(projectPath);
        if (project == null) return;

        var regions = ParseRegionFileCli(regionFilePath);
        if (regions.Count == 0)
        {
            Console.WriteLine("No se encontraron regiones válidas en el archivo.");
            return;
        }

        int added = 0;
        foreach (var region in regions)
        {
            var name = string.IsNullOrWhiteSpace(region.Id)
                ? $"Reg {added:00}"
                : region.Id;

            project.Markers.Add(new TimelineMarker
            {
                Name = name,
                Time = region.Start
            });
            added++;
        }

        // Guardar el proyecto.
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var json = System.Text.Json.JsonSerializer.Serialize(project, options);
        System.IO.File.WriteAllText(outputPath, json);

        Console.WriteLine($"{added} marcador(es) importados desde {System.IO.Path.GetFileName(regionFilePath)}.");
        Console.WriteLine($"Proyecto guardado en: {outputPath}");
    }

    // ==========================================
    // PARSEO DE TIEMPOS Y REGIONES
    // ==========================================

    /// <summary>
    /// Interpreta tiempos como "90", "1:30", "0:01:30" o "1:30.5".
    /// </summary>
    private static bool ParseCliTime(string text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimEnd('s');

        // Número simple (segundos).
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
        {
            seconds = Math.Max(0, plain);
            return true;
        }

        // Formato con dos puntos: mm:ss o hh:mm:ss.
        var parts = text.Split(':');
        double result = 0;
        foreach (var part in parts)
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                result = result * 60 + val;
            else
                return false;
        }

        seconds = Math.Max(0, result);
        return true;
    }

    /// <summary>
    /// Interpreta un archivo de tabla de regiones (formato start_region_table).
    /// </summary>
    private static List<(double Start, double End, string Id)> ParseRegionFileCli(string path)
    {
        var regions = new List<(double Start, double End, string Id)>();
        var lines = System.IO.File.ReadAllLines(path);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("start_region_table", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("end_region_table", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("#") || line.StartsWith("//")) continue;

            var parts = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (!ParseCliTime(parts[0], out double start)) continue;
            if (!ParseCliTime(parts[1], out double end)) continue;

            string id = parts.Length >= 3 ? parts[2] : "";
            regions.Add((start, end, id));
        }

        return regions;
    }

    // ==========================================
    // UTILIDADES
    // ==========================================

    private static int FindDeviceByName(string deviceName)
    {
        int count = Bass.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            if (Bass.GetDeviceInfo(i, out var info))
            {
                if (info.IsEnabled && !info.IsLoopback)
                {
                    if (info.Name != null &&
                        info.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
        }
        return -1;
    }

    private static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.ToString(@"hh\:mm\:ss");
    }

    private static (string? DeviceName, bool Silent) ParsePlaybackFlags(string[] args, int startIndex)
    {
        string? deviceName = null;
        bool silent = false;

        for (int i = startIndex; i < args.Length; i++)
        {
            string flag = args[i].ToLower().TrimStart('-');

            if (flag == "silent" || flag == "silencio")
            {
                silent = true;
            }
            else if (flag == "dispositivo" || flag == "device")
            {
                if (i + 1 < args.Length)
                {
                    i++;
                    deviceName = args[i];
                }
            }
        }

        return (deviceName, silent);
    }

    // ==========================================
    // INTERFAZ GRAFICA
    // ==========================================

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}