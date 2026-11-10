using CommunityToolkit.Mvvm.ComponentModel;

namespace RMVoice.Core.Models;

/// <summary>
/// Conjunto de archivos que definen una "voz" del reloj:
/// 4 audios (hora, minuto, temperatura, humedad) y sus 4 tablas de regiones.
/// </summary>
public sealed class VoiceProfile : ObservableObject
{
    private string _name = "Nuevo perfil";
    private string _language = "es";
    private string _gender = "—";

    private string _hourAudioPath = "";
    private string _minuteAudioPath = "";
    private string _temperatureAudioPath = "";
    private string _humidityAudioPath = "";

    private string _hourTablePath = "";
    private string _minuteTablePath = "";
    private string _temperatureTablePath = "";
    private string _humidityTablePath = "";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Código de idioma: "es", "en", "fr", ...</summary>
    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>"M", "F", "—"</summary>
    public string Gender
    {
        get => _gender;
        set => SetProperty(ref _gender, value);
    }

    // -------------------------- Rutas de audio --------------------------

    public string HourAudioPath
    {
        get => _hourAudioPath;
        set => SetProperty(ref _hourAudioPath, value);
    }

    public string MinuteAudioPath
    {
        get => _minuteAudioPath;
        set => SetProperty(ref _minuteAudioPath, value);
    }

    public string TemperatureAudioPath
    {
        get => _temperatureAudioPath;
        set => SetProperty(ref _temperatureAudioPath, value);
    }

    public string HumidityAudioPath
    {
        get => _humidityAudioPath;
        set => SetProperty(ref _humidityAudioPath, value);
    }

    // -------------------------- Tablas de regiones ----------------------

    public string HourTablePath
    {
        get => _hourTablePath;
        set => SetProperty(ref _hourTablePath, value);
    }

    public string MinuteTablePath
    {
        get => _minuteTablePath;
        set => SetProperty(ref _minuteTablePath, value);
    }

    public string TemperatureTablePath
    {
        get => _temperatureTablePath;
        set => SetProperty(ref _temperatureTablePath, value);
    }

    public string HumidityTablePath
    {
        get => _humidityTablePath;
        set => SetProperty(ref _humidityTablePath, value);
    }

    public override string ToString() => $"{Name} ({Language}/{Gender})";
}
