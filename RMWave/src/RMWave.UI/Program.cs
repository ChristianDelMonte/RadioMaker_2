using Avalonia;
using ManagedBass;
using RMWave.Core.Audio;
using RMWave.Core.Models;
using RMWave.UI.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;
using System.Text;

namespace RMWave.UI;

class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
        catch { }

        // ===== INTEGRACIÓN CON RADIOMAKER =====
        // Precargar DLLs nativas de BASS antes de cualquier código que las use.
        // El loader sube directorios para encontrar "runtimes/{rid}/native/"
        // en la raíz de RadioMaker cuando corremos desde Addons/RMWave/.
        NativeLibraryLoader.EnsureLoaded();
        WaveLocalizationService.Initialize();

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
            Console.WriteLine(CliFormat("** Versión {0} - Última compilación: {1:yyyy-MM-dd}", "** Version {0} - Last build: {1:yyyy-MM-dd}", versionStr, buildDate));
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

    private static string Cli(string spanish, string english) =>
        WaveLocalizationService.CurrentCulture.TwoLetterISOLanguageName == "en" ? english : spanish;

    private static string CliFormat(string spanish, string english, params object?[] args) =>
        string.Format(WaveLocalizationService.CurrentCulture, Cli(spanish, english), args);

    private static string BoolLabel(bool value) =>
        value ? Cli("Sí", "Yes") : Cli("No", "No");

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
                        Console.WriteLine(Cli("Error: Debes especificar el archivo del proyecto.", "Error: You must specify the project file."));
                        Console.WriteLine(Cli("Uso: RMWave -reproducir <proyecto.rmw>", "Usage: RMWave play <project.rmw>"));
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
                                Console.WriteLine(Cli("Error: Debes especificar el nombre del dispositivo.", "Error: You must specify the device name."));
                                Console.WriteLine(Cli("Uso: RMWave -reproducir <proyecto.rmw> -dispositivo <nombre>", "Usage: RMWave play <project.rmw> -device <name>"));
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
                        Console.WriteLine(Cli("Error: Debes especificar el archivo de entrada y salida.", "Error: You must specify the input and output files."));
                        Console.WriteLine(Cli("Uso: RMWave -exportar <proyecto.rmw> <salida.wav|mp3>", "Usage: RMWave export <project.rmw> <output.wav|mp3>"));
                        return;
                    }

                    await ExportProjectAsync(args[1], args[2]);
                    break;
                }

            case "info":
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine(Cli("Error: Debes especificar el archivo del proyecto.", "Error: You must specify the project file."));
                        Console.WriteLine(Cli("Uso: RMWave -info <proyecto.rmw>", "Usage: RMWave info <project.rmw>"));
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
                        Console.WriteLine(Cli("Uso: RMWave -listarmarcadores <proyecto.rmw>", "Usage: RMWave listmarkers <project.rmw>"));
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
                        Console.WriteLine(Cli("Uso: RMWave -reproducirmarcador <proyecto.rmw> <nombre_marcador> [-dispositivo X] [-silent]", "Usage: RMWave playmarker <project.rmw> <marker_name> [-device X] [-silent]"));
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
                        Console.WriteLine(Cli("Uso: RMWave -reproducirrango <proyecto.rmw> <inicio> <fin> [-dispositivo X] [-silent]", "Usage: RMWave playrange <project.rmw> <start> <end> [-device X] [-silent]"));
                        return;
                    }
                    if (!ParseCliTime(args[2], out double rangeStart) || !ParseCliTime(args[3], out double rangeEnd))
                    {
                        Console.WriteLine(Cli("Error: Formato de tiempo inválido. Use segundos (90) o hh:mm:ss (0:01:30).", "Error: Invalid time format. Use seconds (90) or hh:mm:ss (0:01:30)."));
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
                        Console.WriteLine(Cli("Uso: RMWave -importarmarcadores <proyecto.rmw> <regiones.txt> [salida.rmw]", "Usage: RMWave importmarkers <project.rmw> <regions.txt> [output.rmw]"));
                        return;
                    }
                    string outputPath = args.Length >= 4 ? args[3] : args[1];
                    ImportMarkersCli(args[1], args[2], outputPath);
                    break;
                }

            default:
                Console.WriteLine(CliFormat("Comando desconocido: {0}", "Unknown command: {0}", args[0]));
                Console.WriteLine();
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RMWave - RadioMaker Wave Editor");
        Console.WriteLine();
        Console.WriteLine(Cli("Uso general:", "General usage:"));
        Console.WriteLine(Cli("  RMWave                                                    Abre la interfaz gráfica", "  RMWave                                                    Opens the graphical interface"));
        Console.WriteLine();
        Console.WriteLine(Cli("Reproducción:", "Playback:"));
        Console.WriteLine(Cli("  RMWave -reproducir <proyecto.rmw> [-dispositivo X] [-silent]", "  RMWave play <project.rmw> [-device X] [-silent]"));
        Console.WriteLine(Cli("  RMWave -reproducirmarcador <proyecto.rmw> <nombre> [-dispositivo X] [-silent]", "  RMWave playmarker <project.rmw> <name> [-device X] [-silent]"));
        Console.WriteLine(Cli("  RMWave -reproducirrango <proyecto.rmw> <inicio> <fin> [-dispositivo X] [-silent]", "  RMWave playrange <project.rmw> <start> <end> [-device X] [-silent]"));
        Console.WriteLine();
        Console.WriteLine(Cli("Marcadores:", "Markers:"));
        Console.WriteLine(Cli("  RMWave -listarmarcadores <proyecto.rmw>", "  RMWave listmarkers <project.rmw>"));
        Console.WriteLine(Cli("  RMWave -importarmarcadores <proyecto.rmw> <regiones.txt> [salida.rmw]", "  RMWave importmarkers <project.rmw> <regions.txt> [output.rmw]"));
        Console.WriteLine();
        Console.WriteLine(Cli("Exportación:", "Export:"));
        Console.WriteLine(Cli("  RMWave -exportar <proyecto.rmw> <salida.wav|mp3>", "  RMWave export <project.rmw> <output.wav|mp3>"));
        Console.WriteLine();
        Console.WriteLine(Cli("Información:", "Information:"));
        Console.WriteLine(Cli("  RMWave -info <proyecto.rmw>", "  RMWave info <project.rmw>"));
        Console.WriteLine();
        Console.WriteLine(Cli("Opciones:", "Options:"));
        Console.WriteLine(Cli("  -dispositivo <nombre> / -device <name>   Selecciona el dispositivo de salida de audio", "  -device <name>          Selects the audio output device (-dispositivo is also accepted)"));
        Console.WriteLine(Cli("  -silent                 No muestra información en la consola", "  -silent                 Hides console output"));
        Console.WriteLine();
        Console.WriteLine(Cli("Formatos de tiempo: segundos (90), mm:ss (1:30), hh:mm:ss (0:01:30)", "Time formats: seconds (90), mm:ss (1:30), hh:mm:ss (0:01:30)"));
        Console.WriteLine(Cli("Durante la reproducción: ESC cancela", "During playback: ESC cancels"));
    }

    // ==========================================
    // REPRODUCIR
    // ==========================================

    private static async Task PlayProjectAsync(string filePath, string? deviceName, bool silent)
    {
        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine(CliFormat("Error: El archivo no existe: {0}", "Error: File does not exist: {0}", filePath));
            return;
        }

        var project = LoadProjectFromFile(filePath);
        if (project == null) return;

        if (!silent)
        {
            Console.WriteLine(CliFormat("Reproduciendo: {0}", "Playing: {0}", System.IO.Path.GetFileName(filePath)));
            Console.WriteLine(CliFormat("Duración: {0}", "Duration: {0}", FormatDuration(project.Duration)));
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
            Console.WriteLine(CliFormat("Error: El archivo no existe: {0}", "Error: File does not exist: {0}", filePath));
            return;
        }

        try
        {
            var project = LoadProjectFromFile(filePath);
            if (project == null) return;

            Console.WriteLine(CliFormat("Proyecto cargado: {0} pista(s), duración: {1}", "Project loaded: {0} track(s), duration: {1}", project.Tracks.Count, FormatDuration(project.Duration)));
            Console.WriteLine(CliFormat("Exportando a: {0}", "Exporting to: {0}", outputPath));

            using var engine = new TimelinePlayer(ownsBass: true);

            if (!engine.Init())
            {
                Console.WriteLine(Cli("Error: No se pudo inicializar el motor de audio.", "Error: Could not initialize the audio engine."));
                return;
            }

            engine.LoadProject(project);

            var extension = System.IO.Path.GetExtension(outputPath).ToLower();

            if (extension == ".mp3")
            {
                await engine.RenderToMp3Async(
                    outputPath,
                    progress => Console.Write(CliFormat("\r  Exportando MP3: {0:F0}%", "\r  Exporting MP3: {0:F0}%", progress * 100)),
                    status => Console.WriteLine($"\n  {WaveLocalizationService.TranslateExportStatus(status)}"));
            }
            else
            {
                await engine.RenderToFileAsync(
                    outputPath,
                    progress => Console.Write(CliFormat("\r  Exportando WAV: {0:F0}%", "\r  Exporting WAV: {0:F0}%", progress * 100)),
                    status => Console.WriteLine($"\n  {WaveLocalizationService.TranslateExportStatus(status)}"));
            }

            Console.WriteLine();
            Console.WriteLine(Cli("Exportación completada.", "Export completed."));
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
            Console.WriteLine(CliFormat("Error: El archivo no existe: {0}", "Error: File does not exist: {0}", filePath));
            return;
        }

        try
        {
            var project = LoadProjectFromFile(filePath);
            if (project == null) return;

            Console.WriteLine(Cli("Información del proyecto:", "Project information:"));
            Console.WriteLine(CliFormat("  Pistas:       {0}", "  Tracks:       {0}", project.Tracks.Count));
            Console.WriteLine(CliFormat("  Duración:     {0}", "  Duration:     {0}", FormatDuration(project.Duration)));
            Console.WriteLine(CliFormat("  Frecuencia de muestreo: {0} Hz", "  Sample rate:              {0} Hz", project.SampleRate));
            Console.WriteLine();

            int totalClips = 0;

            foreach (var track in project.Tracks)
            {
                totalClips += track.Clips.Count;

                Console.WriteLine(CliFormat("  Pista: {0}", "  Track: {0}", track.Name));
                Console.WriteLine(CliFormat("    Volumen: {0:F2}  |  Pan: {1:F2}  |  Silencio: {2}  |  Solo: {3}", "    Volume: {0:F2}  |  Pan: {1:F2}  |  Mute: {2}  |  Solo: {3}", track.Volume, track.Pan, BoolLabel(track.Mute), BoolLabel(track.Solo)));
                Console.WriteLine(CliFormat("    Filas: {0}  |  Clips: {1}", "    Rows: {0}  |  Clips: {1}", track.RowCount, track.Clips.Count));

                foreach (var clip in track.Clips)
                {
                    Console.WriteLine($"      [{clip.Row}] {clip.Name}");
                    Console.WriteLine(CliFormat("          Inicio: {0:F2}s  |  Duración: {1:F2}s  |  Desplazamiento: {2:F2}s", "          Start: {0:F2}s  |  Duration: {1:F2}s  |  Offset: {2:F2}s", clip.StartTime, clip.Duration, clip.SourceOffset));
                    Console.WriteLine($"          Gain: {clip.Gain:F2}  |  Fade In: {clip.FadeIn:F2}s  |  Fade Out: {clip.FadeOut:F2}s");
                }

                Console.WriteLine();
            }

            Console.WriteLine(CliFormat("  Total de clips: {0}", "  Total clips: {0}", totalClips));
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
                Console.WriteLine(Cli("Error: No se pudo deserializar el proyecto.", "Error: Could not deserialize the project."));
                return null;
            }

            int missingFiles = 0;
            foreach (var track in project.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (!System.IO.File.Exists(clip.FilePath))
                    {
                        Console.WriteLine(CliFormat("  Advertencia: Archivo no encontrado: {0}", "  Warning: File not found: {0}", clip.FilePath));
                        missingFiles++;
                    }
                }
            }

            if (missingFiles > 0)
            {
                Console.WriteLine(CliFormat("  {0} archivo(s) de audio no encontrado(s).", "  {0} audio file(s) not found.", missingFiles));
            }

            return project;
        }
        catch (Exception ex)
        {
            Console.WriteLine(CliFormat("Error cargando proyecto: {0}", "Error loading project: {0}", ex.Message));
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
            Console.WriteLine(Cli("El proyecto no tiene marcadores.", "The project has no markers."));
            return;
        }

        Console.WriteLine(CliFormat("Marcadores del proyecto ({0}):", "Project markers ({0}):", project.Markers.Count));
        Console.WriteLine();

        var ordered = project.Markers.OrderBy(m => m.Time).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var m = ordered[i];
            double nextTime = (i + 1 < ordered.Count) ? ordered[i + 1].Time : project.Duration;
            double segDuration = Math.Max(0, nextTime - m.Time);

            Console.WriteLine(CliFormat("  {0,3}. {1,-20}  Inicio: {2}   Duración: {3}", "  {0,3}. {1,-20}  Start: {2}   Duration: {3}", i + 1, m.Name, FormatDuration(m.Time), FormatDuration(segDuration)));
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
            Console.WriteLine(CliFormat("Error: No se encontró el marcador '{0}'.", "Error: Marker '{0}' was not found.", markerName));
            if (project.Markers.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine(Cli("Marcadores disponibles:", "Available markers:"));
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
            Console.WriteLine(CliFormat("Reproduciendo marcador: '{0}'", "Playing marker: '{0}'", marker.Name));
            Console.WriteLine(CliFormat("Desde {0} hasta {1}", "From {0} to {1}", FormatDuration(startTime), FormatDuration(endTime)));
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
            Console.WriteLine(Cli("Error: El fin del rango debe ser mayor que el inicio.", "Error: The range end must be later than its start."));
            return;
        }

        if (!silent)
        {
            Console.WriteLine(CliFormat("Reproduciendo rango: {0} a {1}", "Playing range: {0} to {1}", FormatDuration(startTime), FormatDuration(endTime)));
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
            Console.WriteLine(Cli("Error: No se pudo inicializar el motor de audio.", "Error: Could not initialize the audio engine."));
            return;
        }

        if (deviceName != null)
        {
            int deviceId = FindDeviceByName(deviceName);
            if (deviceId >= 0)
                engine.SetDevice(deviceId);
            else if (!silent)
                Console.WriteLine(CliFormat("Advertencia: Dispositivo '{0}' no encontrado. Usando dispositivo por defecto.", "Warning: Device '{0}' not found. Using the default device.", deviceName));
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
            Console.WriteLine(Cli("Error: No se pudo iniciar la reproducción.", "Error: Could not start playback."));
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
                Console.Write(CliFormat("\rTranscurrido: {0}   ", "\rElapsed: {0}   ", FormatDuration(position)));

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    engine.Stop();
                    if (!silent) Console.WriteLine(Cli("\nReproducción cancelada.", "\nPlayback cancelled."));
                    return;
                }
            }

            await Task.Delay(50);
        }

        if (!silent)
            Console.WriteLine(Cli("\nReproducción finalizada.", "\nPlayback finished."));
    }

    // ==========================================
    // IMPORTAR MARCADORES (por lote)
    // ==========================================

    private static void ImportMarkersCli(string projectPath, string regionFilePath, string outputPath)
    {
        if (!System.IO.File.Exists(projectPath))
        {
            Console.WriteLine(CliFormat("Error: El proyecto no existe: {0}", "Error: Project does not exist: {0}", projectPath));
            return;
        }
        if (!System.IO.File.Exists(regionFilePath))
        {
            Console.WriteLine(CliFormat("Error: El archivo de regiones no existe: {0}", "Error: Region file does not exist: {0}", regionFilePath));
            return;
        }

        var project = LoadProjectFromFile(projectPath);
        if (project == null) return;

        var regions = ParseRegionFileCli(regionFilePath);
        if (regions.Count == 0)
        {
            Console.WriteLine(Cli("No se encontraron regiones válidas en el archivo.", "No valid regions were found in the file."));
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

        Console.WriteLine(CliFormat("{0} marcador(es) importados desde {1}.", "Imported {0} marker(s) from {1}.", added, System.IO.Path.GetFileName(regionFilePath)));
        Console.WriteLine(CliFormat("Proyecto guardado en: {0}", "Project saved to: {0}", outputPath));
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
