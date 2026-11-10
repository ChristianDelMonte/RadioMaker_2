using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RadioMaker.Core.Engine;

/// <summary>
/// Se asegura de que las DLLs nativas de BASS estén accesibles antes de que
/// ManagedBass intente cargarlas.
///
/// Estrategia:
///   1. Localiza runtimes/{rid}/native/ junto al ejecutable.
///   2. Añade el directorio al PATH del proceso.
///   3. Registra un DllImportResolver en TODOS los assemblies de ManagedBass
///      (core + addons), para que las búsquedas de "bass", "bass_fx",
///      "bassmix", "bassflac", etc. resuelvan a la ruta correcta dentro de
///      runtimes/{rid}/native/.
///
/// El paso 3 es CRÍTICO: sin él, .NET busca las DLLs solo en el directorio
/// del ejecutable y en el PATH del sistema, y las DLLs agregadas manualmente
/// al proyecto (no desde NuGet) NO se resuelven automáticamente desde
/// runtimes/{rid}/native/.
///
/// Se ejecuta UNA vez por proceso. Idempotente.
/// </summary>
public static class NativeLibraryLoader
{
    private static readonly object Gate = new();
    private static bool _done;
    private static string? _lastDir;

    /// <summary>
    /// Nombres de los assemblies de ManagedBass que hacen P/Invoke a las
    /// librerías nativas. Se registra el DllImportResolver en cada uno.
    /// </summary>
    private static readonly string[] ManagedBassAssemblies = new[]
    {
        "ManagedBass",
        "ManagedBass.Fx",
        "ManagedBass.Mix",
        "ManagedBass.Enc",
        "ManagedBass.Flac",
        "ManagedBass.Aac",
        "ManagedBass.Ac3",
        "ManagedBass.Aix",
        "ManagedBass.Alac",
        "ManagedBass.Ape",
        "ManagedBass.Dsd",
        "ManagedBass.Hls",
        "ManagedBass.Mpc",
        "ManagedBass.Ofr",
        "ManagedBass.Opus",
        "ManagedBass.Spx",
        "ManagedBass.WA",
        "ManagedBass.Wma",
        "ManagedBass.Wv",
    };

    public static string? EnsureLoaded()
    {
        lock (Gate)
        {
            if (_done) return _lastDir;
            _done = true;

            try
            {
                var baseDir = AppContext.BaseDirectory;
                var rid = RuntimeInformation.RuntimeIdentifier;
                var nativeDir = Path.Combine(baseDir, "runtimes", rid, "native");

                if (!Directory.Exists(nativeDir))
                {
                    var runtimesRoot = Path.Combine(baseDir, "runtimes");
                    if (Directory.Exists(runtimesRoot))
                    {
                        foreach (var candidate in Directory.EnumerateDirectories(runtimesRoot))
                        {
                            var native = Path.Combine(candidate, "native");
                            if (Directory.Exists(native) && HasBassLibrary(native))
                            {
                                nativeDir = native;
                                break;
                            }
                        }
                    }
                }

                if (!Directory.Exists(nativeDir) || !HasBassLibrary(nativeDir))
                    return null;

                PrependToPath(nativeDir);
                RegisterResolvers();
                _lastDir = nativeDir;
                return nativeDir;
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool HasBassLibrary(string dir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            File.Exists(Path.Combine(dir, "bass.dll")))
            return true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            File.Exists(Path.Combine(dir, "libbass.so")))
            return true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            File.Exists(Path.Combine(dir, "libbass.dylib")))
            return true;

        return Directory.EnumerateFiles(dir, "bass*").Any();
    }

    private static void PrependToPath(string dir)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!current.Split(Path.PathSeparator).Contains(dir, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + current);
    }

    private static void RegisterResolvers()
    {
        RegisterResolverForAssembly(typeof(NativeLibraryLoader).Assembly);

        foreach (var name in ManagedBassAssemblies)
        {
            try
            {
                var asm = Assembly.Load(name);
                if (asm is not null)
                    RegisterResolverForAssembly(asm);
            }
            catch { }
        }
    }

    private static void RegisterResolverForAssembly(Assembly asm)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(asm, Resolve);
        }
        catch (InvalidOperationException) { }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.StartsWith("bass", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        var baseDir = AppContext.BaseDirectory;
        var rid = RuntimeInformation.RuntimeIdentifier;

        string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { libraryName + ".dll", libraryName }
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new[] { "lib" + libraryName + ".dylib", libraryName }
                : new[] { "lib" + libraryName + ".so", libraryName };

        foreach (var ridFolder in new[] { rid, "win-x64", "win-x86", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(baseDir, "runtimes", ridFolder, "native", candidate);
                if (File.Exists(full) && NativeLibrary.TryLoad(full, out var handle))
                    return handle;
            }
        }

        foreach (var candidate in candidates)
        {
            var full = Path.Combine(baseDir, candidate);
            if (File.Exists(full) && NativeLibrary.TryLoad(full, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }
}