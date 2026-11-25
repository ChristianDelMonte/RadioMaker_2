<p align="center">
	<img alt="RadioMaker" height="150" src="imagenes/RM100_ICON.png"/>
</p>
<p align="center">
	<img alt="RadioMaker" height="50" src="imagenes/RM_SMALL.bmp"/>
</p>

> 🎙️ **Forever FREE Radio Automation Software!**

[Español](README.md) · **English**

Cross-platform radio automation suite consisting of **RadioMaker** as the main application, **RMVoice** for voice announcements, and **RMWave** for multitrack editing. All three projects are distributed together in one package. Written in **C# / .NET 8** with **Avalonia UI** for Windows, Linux, and macOS.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![UI](https://img.shields.io/badge/UI-Avalonia%2012.1.5-8B44AC)
![Audio](https://img.shields.io/badge/audio-BASS%202.4-FF6B00)

---

## 📑 Contents

- [Features](#-features)
  - [RadioMaker (main engine)](#radiomaker-main-engine)
  - [RMVoice (voice announcement add-on)](#rmvoice-voice-announcement-add-on)
  - [RMWave (multitrack editing add-on)](#rmwave-multitrack-editing-add-on)
- [User manual](RadioMaker/docs/user-manual.en.md)
- [Project architecture](#-project-architecture-and-repository-layout)
- [System requirements](#-system-requirements)
- [Build](#-build)
- [Usage](#-usage)
- [Libraries and licenses](#-libraries-and-licenses)
- [Roadmap](#-roadmap)
- [Credits](#-credits)
- [License](#-license)

---

<p align="center">
	<img alt="RadioMaker" height="850" src="imagenes/Radiomaker_UI.jpg"/>
</p>

## ✨ Features

### RadioMaker (main engine)

Professional radio automation with **multiple integrated modules** in a persistent, multi-zone tabbed workspace (left / center / right).

#### 🎵 Playback and mixing

- **Multitrack player with independent buses**: each module (Scheduled, Playlists, Cart Board, File Manager) can use a different audio bus. This is useful for headphone previews while the on-air output continues.
- **Bus exclusivity coordinator**: arbitrates which module plays on each bus using configurable role policies (Cart, Playlist, Scheduler, Emergency, Manual, FileManager, FavoriteFolder), priorities (Low / Normal / High / Air), and interruption behaviors (Stop / Pause / AutoResume).
- Configurable track crossfade (0 to 5 seconds).
- Configurable auto-fade (fade-in at the beginning and fade-out before the end).
- **Automatic microphone ducking**: lowers program audio while the announcer speaks, with adjustable threshold, reduction, hold, attack, and release.
- **Global volume control** in a cascade (user × bus × normalization × ducking), with a safety limit to prevent clipping.

#### 📅 Scheduling (Scheduler)

- Weekly scheduling by days and times, with multiple entries per item.
- **Four scheduled content types**:
  - Individual audio file (`.mp3`, `.flac`, `.wav`, `.rmw`, etc.).
  - Sequence (`.tnd`) — a group of audio files played in order.
  - Playlist (`.rmf`) — an ordered list with Once / Loop / Shuffle modes.
  - Block — an ordered sequence of playlists, sequences, and files.
- **HIGH/LOW priority system**: HIGH-priority items suspend the current item and resume it from the exact point (track + second) when finished.
- Playback queue with a limit of 500 pending items.
- **Emergency watchdog**:
  - Detects prolonged silence on the On-Air bus (no active channels or levels below the threshold).
  - Automatically starts an emergency playlist (preconfigured or shuffled from a folder) until the operator stops it.
  - Configurable alert beep (frequency, duration, every N tracks).
- Live Scheduler log with counters and severity levels.

#### 🔗 Chainers

- Audio lists automatically inserted between tracks in a playlist or a Scheduler sequence.
- Each tab has its own configuration and is saved as **`.rme`**.
- The operator can activate a list (only one can be active at a time), choose sequential or random order, and set how often to insert a Chainer.
- The counter can continue across sources or reset when the source changes, according to the selected setting.
- Chainers do not interrupt Carts or Sequences. They play without their own fades, respect global volume, and inherit the output bus of the playlist or scheduled playback that triggered them.
- When fades are enabled, Chainers fit into playlist or Scheduler track transitions.

#### 🎛️ Audio processing

- **10-band graphic equalizer** (31 Hz to 16 kHz, ±12 dB per band).
  - Factory presets: Flat, Rock, Pop, Jazz, Classical, Vocal, Bass Boost, Treble Boost, Loudness, Podcast.
  - Users can save custom presets.
  - Live application to all active channels in all modules.
- **Silence analyzer** with auto-trim:
  - Detects leading, trailing, and internal silence in blocks of N ms.
  - Configurable threshold (dB) and minimum silence duration.
  - Automatically trims during playback.
- **Level normalization** (Peak or RMS):
  - Offline peak and RMS analysis per file.
  - Configurable target (default: -1 dBFS Peak or -16 dBFS RMS).
  - Gain calculated to avoid clipping.
- **Persistent analysis cache** (`.rmt` per file), so trim and normalization analysis is instant on the second playback.

#### 🎚️ Visualization

- **Stereo VU meter** with three visual styles:
  - **Console**: continuous bar with a green/amber/red gradient.
  - **LED Segments**: discrete LEDs in the style of radio hardware.
  - **Minimal**: compact rounded bar.
- **Spectrum analyzer** with four modes:
  - **FFT**: classic frequency spectrum.
  - **Log Bands**: 28 logarithmic bands in equalizer style.
  - **Waterfall**: horizontally scrolling 3D heat map.
  - **Waveform**: time-domain oscilloscope.
- Mini waveform in the top panel (compact oscilloscope).
- Animated audio-wave progress bar.
- 7-segment LED display for elapsed and remaining time.
- **ON AIR** indicator with a red LED when audio is playing on the On-Air bus.

#### 📂 Content management

- **Integrated file browser**:
  - System directory tree with lazy loading.
  - Extension filters for all formats supported by BASS and its plugins.
  - Name search.
  - Drag and drop files into tabs.
  - Playback on its own bus with an information/tags panel.
- **Favorite folders** with a custom name and their own output bus.
- **Cart Board** with a configurable grid:
  - Buttons with audio, label, color, hotkey, volume, loop, and notes.
  - Independent, simultaneous playback of multiple buttons.
  - Crossfade mode between buttons (fade-out of the current one + fade-in of the next).
  - Drag and drop files onto any button.
  - `.rcb` persistence (JSON).
- Integrated **audio tag editor**:
  - Reads and writes metadata (ID3v1, ID3v2, Vorbis, FLAC, MP4, APE).
  - Fields: title, artists, album, album artists, genres, year, track, disc, comment.
  - Embedded cover-art preview.

#### 📝 Notes and workspace

- **Notes editor** with automatic draft saving (`%APPDATA%/RadioMaker/notes/`).
- **Persistent workspace** with three tab areas (left, center, right) and these tab types:
  - **Scheduled** (`.rms`).
  - **Sequences** (`.tnd`).
  - **Playlists** (`.rmf`).
  - **Chainers** (`.rme`).
  - **Carts** (`.rcb`).
  - **Notes** (`.txt` with automatic draft saving).
  - **Favorite folders**.
- Automatically restores the state when the app is reopened.

#### 🎨 Interface and experience

- Light / dark / system theme with live switching.
- **Custom chrome** on Windows/macOS (custom title bar); native chrome on Linux.
- **Splash screen** with images randomly rotated per session.
- **Status bar** with rotating tips every 30 seconds.
- **Centralized logging system**:
  - Persists to `%APPDATA%/RadioMaker/log/rms-log-YYYY-MM-DD.log`.
  - Live log panel with level and category filters.
  - Export to **TXT, CSV, HTML, XLSX, and PDF**.
- **Single instance** via Named Pipe (prevents opening two windows and focuses the existing one).

#### 🌐 Integrations

- **Automatic playlist generator**:
  - Generate by track count or total duration.
  - Filters by genre, artist, album, year, and duration.
  - Variety rules: avoid consecutive tracks by the same artist, set a minimum window between songs by the same artist, and balance artists round-robin.
  - Saveable presets (`.rpg`).
  - Preview before applying.
- **Playlist import/export**:
  - Import: M3U, M3U8, PLS.
  - Export: M3U, M3U8, PLS.
  - Native format: `.rmf` (JSON).

---

### RMVoice (voice announcement add-on)

Speaking clock with time, temperature, and humidity announcements. It can be integrated with RadioMaker or run standalone.

#### 🗣️ Voice announcements

- **Four audio tracks** per voice profile (one per category):
  - **Hour** with 24 regions (00..23).
  - **Minute** with 60 regions (00..59).
  - **Temperature** with 81 regions (00..50 + -1..-30).
  - **Humidity** with 96 regions (05..100).
- **Region system** based on editable `.txt` tables:
  - Format: `HH:MM:SS.mmm HH:MM:SS.mmm index` (start, end, ID).
  - Tolerant parser (comments with `#` or `//`).
- **Three announcement modes**:
  - `HourMinute`: hour and minutes only.
  - `ClimateOnly`: weather only (temperature + humidity).
  - `HourMinuteAndClimate`: all of them.
- Configurable automatic announcement every minute.

#### 🌤️ Real-time weather

- **Open-Meteo API** (no API key):
  - City geocoding.
  - 2-meter temperature and relative humidity.
- City search with a results list.
- Automatic refresh every N minutes.
- Optional voice announcement after each refresh.
- Automatically clamps out-of-range values:
  - Temperature: `[-30, 50]` °C.
  - Humidity: `[5, 100]` %.

#### 🎤 Voice profiles

- Multiple profiles with name, language, and gender.
- **Packaged assets** searched in two locations (`AppContext.BaseDirectory/Assets/` and `Addons/RMVoice/Assets/`).
- Duplicate or delete profiles.
- Persistence at `%APPDATA%/RMVoice/profiles.json`.

#### 🧩 Integration with RadioMaker

- **`OwnsBass = false` mode**: reuses the BASS instance already initialized by RadioMaker.
- Voice announcements from playlists as a standalone track or an intermediate mix.
- Playback on the device used by the active bus.
- Standalone UI launched from RadioMaker's **Add-ons** menu.

---

### RMWave (multitrack editing add-on)

Multitrack audio editor for creating (`.rmw`) projects that RadioMaker later plays like regular audio files.

#### 🎛️ Multitrack editor

- Unlimited tracks with name, volume, pan, mute, and solo.
- Multiple clips per track, with drag and drop on the timeline.
- Player with a BASS mixer (creates a dedicated mixer per project).
- Visual timeline with:
  - Waveforms calculated from peaks (in-memory cache).
  - Time ruler with zoom (10x to 300x).
  - Magnetic snapping to other clips, zero, and the playhead.
  - Independent per-track loop with start/end.
  - Time markers.

#### ✂️ Clip editing

- Cut / split at the playhead position.
- Duplicate clips.
- Trim (offset + duration).
- Per-clip gain.
- Per-clip fade-in/out with triangular visualization.
- Automatic peak normalization.

#### 🎙️ Recording

- Record audio from a microphone / input device.
- Format selection: 44.1 / 48 kHz, mono / stereo.
- Live VU meter while recording.
- Direct writing to 16-bit PCM WAV.

#### 💾 Export

- Export the full mix to WAV (16-bit PCM) or MP3 (via `bassenc`).
- Export a selected time range.
- Offline rendering with `BassMix` + `bassenc`.
- Import region markers from a `.txt` table (the same format as RMVoice) to create cuts.

#### 🎨 Interface

- Four visual themes: Dark, Light, Midnight Blue, Warm Studio.
- Custom controls:
  - `AudioKnob` (audio knobs with a value arc).
  - `VuMeter` (three styles: Console, LED, Minimal).
- Drag and drop files from the file browser.

#### 🧩 Integration with RadioMaker

- `.rmw` format (JSON) played like a regular audio file.
- `TimelinePlayer` uses `ownsBass = false` when running under RadioMaker.
- RadioMaker **Add-ons** menu can open `.rmw` projects.
- Drag and drop an `.rmw` file into a playlist tab.

---

## 🏗️ Project architecture and repository layout

All three projects live in **one repository**. RadioMaker is the main application; RMVoice and RMWave have Core engines referenced by RadioMaker and their own UI applications, which are packaged as add-ons.

```text
RadioMaker_2/                              ← repository root
├── RadioMaker/                            ← main application
│   ├── docs/
│   │   └── manual-usuario.md              ← user manual and Wiki source (Spanish)
│   ├── src/
│   │   ├── RadioMaker.Core/
│   │   │   ├── Abstractions/               ← audio interfaces
│   │   │   ├── Configuration/              ← settings and workspace
│   │   │   ├── Engine/                     ← BASS engine, buses, and channels
│   │   │   ├── Logging/                    ← event logging
│   │   │   ├── Playback/                   ← players and voice announcement service
│   │   │   ├── Scheduling/                 ← Scheduler and native formats
│   │   │   │   ├── Encadenadores.cs
│   │   │   │   ├── EncadenadoresRegistry.cs
│   │   │   │   └── EncadenadoresStore.cs
│   │   │   │   ├── Tags/                   ← metadata reading and editing
│   │   │   └── RadioMaker.Core.csproj
│   │   └── RadioMaker.UI/
│   │       ├── Configuration/
│   │       ├── Controls/                   ← Spectrum, VU, and UI controls
│   │       ├── Services/
│   │       ├── Themes/
│   │       ├── ViewModels/
│   │       │   └── EncadenadoresEditorViewModel.cs
│   │       ├── Views/
│   │       │   ├── EncadenadoresEditorView.axaml
│   │       │   └── EncadenadoresEditorView.axaml.cs
│   │       ├── runtimes/                   ← native libraries by platform
│   │       └── RadioMaker.UI.csproj
│   ├── RadioMaker.sln
│   └── README.md
├── RMVoice/                                ← voice announcement add-on
│   ├── src/
│   │   ├── RMVoice.Core/                   ← engine, audio, weather, and profiles
│   │   └── RMVoice.UI/                     ← standalone app and Assets
│   └── RMVoice.sln
├── RMWave/                                 ← multitrack editing add-on
│   ├── src/
│   │   ├── RMWave.Core/                    ← timeline, mixing, and recording
│   │   └── RMWave.UI/                      ← editor and visual controls
│   └── RMWave.slnx
├── distribution/                           ← platform guides
├── imagenes/                               ← README images and branding
├── build-windows.ps1                       ← Windows packaging
├── build-linux.ps1                         ← Linux packaging
├── build-mac.ps1                           ← macOS packaging
├── publish/                                ← output generated by the scripts
├── LICENSE
└── README.md                               ← Spanish README
```

### Chainer organization

The feature is split between the engine's model and persistence, the registry that guarantees only one list is active, and the editing UI. `.rme` files store the audio list and its options (order, insertion frequency, and counter reset). Tabs can be placed in the left or center area; each one retains its own file and configuration.

### Main native formats

| Extension | Contents |
| --- | --- |
| `.rmf` | Playlist |
| `.rms` | Scheduled list |
| `.tnd` | Sequence |
| `.rme` | Chainer list/configuration |
| `.rcb` | Cart Board |
| `.rmw` | RMWave multitrack project |

### Output AssemblyNames

| Project | AssemblyName | Type | Output |
|---|---|---|---|
| `RadioMaker.Core` | `RadioMaker.Core` | Library | `RadioMaker.Core.dll` |
| `RadioMaker.UI` | `RadioMaker` | Executable | `RadioMaker.exe` (Windows) / `RadioMaker` (Linux/macOS) |
| `RMVoice.Core` | `RMVoice.Core` | Library | `RMVoice.Core.dll` |
| `RMVoice.UI` | `RMVoice` | Executable | `RMVoice.exe` (Windows) / `RMVoice` (Linux/macOS) |
| `RMWave.Core` | `RMWave.Core` | Library | `RMWave.Core.dll` |
| `RMWave.UI` | `RMWave` | Executable | `RMWave.exe` (Windows) / `RMWave` (Linux/macOS) |

### Integration between projects

- **RadioMaker.Core** references **RMVoice.Core** and **RMWave.Core**.
- The RMVoice engine is used by `LocutionService` to play voice announcements.
- The RMWave engine is used by `RmwChannel` to play `.rmw` files like regular audio.
- The Core engines are included as RadioMaker dependencies. The UI applications (`RMVoice.exe`, `RMWave.exe`) are published under `Addons/RMVoice/` and `Addons/RMWave/` in the distributable package.
- The package keeps all three components together and adds native BASS libraries for the target platform.

---

## 💻 System requirements

### To run (end user)

- **Windows**: Windows 10 build 1809+ or Windows 11 (x64 / ARM64).
- **Linux**: any modern x64 distribution with PulseAudio / PipeWire / ALSA.
- **macOS**: macOS 11 (Big Sur) or later (Apple Silicon or Intel).
- **No .NET installation required** for self-contained packages.
- Approximately 500 MB of free disk space.
- Configured audio device.

### To build (developer)

- **.NET 8 SDK** or later.
- Native BASS libraries for the target platform:
  - Windows: `bass.dll`, `bassmix.dll`, `bass_fx.dll`.
  - Linux: `libbass.so`, `libbassmix.so`, `libbass_fx.so`.
  - macOS: `libbass.dylib`, `libbassmix.dylib`, `libbass_fx.dylib`.
- Optional BASS format plugins for FLAC, AAC, Opus, WavPack, etc.

Download the native libraries from [un4seen.com](https://www.un4seen.com) and place them under `RadioMaker/src/RadioMaker.UI/runtimes/{rid}/native/`.

---

## 🔧 Build

### Clone the repository

```bash
git clone https://github.com/ChristianDelMonte/RadioMaker_2.git
cd RadioMaker_2
```

The repository contains the three projects as sibling folders:

```text
RadioMaker_2/
├── RadioMaker/
├── RMVoice/
└── RMWave/
```

Project references and packaging scripts rely on this layout; keep the three directories in these relative locations.

### Quick build (one platform)

```bash
# Windows
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release

# Linux
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r linux-x64

# macOS (Apple Silicon)
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r osx-arm64
```

### Distributable build (self-contained, with add-ons)

Run the PowerShell scripts from the repository root (`RadioMaker_2`):

```powershell
# Windows → creates RadioMaker-windows-x64.zip
.\build-windows.ps1

# Linux → creates RadioMaker-linux.tar.gz
.\build-linux.ps1

# macOS (Apple Silicon) → creates RadioMaker-mac-arm64.tar.gz
.\build-mac.ps1
```

Each script:

1. Builds `RadioMaker.UI`, `RMVoice.UI`, and `RMWave.UI` for the corresponding RID (self-contained).
2. Copies the add-ons to `Addons/RMVoice/` and `Addons/RMWave/`.
3. Verifies that the binaries and native libraries are present.
4. Includes the corresponding `README-{PLATFORM}.md`.
5. Creates the final `.zip` / `.tar.gz`.

**Output**: `publish/RadioMaker-{platform}.[zip|tar.gz]`

---

## 🚀 Usage

### First launch

1. Extract the `.zip` / `.tar.gz` to a local folder.
2. **Windows**: run `RadioMaker.exe` (SmartScreen → **More info** → **Run anyway**).
3. **Linux**: run `./run.sh` (or `bash run.sh` if executable permissions were lost when copying via removable media).
4. **macOS**: run `bash first-run.sh` **once** (`chmod +x` + `codesign --force --sign -` + remove quarantine), then run `./run.sh`.

### Platform-specific READMEs

See `distribution/README-WINDOWS.md`, `README-LINUX.md`, and `README-MAC.md` for detailed installation and troubleshooting guides.

### Basic use

1. **Configure output buses**: Settings → Output Buses.
2. **Create or open content**: use File → New/Open or the buttons on each tab's toolbar. You can also drag files from File Manager.
3. **Schedule playback**: create a Scheduled tab, add files, sequences, playlists, or blocks, configure days and times, and enable the list.
4. **Configure Chainers**: create a tab in the left or center panel, add audio, set the order and interval, and enable the list. Routing is inherited from the playback source.
5. **Adjust processing and routing**: use Settings for buses, module routing, Fade, Silence, Normalization, Ducking, and other options.
6. **Review logs**: use the Log tab in the lower center panel; you can export the records from there.

For step-by-step instructions for all modules, see the [user manual](RadioMaker/docs/user-manual.en.md).

---

## 📦 Libraries and licenses

### Core frameworks

| Library | Version | License | Use |
|---|---|---|---|
| **.NET** | 8.0 | MIT | Cross-platform runtime |
| **Avalonia UI** | 12.1.5 | MIT | UI framework |
| **Avalonia.Desktop** | 12.1.5 | MIT | Desktop backend |
| **Avalonia.Themes.Fluent** | 12.1.5 | MIT | Fluent theme |
| **Avalonia.Fonts.Inter** | 12.1.5 | MIT | Inter typeface |
| **AvaloniaUI.DiagnosticsSupport** | 2.2.3 | MIT | DevTools (Debug only) |

### Patterns and utilities

| Library | Version | License | Use |
|---|---|---|---|
| **CommunityToolkit.Mvvm** | 8.4.2 | MIT | MVVM helpers |
| **Material.Icons.Avalonia** | 3.0.2 | MIT | Material Design icons |

### Audio engine

| Library | Version | License | Use |
|---|---|---|---|
| **BASS** | 2.4.x | ⚠️ Free for non-commercial use only | Native audio engine; commercial use is subject to licensing |
| **ManagedBass** | 4.0.2 | MIT | .NET wrapper for BASS |
| **ManagedBass.Fx** | 4.0.2 | MIT | Effects add-on (EQ, reverb) |
| **ManagedBass.Mix** | 4.0.2 | MIT | Multitrack mixing add-on |
| **ManagedBass.Enc** | 4.0.2 | MIT | Encoding add-on (MP3) |
| **ManagedBass.Flac** | 4.0.2 | MIT | FLAC decoder |
| **ManagedBass.Aac** | 4.0.2 | MIT | AAC/M4A decoder |
| **ManagedBass.Ac3** | 4.0.2 | MIT | AC-3 decoder |
| **ManagedBass.Alac** | 4.0.2 | MIT | ALAC decoder |
| **ManagedBass.Ape** | 4.0.2 | MIT | Monkey's Audio decoder |
| **ManagedBass.Dsd** | 4.0.2 | MIT | DSD decoder |
| **ManagedBass.Hls** | 4.0.2 | MIT | HLS streaming |
| **ManagedBass.Mpc** | 4.0.2 | MIT | Musepack decoder |
| **ManagedBass.Opus** | 4.0.2 | MIT | Opus decoder |
| **ManagedBass.Spx** | 4.0.2 | MIT | Speex decoder |
| **ManagedBass.Wma** | 4.0.2 | MIT | Windows Media decoder |
| **ManagedBass.Wv** | 4.0.2 | MIT | WavPack decoder |

### Tags and metadata

| Library | Version | License | Use |
|---|---|---|---|
| **TagLib#** | 2.3.0 | LGPL-2.1 / MIT | Tag reading/writing |

### Document generation

| Library | Version | License | Use |
|---|---|---|---|
| **ClosedXML** | 0.105.1 | MIT | Excel (`.xlsx`) generation |
| **QuestPDF** | 2024.12.3 | ⚠️ Conditional Community License | PDF generation; some users/organizations require a paid license |

### External APIs (direct HTTP, no library)

| Service | Use | License |
|---|---|---|
| **Open-Meteo** | Real-time weather | Free API for non-commercial use; commercial use requires a paid plan |

---

### ⚠️ Important licensing notes

RadioMaker, RMVoice, and RMWave are free to download and use. Third-party dependencies retain their own terms: if an end user chooses a commercial use that requires a license or subscription, they must obtain it themselves. This policy does not make third-party dependencies MIT-licensed or replace their terms; RadioMaker must clearly communicate these requirements.

#### BASS (Un4seen Developments)

- **Free for non-commercial use**.
- Its terms require a commercial license for commercial use; licenses are platform-specific.
- See un4seen.com for prices and terms.
- The `ManagedBass.*` wrappers are MIT-licensed, but **the native BASS library is not**. The BASS license takes precedence over the wrapper license.

#### QuestPDF

- Its Community License is free only for eligible categories and is subject to conditions; some companies, public entities, and projects that do not qualify need a paid license.
- See [questpdf.com/license](https://www.questpdf.com/license/) for details.
- The current project uses `LicenseType.Community`, configured in `Program.Main`.

#### Open-Meteo

- The free endpoint is for **non-commercial** use; its terms state that commercial use requires a paid API plan.
- If the user enables weather in a commercial context, they must follow the provider's terms or configure another compatible provider.

#### TagLib#

- Dual-licensed under **LGPL-2.1** and **MIT**.
- This project uses it under the **LGPL-2.1** terms.

#### Avalonia UI and the .NET ecosystem

- **MIT**, with no commercial-use restrictions.

---

## 🗺️ Roadmap

### Future features

- □ **License transparency**: maintain an inventory of dependencies, codecs, services, and terms by platform; explain what RadioMaker includes for free and which licenses/subscriptions users need for their use case.
- □ **Cross-platform audio engine validation**: test BASS and its add-ons on Windows, macOS, and Linux; cover formats/devices and document end-user licensing requirements. Evaluate another backend only if it provides a concrete technical benefit or improves compatibility, not as a requirement for free use.
- □ **Configurable external providers**: allow users to choose/configure weather and other data services; show their requirements and degrade safely when unavailable.
- □ **Streaming output**: encode and send output to Icecast/SHOUTcast with reconnection, “now playing” metadata, and multiple quality profiles; RadioMaker will not depend on selling its own hosting service.
- □ **AutoDJ with clocks and rotations**: generate and maintain continuous programming from hourly templates, categories, and selection rules.
- □ **Music library and anti-repeat rules**: central catalog with metadata, intro/outro/cues, advanced search, and separation by artist, title, genre, and time period.
- □ **Voice tracking**: record, preview, and place announcer segments between the end of one track and the start of the next.
- □ **Broadcast audit and aircheck**: actual as-played history with time/duration and continuous or segmented recording of the on-air bus, with configurable retention.
- □ **Remote access and control API**: check status, “on air,” and “next,” and enable authenticated remote controls.
- □ **Inputs and rebroadcasting**: streams and line input as scheduled sources, including priority takeover and automatic return to the schedule.
- □ **Dynamic voice announcements**: evaluate local/offline TTS and cloud providers as options; document licenses/costs for each engine or voice and keep the current RMVoice workflow available.
- □ **Automatable commands and events**: scheduled actions (volume, start/stop, load/generate lists, HTTP calls) and a manual event queue.
- □ **Station IDs / sweepers**: insert identifiers or separators over intros/outros using cues, with priority and mixing control.
- □ **Commercial scheduling**: ad blocks, campaigns, broadcast reports, and, at a later stage, local ad replacement in rebroadcasts.
- □ **Synchronization and recovery**: automatically import media from watched folders, back up/restore, and recover from failures.
- □ **Multi-station and multi-user operation**: distributed services, operator permissions, and network synchronization (an advanced stage for groups of stations).
- □ **Studio integration**: MIDI/fader start first; console and GPIO protocols as demand and cross-platform availability allow.
- □ **Multilingual UI** (currently in Spanish).
- □ **Multi-monitor support** for preview playback.
- □ **Plugin system** for external modules.
- □ **Newsroom and multimedia production workflow**: collaborative rundown/editor and program-related data (advanced stage; does not block the initial focus on audio automation).

#### Suggested prioritization

0. **Transparency and compliance**: inventory licenses by package, codec, service, and platform; clear end-user documentation and free distribution of the project's own software.
1. **Reliable broadcast foundation**: Windows/macOS/Linux build and validation matrix, native installers, updates, and configuration recovery. Code signing may be optional if it requires paid certificates or memberships.
2. **Music automation**: library, anti-repeat rules, clocks, and AutoDJ.
3. **Production and on-air control**: voice tracking, cues/IDs, events/commands, and audio inputs.
4. **Audit and integration**: as-run log, aircheck, now-playing/API, and secure remote access.
5. **Scaling features**: regional advertising, multi-studio synchronization, multi-station, permissions, and advanced console integration.

These features are proposals for evaluation; they are not necessarily implemented. RadioMaker and its add-ons are distributed free of charge; third-party licenses are respected, and if an end user chooses a use requiring a paid license or service, that user must obtain and pay for it. Every new dependency must be reviewed for all three platforms, package redistribution, and clear documentation of its terms.

### Packaging

- □ **Native installers**:
  - Windows: Inno Setup / MSIX.
  - macOS: `.dmg` signed with Developer ID + notarization.
  - Linux: AppImage / Flatpak / Snap.
- □ **Code signing** on Windows (EV certificate) and macOS (Apple Developer).
- □ **CI/CD** with GitHub Actions (build matrix for all three platforms).

---

## 🙏 Credits

- **BASS** by [Un4seen Developments](https://www.un4seen.com/) — cross-platform audio engine.
- **Avalonia UI** — a cross-platform UI framework for .NET.
- **CommunityToolkit.Mvvm** — MVVM without boilerplate.
- **Material.Icons.Avalonia** — modern iconography.
- **Open-Meteo** — free weather API with no key required.
- The .NET community in general, for making all of this possible.

---

## 📄 License

> **RadioMaker — Forever FREE Radio Automation Software**

The three projects (RadioMaker, RMVoice, RMWave) are distributed under the **MIT** license for their own code.

**Third-party libraries retain their own licenses** (see Libraries and licenses). In particular:

- **BASS** is free for non-commercial use; commercial use may require a license under Un4seen's terms, obtained by the user who needs it.
- **QuestPDF** uses a conditional Community License; users who do not qualify must purchase a license for production use.
- **Open-Meteo** reserves its free endpoint for non-commercial use; commercial use requires a subscription paid by the user.

RadioMaker does not charge for its own software. Third-party dependencies and services are subject to their terms; each end user must obtain and pay for any licenses or subscriptions applicable to their use if they use RadioMaker commercially.

For details of the MIT license, see `LICENSE`.

---

## 🤝 Contributing

Contributions are welcome. For large changes, open an issue first to discuss what you would like to change.

### How to build and test

1. Fork the `RadioMaker_2` repository.
2. Clone your fork; RadioMaker, RMVoice, and RMWave are already organized as folders in the repository.
3. Install the .NET 8 SDK and the native BASS libraries for your platform.
4. Run `dotnet build RadioMaker/RadioMaker.sln` (or open the solution in VS / Rider).
5. Run `build-{platform}.ps1` to create a distributable package.
6. Test on Windows / Linux / macOS before submitting the PR.

### Code style

- C# with `Nullable enable`.
- Spanish domain names (Programados, Tandas, Playlists, Carts).
- XML comments on public APIs.
- No `ImplicitUsings` in projects that conflict with `TagLib.File` (therefore RadioMaker.Core and RMWave.Core use explicit `using` directives).

---

## 💬 Support

- **Issues**: [github.com/ChristianDelMonte/RadioMaker_2/issues](https://github.com/ChristianDelMonte/RadioMaker_2/issues)
- **Discussions**: [github.com/ChristianDelMonte/RadioMaker_2/discussions](https://github.com/ChristianDelMonte/RadioMaker_2/discussions)
- **Logs**: `%APPDATA%/RadioMaker/log/` (Windows) or `~/.config/RadioMaker/log/` (Linux/macOS).

---
<p align="center">
  <strong>🎙️ RadioMaker</strong><br>
  <strong>Copyright (c) 2020 / 2026 - Christian A. Del Monte</strong><br>
  <strong>creadig@gmail.com / creadig@hotmail.com</strong><br>
  <em>Forever FREE Radio Automation Software</em>
</p><p align="center">
  Made with ❤️ for the community of free radio stations and independent projects.
</p>
