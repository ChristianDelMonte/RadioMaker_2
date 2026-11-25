<p align="center">
	<img alt="RadioMaker" height="150" src="imagenes/RM100_ICON.png"/>
</p>
<p align="center">
	<img alt="RadioMaker" height="50" src="imagenes/RM_SMALL.bmp"/>
</p>

> 🎙️ **Forever FREE Radio Automation Software!**

[Español](README.md) · **English**

RadioMaker is a cross-platform radio automation suite made up of **RadioMaker**, the main application, and two add-ons: **RMVoice** for voice announcements and **RMWave** for multitrack audio editing. The three projects ship together in one package. The applications are written in **C# / .NET 8** with **Avalonia UI** for Windows, Linux, and macOS.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![UI](https://img.shields.io/badge/UI-Avalonia%2012.1.5-8B44AC)
![Audio](https://img.shields.io/badge/audio-BASS%202.4-FF6B00)

## Contents

- [Features](#features)
- [User manual](RadioMaker/docs/user-manual.en.md)
- [Repository structure](#repository-structure)
- [System requirements](#system-requirements)
- [Build](#build)
- [First launch and basic use](#first-launch-and-basic-use)
- [Libraries and licensing](#libraries-and-licensing)
- [Roadmap](#roadmap)
- [Contributing and support](#contributing-and-support)

## Features

### RadioMaker — main application

RadioMaker is a radio automation system with a persistent workspace and tabbed modules across left, center, and right areas.

#### Playback and mixing

- Independent output buses for Scheduled, Playlists, Cart Board, and File Manager, allowing previews on a separate device while the on-air output continues.
- Configurable bus-exclusivity policies by module role, priority, and interruption behavior (stop, pause, or automatic resume).
- Configurable track crossfade and automatic fades.
- Microphone ducking with adjustable threshold, reduction, hold, attack, and release.
- Cascaded global, bus, normalization, and ducking volume with a safety ceiling.

#### Scheduler and automation

- Weekly schedules by day and time, with multiple entries per item.
- Scheduled files, `.tnd` sequences, `.rmf` playlists, and ordered blocks containing these types.
- High-priority items can suspend current playback and resume it afterward.
- Playback queue and live Scheduler log.
- Emergency watchdog detects prolonged silence and can start a prepared emergency playlist. An alert beep is configurable.

#### Chainers

- Insert audio between playlist or Scheduler tracks. Chainers do not interrupt Carts or sequences.
- Each tab has its own configuration and `.rme` file; only one list can be active at a time.
- Choose sequential or random order, insertion interval, and whether the counter resets when the source changes.
- Chainers have no fades of their own, use global volume, inherit the source output bus, and fit into the configured transitions.

#### Audio processing and meters

- Ten-band graphic EQ (31 Hz–16 kHz, ±12 dB per band), factory presets, and user presets. EQ changes are applied live to supported active channels.
- Silence analysis and automatic trimming where supported.
- Peak or RMS normalization where supported, with persistent per-file analysis cache (`.rmt`).
- Stereo VU meter with Console, LED Segments, and Minimal styles.
- Spectrum views: FFT, log bands, waterfall, and waveform; mini waveform and playback time displays.

#### Content and workspace

- File Manager with directory browsing, format filters, search, drag and drop, preview, and audio information.
- Favorite folders with custom names and output buses.
- Cart Board with configurable buttons, audio, labels, colors, shortcuts, volume, looping, notes, and crossfade.
- Audio tag editor for supported formats.
- Plain-text Notes with automatic drafts.
- Persistent workspace across left, center, and right tab areas.
- Native formats: `.rmf` playlists, `.rms` Scheduled lists, `.tnd` sequences, `.rme` Chainers, `.rcb` Cart boards, and `.rmw` RMWave projects.
- Playlist import/export for M3U, M3U8, and PLS; automatic playlist generator with filters and variety rules.

#### Interface and logging

- Light, dark, and system themes; Spanish and English UI.
- On the first launch, a language chooser appears before the splash screen. Choosing Spanish or English saves that preference. Closing the chooser uses the operating-system language; Spanish systems start in Spanish, while other languages default to English. The choice can later be changed in Settings.
- Centralized logs with severity and category filters, live playback entries, and export to TXT, CSV, HTML, XLSX, and PDF.
- Single-instance behavior to focus an already running window.

### RMVoice — voice announcements add-on

RMVoice provides voice profiles and audio-region tables for time, temperature, and humidity announcements. It supports hour/minute, climate-only, and combined announcements, weather retrieval, editable profiles, and integration with RadioMaker playlists and Scheduler items. It can also be run standalone. If RMVoice is unavailable, RadioMaker remains usable without its announcement features.

### RMWave — multitrack editor add-on

RMWave edits multitrack `.rmw` projects with clips, tracks, gain, fades, EQ, markers, recording, playback, and mix or selection export. RadioMaker can play `.rmw` projects through the integrated RMWave engine and can open the editor from its Add-ons menu or supported context menus.

## Repository structure

All three applications are maintained in one repository. RadioMaker is the main app; the RMVoice and RMWave Core projects are referenced by it, while their UI applications are packaged as add-ons.

```text
RadioMaker_2/
├── RadioMaker/
│   ├── docs/                 user manual
│   └── src/
│       ├── RadioMaker.Core/  audio engine, playback, Scheduler, models
│       └── RadioMaker.UI/    main application and UI
├── RMVoice/
│   └── src/                  RMVoice.Core and RMVoice.UI
├── RMWave/
│   └── src/                  RMWave.Core and RMWave.UI
├── distribution/             platform installation guides
├── imagenes/                 README images and project branding
├── build-windows.ps1
├── build-linux.ps1
├── build-mac.ps1
├── publish/                  generated package output
├── LICENSE
└── README.md / README.en.md
```

The Chainers model, registry, and persistence live in RadioMaker.Core; the editor lives in RadioMaker.UI. `.rme` files store the audio list and insertion options. Tabs can be placed in the left or center area and keep separate configurations.

## System requirements

### Running the packaged application

- **Windows:** Windows 10 build 1809 or later, or Windows 11 (x64 / ARM64).
- **Linux:** a modern x64 distribution with PulseAudio, PipeWire, or ALSA.
- **macOS:** macOS 11 (Big Sur) or later, Apple Silicon or Intel.
- Self-contained packages include the .NET runtime; allow approximately 500 MB of free disk space and configure an audio device.

### Building from source

- .NET 8 SDK or later.
- Native BASS libraries for the target platform: `bass.dll` / `libbass.so` / `libbass.dylib`, plus the required mix and effects libraries.
- Optional BASS format plugins for FLAC, AAC, Opus, WavPack, and other formats.

Place native libraries under `RadioMaker/src/RadioMaker.UI/runtimes/{rid}/native/`. Obtain them from [un4seen.com](https://www.un4seen.com/).

## Build

Clone the repository and keep the three project folders together:

```bash
git clone https://github.com/ChristianDelMonte/RadioMaker_2.git
cd RadioMaker_2
```

Quick build examples:

```bash
# Windows
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release

# Linux x64
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r linux-x64

# macOS Apple Silicon
dotnet build RadioMaker/src/RadioMaker.UI/RadioMaker.UI.csproj -c Release -r osx-arm64
```

Run the packaging scripts from the repository root to create self-contained packages with add-ons:

```powershell
.uild-windows.ps1
.uild-linux.ps1
.uild-mac.ps1
```

The scripts build RadioMaker, RMVoice, and RMWave for their target runtime, copy the add-ons, validate required binaries and native libraries, include the platform guide, and write the package under `publish/`.

## First launch and basic use

Extract the package to a local folder. On first launch, choose the interface language in the small startup window. If it is closed without a choice, RadioMaker uses the system language (Spanish when the system is Spanish, English otherwise). This initial choice is saved and is not asked again; it can be changed later in Settings.

- **Windows:** launch `RadioMaker.exe`. Windows SmartScreen may require **More info > Run anyway** because the binaries are not commercially signed.
- **Linux:** launch `./run.sh`; use `bash run.sh` if executable permissions were lost while copying through FAT32/exFAT media.
- **macOS:** run `bash first-run.sh` once, then launch with `./run.sh`. The setup script applies executable permissions, ad-hoc signing, and removes the quarantine attribute.

Basic workflow:

1. Configure **Settings > Output Buses** and module routing.
2. Create or open content from **File > New/Open**, tab toolbars, or drag and drop from File Manager.
3. Create a Scheduled tab, add content, set days and times, and enable the list.
4. Configure and enable a Chainers list if needed.
5. Adjust fades, silence handling, normalization, ducking, and other options in Settings.
6. Review or export playback records from the lower **Log** panel.

See the [user manual](RadioMaker/docs/user-manual.en.md) and the [Windows](distribution/README-WINDOWS.en.md), [Linux](distribution/README-LINUX.en.md), and [macOS](distribution/README-MAC.en.md) guides for detailed instructions.

## Libraries and licensing

The project uses .NET, Avalonia UI, CommunityToolkit.Mvvm, ManagedBass wrappers, BASS native libraries, TagLib#, ClosedXML, QuestPDF, and Open-Meteo. Their licenses and terms are independent of RadioMaker's own source license.

RadioMaker, RMVoice, and RMWave source code are distributed under the MIT license in `LICENSE`. Some third-party libraries or services may impose additional conditions, including commercial-use licenses or subscriptions. Users are responsible for checking and obtaining any third-party license required for their use. In particular, check the current terms for BASS, QuestPDF, Open-Meteo, and TagLib# before redistribution or commercial use. See the Spanish [README](README.md) for the current dependency inventory and references.

## Roadmap

Ideas under consideration include:

- A documented Windows/macOS/Linux build and validation matrix, native installers, and configuration recovery.
- Music-library indexing, advanced search, and anti-repeat rules for AutoDJ.
- Streaming output to Icecast/SHOUTcast, now-playing metadata, and reconnect handling.
- Voice tracking, cue points, sweepers, and programmable actions.
- As-run reporting, aircheck recording, remote control API, and authenticated access.
- Configurable weather/data providers, backup tools, and watched-folder import.
- MIDI/fader-start integration and, at a later stage, multi-station workflows.

These are proposals, not implemented features. Each addition should be reviewed for Windows, macOS, Linux, redistribution, and third-party licensing.

## Credits

- [BASS](https://www.un4seen.com/) by Un4seen Developments.
- Avalonia UI, CommunityToolkit.Mvvm, and Material.Icons.Avalonia.
- Open-Meteo weather API and the .NET community.

## License

The project's own source code is distributed under the MIT license. Third-party libraries and services retain their own licenses and terms. See [`LICENSE`](LICENSE) and the dependency notes above.

## Contributing and support

Contributions are welcome. For larger changes, open an issue first to discuss the proposal. Build and verify the affected applications on Windows, Linux, and macOS before submitting a pull request.

- [Issues](https://github.com/ChristianDelMonte/RadioMaker_2/issues)
- [Discussions](https://github.com/ChristianDelMonte/RadioMaker_2/discussions)
- Logs: `%APPDATA%/RadioMaker/log/` on Windows; `~/.config/RadioMaker/log/` on Linux and macOS (the exact app-data path may vary by platform).
