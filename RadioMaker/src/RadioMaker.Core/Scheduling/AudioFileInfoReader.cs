using ManagedBass;

namespace RadioMaker.Core.Scheduling;

/// -------------
/// Lee informacion basica de un archivo de audio sin mantener el stream abierto.
///
/// Uso principal: obtener la duracion en segundos al agregar una pista a una
/// playlist o tanda, para poder mostrarla y validar el offset del mixado.
///
/// Requiere que BASS este inicializado (cualquier dispositivo alcanza — el
/// stream de decodificacion no toca hardware).
/// -------------
public static class AudioFileInfoReader
{
    /// -------------
    /// Devuelve la duracion del archivo en segundos. Cero si no se puede leer.
    /// -------------
    public static double GetDurationSeconds(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return 0;
        if (!File.Exists(filePath)) return 0;

        // ===== INTEGRACIÓN RMWave =====
        if (filePath.EndsWith(".rmw", StringComparison.OrdinalIgnoreCase))
        {
            try { return Engine.RmwLoader.GetDuration(filePath); }
            catch { return 0; }
        }

        int handle = 0;
        try
        {
            handle = Bass.CreateStream(filePath, 0L, 0L, BassFlags.Decode);
            if (handle == 0) return 0;

            long lenBytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
            if (lenBytes <= 0) return 0;

            double seconds = Bass.ChannelBytes2Seconds(handle, lenBytes);
            return seconds > 0 ? seconds : 0;
        }
        catch { return 0; }
        finally
        {
            if (handle != 0) { try { Bass.StreamFree(handle); } catch { } }
        }
    }

    /// -------------Formatea una duracion en segundos a "hh:mm:ss".</summary>
    public static string FormatDuration(double totalSeconds)
    {
        if (totalSeconds <= 0) return "00:00:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}