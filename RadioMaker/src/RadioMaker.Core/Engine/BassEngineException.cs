using ManagedBass;

// (namespace renombrado — ver nota en BassEngine.cs)
namespace RadioMaker.Core.Engine;

/// -------------
/// Excepción que encapsula un error devuelto por la librería nativa BASS.
/// -------------
public sealed class BassEngineException : Exception
{
    public Errors ErrorCode { get; }

    public BassEngineException(string message, Errors errorCode)
        : base($"{message} (BASS error: {errorCode})")
    {
        ErrorCode = errorCode;
    }

    public BassEngineException(string message, Errors errorCode, Exception innerException)
        : base($"{message} (BASS error: {errorCode})", innerException)
    {
        ErrorCode = errorCode;
    }
}
