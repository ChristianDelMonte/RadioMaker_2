# 🎙️ RadioMaker

> **Forever FREE Radio Automation Software!**

Suite completa de automatización de radio multiplataforma, compuesta por un
**motor central** y dos **addons opcionales** para locuciones y edición
multipista. Escrita en **C# / .NET 8** con **Avalonia UI** para una experiencia
de escritorio nativa en Windows, Linux y macOS.

<!-- Badges opcionales (descomentar cuando exista CI/CD) -->

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![UI](https://img.shields.io/badge/UI-Avalonia%2012.1.5-8B44AC)
![Audio](https://img.shields.io/badge/audio-BASS%202.4-FF6B00)

---

## 📑 Índice

- [Características](#-características)
  - [RadioMaker (motor principal)](#radiomaker-motor-principal)
  - [RMVoice (addon de locuciones)](#rmvoice-addon-de-locuciones)
  - [RMWave (addon de edición multipista)](#rmwave-addon-de-edición-multipista)
- [Arquitectura del proyecto](#-arquitectura-del-proyecto)
- [Requisitos del sistema](#-requisitos-del-sistema)
- [Compilación](#-compilación)
- [Uso](#-uso)
- [Librerías y licencias](#-librerías-y-licencias)
- [Roadmap](#-roadmap)
- [Créditos](#-créditos)
- [Licencia](#-licencia)

---

## ✨ Características

### RadioMaker (motor principal)

Automatizador de radio profesional con **múltiples módulos** integrados en una
interfaz de pestañas multi-zona (izquierda / centro / derecha) con workspace
persistente.

#### 🎵 Reproducción y mezcla

- **Reproductor multipista con buses independientes**: cada módulo (Programados,
  Playlists, Botonera, Explorador) puede salir por un bus de audio distinto
  (útil para preescucha en auriculares mientras el aire sigue sonando).
- **Coordinador de exclusividad de buses**: arbitra qué módulo suena en cada
  bus con políticas configurables por rol (Cart, Playlist, Scheduler,
  Emergency, Manual, FileManager, FavoriteFolder), prioridades (Low / Normal /
  High / Air) y comportamientos de interrupción (Stop / Pause / AutoResume).
- **Crossfade entre pistas** configurable (0 a 5 segundos).
- **Auto-fade** (fade-in al inicio + fade-out antes del final) configurable.
- **Ducking automático por micrófono**: baja el volumen del programa cuando el
  locutor habla, con umbral, reducción, hold, attack y release ajustables.
- **Control de volumen global** en cascada (usuario × bus × normalización ×
  ducking) con límite de seguridad para evitar saturaciones.

#### 📅 Programación (Scheduler)

- **Programación semanal por días y horarios** con múltiples entradas por ítem.
- **4 tipos de contenido programable**:
  - Archivo suelto (`.mp3`, `.flac`, `.wav`, `.rmw`, etc.).
  - Tanda (`.tnd`) — grupo de archivos de audio reproducidos en secuencia.
  - Playlist (`.rmf`) — lista ordenada con modo Once / Loop / Shuffle.
  - Bloque — secuencia ordenada de playlists, tandas y archivos.
- **Sistema de prioridades ALTA/BAJA**: los ítems ALTA suspenden lo que está
  sonando y, al terminar, reanudan el ítem suspendido desde el punto exacto
  (pista + segundo).
- **Cola de reproducción** con límite de 500 ítems pendientes.
- **Watchdog de emergencia**:
  - Detecta silencio prolongado en el bus del Aire (sin canales activos o
    niveles por debajo del umbral).
  - Dispara automáticamente una playlist de emergencia (pre-armada o carpeta
    con shuffle) hasta que el operador la detenga.
  - Beep de alerta configurable (frecuencia, duración, cada N tracks).
- **Log del scheduler en vivo** con contadores y niveles de severidad.

#### 🎛️ Procesamiento de audio

- **Ecualizador gráfico de 10 bandas** (31 Hz a 16 kHz, ±12 dB por banda).
  - Presets de fábrica: Flat, Rock, Pop, Jazz, Classical, Vocal, Bass Boost,
    Treble Boost, Loudness, Podcast.
  - Presets personalizados guardables por el usuario.
  - Aplicación en vivo a todos los canales activos de todos los módulos.
- **Analizador de silencios** con auto-trim:
  - Detección de silencio inicial, final e interno por bloques de N ms.
  - Umbral configurable (dB), duración mínima de silencio.
  - Recorte automático al reproducir.
- **Normalización de niveles** (Peak o RMS):
  - Análisis offline de peak y RMS por archivo.
  - Target configurable (por defecto -1 dBFS Peak o -16 dBFS RMS).
  - Ganancia calculada para evitar clipping.
- **Cache persistente de análisis** (`.rmt` por archivo) para que el análisis
  de trim + normalización sea instantáneo en la segunda reproducción.

#### 🎚️ Visualización

- **VU meter estereofónico** con 3 estilos visuales:
  - **Console**: barra continua con degradé verde/ámbar/rojo.
  - **LED Segments**: LEDs discretos, estilo hardware de radio.
  - **Minimal**: barra redondeada compacta.
- **Analizador de espectro** con 4 modos:
  - **FFT**: espectro de frecuencia clásico.
  - **Log Bands**: 28 bandas logarítmicas estilo ecualizador.
  - **Waterfall**: mapa de calor 3D desplazándose horizontalmente.
  - **Waveform**: osciloscopio en el dominio del tiempo.
- **Mini waveform** en el panel superior (osciloscopio compacto).
- **Barra de progreso animada** estilo onda de audio.
- **Display LED de 7 segmentos** para tiempo transcurrido y restante.
- **Indicador "AL AIRE"** con LED rojo cuando hay audio en el bus del aire.

#### 📂 Gestión de contenido

- **Explorador de archivos integrado**:
  - Árbol de directorios del sistema con carga lazy.
  - Filtros por extensión (todos los formatos soportados por BASS + plugins).
  - Búsqueda por nombre.
  - Drag & drop de archivos hacia pestañas.
  - Reproducción en bus propio con panel de info/tags.
- **Carpetas favoritas** con nombre personalizado y bus de salida propio por
  cada favorito.
- **Botonera (Cart Board)** con grilla configurable:
  - Botones con audio, label, color, hotkey, volumen, loop y notas.
  - Reproducción independiente y simultánea de varios botones.
  - Modo crossfade entre botones (fade-out del actual + fade-in del nuevo).
  - Drag & drop de archivos sobre cualquier botón.
  - Persistencia en `.rcb` (JSON).
- **Editor de TAGs de audio** integrado:
  - Lectura y escritura de metadatos (ID3v1, ID3v2, Vorbis, FLAC, MP4, APE).
  - Campos: título, artistas, álbum, album artists, géneros, año, track, disc,
    comentario.
  - Vista previa de la carátula embebida.

#### 📝 Notas y workspace

- **Editor de notas** con autosave a draft (`%APPDATA%/RadioMaker/notes/`).
- **Workspace persistente** con 3 zonas de pestañas (izquierda, centro,
  derecha) y tipos de pestaña:
  - **Programados** (`.rms`).
  - **Tandas** (`.tnd`).
  - **Playlists** (`.rmf`).
  - **Carts** (`.rcb`).
  - **Notes** (`.txt` con draft autosave).
  - **Carpetas favoritas**.
- **Restauración automática** del estado al reabrir la app.

#### 🎨 Interfaz y experiencia

- **Tema claro / oscuro / sistema** con cambio en vivo.
- **Custom chrome** en Windows/macOS (barra de título propia); chrome nativo en
  Linux.
- **Splash screen** con imágenes rotativas al azar por sesión.
- **Barra de estado** con tips rotativos cada 30 segundos.
- **Sistema de logs centralizado**:
  - Persistencia a `%APPDATA%/RadioMaker/log/rms-log-YYYY-MM-DD.log`.
  - Panel de log en vivo con filtros por nivel y categoría.
  - Exportación a **TXT, CSV, HTML, XLSX y PDF**.
- **Single instance** con Named Pipe (evita abrir 2 ventanas; enfoca la
  existente).

#### 🌐 Integraciones

- **Generador automático de playlists**:
  - Modo por cantidad de tracks o por duración total.
  - Filtros por género, artista, álbum, año, duración.
  - Reglas de variedad: evitar mismo artista consecutivo, ventana mínima entre
    canciones del mismo artista, balance round-robin por artista.
  - Presets guardables (`.rpg`).
  - Previsualización antes de aplicar.
- **Import/Export de playlists**:
  - Import: M3U, M3U8, PLS.
  - Export: M3U, M3U8, PLS.
  - Formato nativo: `.rmf` (JSON).

---

### RMVoice (addon de locuciones)

Reloj parlante con anuncio de hora, temperatura y humedad, integrable con
RadioMaker o standalone.

#### 🗣️ Locuciones

- **4 pistas de audio** por perfil de voz (una por cada categoría):
  - **Hora** con 24 regiones (00..23).
  - **Minuto** con 60 regiones (00..59).
  - **Temperatura** con 81 regiones (00..50 + -1..-30).
  - **Humedad** con 96 regiones (05..100).
- **Sistema de regiones** basado en tablas `.txt` editables:
  - Formato: `HH:MM:SS.mmm HH:MM:SS.mmm índice` (inicio, fin, ID).
  - Parser tolerante (comentarios con `#` o `//`).
- **3 modos de anuncio**:
  - `HourMinute`: solo hora + minutos.
  - `ClimateOnly`: solo clima (temperatura + humedad).
  - `HourMinuteAndClimate`: todo.
- **Anuncio automático cada minuto** configurable.

#### 🌤️ Clima en tiempo real

- **Open-Meteo API** (sin clave):
  - Geocodificación de ciudades.
  - Temperatura 2m y humedad relativa.
- **Búsqueda de ciudades** con lista de resultados.
- **Refresco automático** cada N minutos.
- **Anuncio por voz después de cada refresco** (opcional).
- **Recorte automático** de valores fuera de rango:
  - Temperatura: `[-30, 50]` °C.
  - Humedad: `[5, 100]` %.

#### 🎤 Perfiles de voz

- **Múltiples perfiles** con nombre, idioma y género.
- **Assets empaquetados** con búsqueda en dos ubicaciones
  (`AppContext.BaseDirectory/Assets/` y `Addons/RMVoice/Assets/`).
- **Duplicar / eliminar** perfiles.
- **Persistencia** en `%APPDATA%/RMVoice/profiles.json`.

#### 🧩 Integración con RadioMaker

- **Modo `OwnsBass = false`**: reutiliza el BASS que ya inicializó RadioMaker.
- **Locuciones desde playlists**: como pista suelta o mixado intermedio.
- **Reproducción en el device del bus activo**.
- **UI standalone** lanzable desde el menú **Complementos** de RadioMaker.

---

### RMWave (addon de edición multipista)

Editor de audio multipista para armar proyectos (`.rmw`) que luego se
reproducen como archivos comunes en RadioMaker.

#### 🎛️ Editor multipista

- **Pistas ilimitadas** con nombre, volumen, pan, mute y solo.
- **Múltiples clips por pista** con drag & drop en la línea de tiempo.
- **Reproductor con mixer BASS** (crea un mixer propio por proyecto).
- **Timeline visual** con:
  - Waveforms calculadas por peaks (cache en memoria).
  - Regla de tiempo con zoom (10x a 300x).
  - Snap magnético a otros clips, al cero y al cursor.
  - Loop independiente por pista con inicio/fin.
  - Marcadores de tiempo.

#### ✂️ Edición de clips

- **Cortar / split** por posición del cursor.
- **Duplicar** clips.
- **Trim** (offset + duración).
- **Gain** por clip.
- **Fades** in/out por clip con visualización triangular.
- **Normalización** automática por peak.

#### 🎙️ Grabación

- **Grabación de audio** por micrófono / dispositivo de entrada.
- **Selección de formato**: 44.1 / 48 kHz, mono / estéreo.
- **VU meter en vivo** durante la grabación.
- **Escritura directa a WAV PCM 16-bit**.

#### 💾 Exportación

- **Exportar mix completo** a WAV (PCM 16-bit) o MP3 (via `bassenc`).
- **Exportar selección** por rango de tiempo.
- **Renderizado offline** con `BassMix` + `bassenc`.
- **Importar marcadores de regiones** desde tabla `.txt` (mismo formato que
  RMVoice) para armar cortes.

#### 🎨 Interfaz

- **4 temas visuales**: Oscuro, Claro, Azul Medianoche, Estudio Cálido.
- **Controles custom**:
  - `AudioKnob` (perillas de audio con arco de valor).
  - `VuMeter` (3 estilos: Console, LED, Minimal).
- **Drag & drop** de archivos desde el explorador.

#### 🧩 Integración con RadioMaker

- **Formato `.rmw`** (JSON) reproducido como archivo de audio normal.
- **TimelinePlayer** con `ownsBass = false` cuando corre bajo RadioMaker.
- **Menú Complementos** en RadioMaker para abrir proyectos `.rmw`.
- **Drag & drop** de un `.rmw` a una pestaña de playlist.

---

## 🏗️ Arquitectura del proyecto

RadioMaker se compone de **3 soluciones independientes** que se integran entre
sí mediante referencias de proyecto y contratos públicos.

```bash
RadioMaker/                              ← repo principal
│
├── src/
│   ├── RadioMaker.Core/                 ← Motor de audio (biblioteca)
│   │   ├── Abstractions/                (IAudioEngine, IAudioBus, IAudioChannel)
│   │   ├── Configuration/               (persistencia de configuración)
│   │   ├── Engine/                      (BassEngine, BassBus, BassChannel, RmwChannel)
│   │   ├── Logging/                     (AppLogger, LogService)
│   │   ├── Playback/                    (CartBoardPlayer, PlaylistPlayer, LocutionService)
│   │   ├── Scheduling/                  (SchedulerService, PlaybackEngine, …)
│   │   ├── Tags/                        (lectura/escritura de TAGs)
│   │   └── RadioMaker.Core.csproj       (AssemblyName: RadioMaker.Core)
│   │
│   └── RadioMaker.UI/                   ← Aplicación de escritorio (Avalonia)
│       ├── Configuration/
│       ├── Controls/                    (VuMeter, SpectrumAnalyzer, etc.)
│       ├── Services/
│       ├── ViewModels/
│       ├── Views/
│       ├── Themes/
│       └── RadioMaker.UI.csproj         (AssemblyName: RadioMaker)
│
├── distribution/                        ← READMEs por plataforma
│   ├── README-WINDOWS.md
│   ├── README-LINUX.md
│   └── README-MAC.md
│
├── build-windows.ps1                    ← Script de compilación Windows
├── build-linux.ps1                      ← Script de compilación Linux
├── build-mac.ps1                        ← Script de compilación macOS
├── RadioMaker.sln
└── README.md                            ← Este archivo
```
```bash
RMVoice/                                 ← Addon de locuciones (repo hermano)
│
├── src/
│   ├── RMVoice.Core/                    ← Motor de locuciones (biblioteca)
│   │   ├── Models/                      (VoiceProfile, AudioRegion, WeatherLocation, …)
│   │   ├── Services/                    (AudioService, WeatherService, ProfileStore, …)
│   │   ├── Announcer.cs
│   │   ├── AnnounceRequest.cs
│   │   ├── AnnounceContent.cs
│   │   ├── AnnouncerOptions.cs
│   │   ├── IAnnouncer.cs
│   │   └── RMVoice.Core.csproj          (AssemblyName: RMVoice.Core)
│   │
│   └── RMVoice.UI/                      ← Aplicación standalone (Avalonia)
│       ├── Assets/
│       │   ├── Clima/Hombre/            (T_*.txt, Hu_*.txt + audios)
│       │   └── Hora/Hombre/             (H_*.txt, M_*.txt + audios)
│       ├── Configuration/
│       ├── Controls/
│       ├── Models/
│       ├── Services/
│       ├── ViewModels/
│       ├── Views/
│       └── RMVoice.UI.csproj            (AssemblyName: RMVoice)
│
├── RMVoice.sln
└── README.md
```
```bash
RMWave/                                  ← Addon de edición multipista (repo hermano)
│
├── src/
│   ├── RMWave.Core/                     ← Motor de edición y playback (biblioteca)
│   │   ├── Audio/                       (RecordingEngine, TimelinePlayer, WaveformCache)
│   │   ├── Models/                      (ProjectModel)
│   │   └── RMWave.Core.csproj           (AssemblyName: RMWave.Core)
│   │
│   └── RMWave.UI/                       ← Aplicación standalone (Avalonia)
│       ├── Controls/                    (AudioKnob, VuMeter, CustomChromeWindow)
│       ├── Services/                    (RecentProjectsManager)
│       ├── Themes/                      (ThemeManager)
│       └── RMWave.UI.csproj             (AssemblyName: RMWave)
│
├── RMWave.sln
└── README.md
```

### AssemblyNames de salida

| Proyecto | AssemblyName | Tipo | Salida |
|---|---|---|---|
| `RadioMaker.Core` | `RadioMaker.Core` | Biblioteca | `RadioMaker.Core.dll` |
| `RadioMaker.UI` | `RadioMaker` | Ejecutable | `RadioMaker.exe` (Win) / `RadioMaker` (Linux/Mac) |
| `RMVoice.Core` | `RMVoice.Core` | Biblioteca | `RMVoice.Core.dll` |
| `RMVoice.UI` | `RMVoice` | Ejecutable | `RMVoice.exe` (Win) / `RMVoice` (Linux/Mac) |
| `RMWave.Core` | `RMWave.Core` | Biblioteca | `RMWave.Core.dll` |
| `RMWave.UI` | `RMWave` | Ejecutable | `RMWave.exe` (Win) / `RMWave` (Linux/Mac) |

### Integración entre proyectos

- **RadioMaker.Core** referencia a **RMVoice.Core** y **RMWave.Core**.
- El engine de RMVoice se usa desde `LocutionService` para disparar locuciones.
- El engine de RMWave se usa desde `RmwChannel` para reproducir archivos
  `.rmw` como si fueran audios comunes.
- Los addons UI (`RMVoice.exe`, `RMWave.exe`) se compilan por separado y se
  copian a `Addons/RMVoice/` y `Addons/RMWave/` dentro del publish de
  RadioMaker.

---

## 💻 Requisitos del sistema

### Para ejecutar (usuario final)

- **Windows**: Windows 10 build 1809+ o Windows 11 (x64 / ARM64).
- **Linux**: cualquier distro moderna x64 con PulseAudio / PipeWire / ALSA.
- **macOS**: macOS 11 (Big Sur) o superior (Apple Silicon o Intel).
- **No requiere .NET instalado** en los paquetes self-contained.
- ~500 MB de espacio libre en disco.
- Dispositivo de audio configurado.

### Para compilar (desarrollador)

- **.NET 8 SDK** o superior.
- Librerías nativas de BASS correspondientes a la plataforma:
  - Windows: `bass.dll`, `bassmix.dll`, `bass_fx.dll`.
  - Linux: `libbass.so`, `libbassmix.so`, `libbass_fx.so`.
  - macOS: `libbass.dylib`, `libbassmix.dylib`, `libbass_fx.dylib`.
- Opcional: plugins de formato BASS para FLAC, AAC, Opus, WavPack, etc.

Descargar las librerías nativas desde [un4seen.com](https://www.un4seen.com) y
colocarlas en `RadioMaker/src/RadioMaker.UI/runtimes/{rid}/native/`.

---

## 🔧 Compilación

### Clonar los 3 repos

```bash
git clone https://github.com/tu-usuario/RadioMaker.git
git clone https://github.com/tu-usuario/RMVoice.git
git clone https://github.com/tu-usuario/RMWave.git
```

Los 3 repos deben quedar como carpetas **hermanas** en el mismo directorio
padre:

```
tu-carpeta-de-trabajo/
├── RadioMaker/
├── RMVoice/
└── RMWave/
```

Eso es porque `RadioMaker.Core.csproj` referencia a `..\..\..\RMVoice\src\RMVoice.Core\RMVoice.Core.csproj`
y `..\..\..\RMWave\src\RMWave.Core\RMWave.Core.csproj`.

### Compilación rápida (una plataforma)

```
# Windows
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release

# Linux
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r linux-x64

# macOS (Apple Silicon)
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r osx-arm64
```

### Compilación distribuible (self-contained, con addons)

Usar los scripts de PowerShell desde la raíz de RadioMaker:

```
cd RadioMaker

# Windows → genera RadioMaker-windows-x64.zip
.\build-windows.ps1

# Linux → genera RadioMaker-linux.tar.gz
.\build-linux.ps1

# macOS (Apple Silicon) → genera RadioMaker-mac-arm64.tar.gz
.\build-mac.ps1
```

Cada script:

1. Compila `RadioMaker.UI`, `RMVoice.UI` y `RMWave.UI` para el RID
correspondiente (self-contained).
2. Copia los addons a `Addons/RMVoice/` y `Addons/RMWave/`.
3. Verifica que los binarios y las librerías nativas estén presentes.
4. Incluye el `README-{PLATAFORMA}.md` correspondiente.
5. Genera el `.zip` / `.tar.gz` final.

**Salida**: `RadioMaker/publish/RadioMaker-{plataforma}.[zip|tar.gz]`

---

## 🚀 Uso

### Primer arranque

1. Extraer el `.zip` / `.tar.gz` en una carpeta local.
2. **Windows**: ejecutar `RadioMaker.exe` (SmartScreen → "Más información" →
"Ejecutar de todas formas").
3. **Linux**: ejecutar `./run.sh` (o `bash run.sh` si los permisos se
perdieron al pasar por el pendrive).
4. **macOS**: ejecutar **una vez** `bash first-run.sh` (hace `chmod +x` +
`codesign --force --sign -` + limpia cuarentena) y luego `./run.sh`.

### Ver READMEs específicos por plataforma

Ver `distribution/README-WINDOWS.md`, `README-LINUX.md` y `README-MAC.md`
para guías detalladas de instalación y troubleshooting.

### Uso básico

1. **Configurar buses de salida**: Configuración → Buses de salida.
2. **Cargar una playlist**: Archivo → Abrir → Playlist, o arrastrar un `.rmf`.
3. **Programar**: Archivo → Nuevo → Programados, agregar ítems con horarios.
4. **Ajustar procesamiento**: Configuración → EQ / Silencios / Normalización /
Ducking / Fade.
5. **Ver el log**: menú Ver → Log.
6. **Exportar el log**: botón Exportar en el panel de log.

---

## 📦 Librerías y licencias

### Frameworks base

| Librería ↕▾ | Versión ↕▾ | Licencia ↕▾ | Uso ↕▾ |
|---|---|---|---|
| −**.NET** | 8.0 | MIT | Runtime multiplataforma |
| **Avalonia UI** | 12.1.5 | MIT | Framework de UI |
| **Avalonia.Desktop** | 12.1.5 | MIT | Backend de escritorio |
| **Avalonia.Themes.Fluent** | 12.1.5 | MIT | Tema Fluent |
| **Avalonia.Fonts.Inter** | 12.1.5 | MIT | Tipografía Inter |
| **AvaloniaUI.DiagnosticsSupport** | 2.2.3 | MIT | DevTools (solo Debug) |
⚙

### Patrones y utilidades

| Librería ↕▾ | Versión ↕▾ | Licencia ↕▾ | Uso ↕▾ |
|---|---|---|---|
| −**CommunityToolkit.Mvvm** | 8.4.2 | MIT | MVVM helpers |
| **Material.Icons.Avalonia** | 3.0.2 | MIT | Iconografía Material Design |
⚙

### Motor de audio

| Librería ↕▾ | Versión ↕▾ | Licencia ↕▾ | Uso ↕▾ |
|---|---|---|---|
| −**BASS** | 2.4.x | ⚠️ Gratis uso no comercial | Motor de audio nativo |
| **ManagedBass** | 4.0.2 | MIT | Wrapper .NET de BASS |
| **ManagedBass.Fx** | 4.0.2 | MIT | Addon de efectos (EQ, reverb) |
| **ManagedBass.Mix** | 4.0.2 | MIT | Addon de mezcla multipista |
| **ManagedBass.Enc** | 4.0.2 | MIT | Addon de encoding (MP3) |
| **ManagedBass.Flac** | 4.0.2 | MIT | Decodificador FLAC |
| **ManagedBass.Aac** | 4.0.2 | MIT | Decodificador AAC/M4A |
| **ManagedBass.Ac3** | 4.0.2 | MIT | Decodificador AC-3 |
| **ManagedBass.Aix** | 4.0.2 | MIT | Decodificador AIX |
| **ManagedBass.Alac** | 4.0.2 | MIT | Decodificador ALAC |
| **ManagedBass.Ape** | 4.0.2 | MIT | Decodificador Monkey's Audio |
| **ManagedBass.Dsd** | 4.0.2 | MIT | Decodificador DSD |
| **ManagedBass.Hls** | 4.0.2 | MIT | Streaming HLS |
| **ManagedBass.Mpc** | 4.0.2 | MIT | Decodificador Musepack |
| **ManagedBass.Ofr** | 4.0.2 | MIT | Decodificador OptimFROG |
| **ManagedBass.Opus** | 4.0.2 | MIT | Decodificador Opus |
| **ManagedBass.Spx** | 4.0.2 | MIT | Decodificador Speex |
| **ManagedBass.WA** | 4.0.2 | MIT | Decodificador WavPack |
| **ManagedBass.Wma** | 4.0.2 | MIT | Decodificador Windows Media |
| **ManagedBass.Wv** | 4.0.2 | MIT | Decodificador WavPack |
⚙

### TAGs y metadatos

| Librería ↕▾ | Versión ↕▾ | Licencia ↕▾ | Uso ↕▾ |
|---|---|---|---|
| −**TagLib#** | 2.3.0 | LGPL-2.1 / MIT | Lectura/escritura de TAGs |
⚙

### Generación de documentos

| Librería | Versión | Licencia | Uso |
|---|---|---|---|
| **ClosedXML** | 0.105.1 | MIT | Generación de Excel (`.xlsx`) |
| **QuestPDF** | 2024.12.3 | ⚠️ Community License | Generación de PDF |

### APIs externas (sin librería, HTTP directo)

| Servicio | Uso | Licencia |
|---|---|---|
| **Open-Meteo** | Clima en tiempo real | Datos libres, sin API key |

---

### ⚠️ Notas importantes sobre licenciamiento

#### BASS (Un4seen Developments)

- **Gratis** para uso personal, educativo y proyectos no comerciales.
- **Requiere licencia comercial** para uso en productos que generen ingresos.
- Ver un4seen.com para precios y condiciones.
- Los wrappers `ManagedBass.*` son MIT, pero **la librería nativa de BASS no
lo es**. La licencia de BASS prevalece sobre el wrapper.

#### QuestPDF

- **Community License**: gratis para proyectos con facturación anual **menor
a USD 1.000.000**.
- Para facturación mayor, requiere **Professional o Enterprise** (de pago).
- Ver [questpdf.com/license](https://www.questpdf.com/license/) para detalles.
- El proyecto actual usa `LicenseType.Community` configurado en
`Program.Main`.

#### TagLib#

- Licencia dual **LGPL-2.1** y **MIT**.
- El proyecto lo usa bajo los términos de **LGPL-2.1**.

#### Avalonia UI y todo el ecosistema .NET

- **MIT** sin restricciones para uso comercial.

---

## 🗺️ Roadmap

### Pendientes técnicos (bug fixes y mejoras)

- □  
**Panel "Reproduciendo" / "Siguiente" con locuciones**: cuando suena una
locución o cuando la siguiente pista es una locución, los paneles no la
muestran.
- □  
**P4 — Podar plugins BASS no usados**: revisar si `ManagedBass.Aix`,
`ManagedBass.Ofr` y `ManagedBass.WA` están realmente referenciados, o si se
pueden sacar del `.csproj`.
- □  
**P5 — EQ de RMWave**: implementar `ApplyEqToStream` con
`BASS_FX_BFX_PEAKEQ` (cross-platform) para que el ecualizador de RMWave
aplique al audio.
- □  
**P7 — Refactor estructural**: dividir archivos gigantes
(`MainWindowViewModel`, `PlaylistPlayer`, `PlaybackEngine`,
`MainWindow.axaml.cs`).
- □  
**P8 — RMCast**: decidir si se implementa, se remueve del menú, o se deja
como placeholder.

### Features futuras

- □  
**UI multi-idioma** (actualmente en español).
- □  
**Captura de audio de streaming** (Icecast / SHOUTcast).
- □  
**Soporte de múltiples monitores** para la preescucha.
- □  
**Plugin system** para módulos externos.

### Empaquetado

- □  
**Instaladores nativos**:

- Windows: Inno Setup / MSIX.
- macOS: `.dmg` firmado con Developer ID + notarización.
- Linux: AppImage / Flatpak / Snap.
- □  
**Firma de código** en Windows (certificado EV) y macOS (Apple Developer).
- □  
**CI/CD** con GitHub Actions (build matrix para las 3 plataformas).

---

## 🙏 Créditos

- **BASS** por [Un4seen Developments](https://www.un4seen.com/) — motor de
audio multiplataforma.
- **Avalonia UI** — el mejor framework de UI multiplataforma para .NET.
- **CommunityToolkit.Mvvm** — MVVM sin boilerplate.
- **Material.Icons.Avalonia** — iconografía moderna.
- **Open-Meteo** — API de clima libre y sin clave.
- **Comunidad .NET** en general, por hacer todo esto posible.

---

## 📄 Licencia

> **RadioMaker — Forever FREE Radio Automation Software**

Los tres proyectos (RadioMaker, RMVoice, RMWave) se distribuyen bajo la
licencia **MIT** para el código propio.

**Las librerías de terceros mantienen sus propias licencias** (ver
Librerías y licencias). En particular:

- **BASS** requiere licencia comercial para uso comercial.
- **QuestPDF** requiere licencia Professional/Enterprise para proyectos con
facturación anual ≥ USD 1.000.000.

Para detalles sobre la licencia MIT, ver LICENSE.

---

## 🤝 Contribución

Las contribuciones son bienvenidas. Para cambios grandes, abrí primero un
issue para discutir qué te gustaría cambiar.

### Cómo compilar y probar

1. Fork de los 3 repos (RadioMaker, RMVoice, RMWave).
2. Clonar los 3 forks como carpetas **hermanas**.
3. Instalar .NET 8 SDK y las librerías nativas de BASS para tu plataforma.
4. `dotnet build RadioMaker/RadioMaker.sln` (o abrir la solución en VS / Rider).
5. Correr `build-{plataforma}.ps1` para generar un paquete distribuible.
6. Probar en Windows / Linux / macOS antes de enviar el PR.

### Estilo de código

- C# con `Nullable enable`.
- Nombres en español para el dominio (Programados, Tandas, Playlists, Carts).
- Comentarios XML en APIs públicas.
- Sin `ImplicitUsings` en proyectos que colisionan con `TagLib.File` (por eso
hay `using` explícitos en RadioMaker.Core y RMWave.Core).

---

## 💬 Soporte

- **Issues**: [github.com/tu-usuario/RadioMaker/issues](https://github.com/tu-usuario/RadioMaker/issues)
- **Discusiones**: [github.com/tu-usuario/RadioMaker/discussions](https://github.com/tu-usuario/RadioMaker/discussions)
- **Logs**: `%APPDATA%/RadioMaker/log/` (Windows) o
`~/.config/RadioMaker/log/` (Linux/macOS).

---
<p align="center">
  <strong>🎙️ RadioMaker</strong><br>
  <strong>Copyright (c) 2020 / 2026 - Christian A. Del Monte</strong><br>
  <strong>creadig@gmail.com / creadig@hotmail.com</strong><br>
  <em>Forever FREE Radio Automation Software</em>
</p><p align="center">
  Hecho con ❤️ para la comunidad de radios libres y proyectos independientes.
</p>
