# RadioMaker — Linux installation guide

Version: 2.01 · Linux x64 (x86_64) · Tested distributions: Debian 12+, Ubuntu 22.04+, Fedora 38+, Arch

[Versión en español](README-LINUX.md) · **English**

## Requirements

- Linux x64. ARM64 support is planned for the future.
- .NET does not need to be installed; it is included in the package.
- Approximately 500 MB of free disk space.
- PulseAudio, PipeWire, or ALSA audio server.
- Avalonia native libraries (often already installed):

  ```bash
  # Debian / Ubuntu
  sudo apt install libice6 libsm6 libfontconfig1 libgdiplus

  # Fedora / RHEL
  sudo dnf install libICE libSM fontconfig libgdiplus

  # Arch / Manjaro
  sudo pacman -S libice libsm fontconfig libgdiplus
  ```

## Installation and launch

Copy `RadioMaker-linux.tar.gz` to a local disk, then extract it:

```bash
tar -xzf RadioMaker-linux.tar.gz
cd RadioMaker-linux
./run.sh
```

The `run.sh` script sets executable permissions, which may be lost when copying files through FAT32/exFAT media. You can also set permissions manually:

```bash
chmod +x RadioMaker Addons/RMVoice/RMVoice Addons/RMWave/RMWave
./RadioMaker
```

On the first RadioMaker launch, choose English or Spanish in the language window. Closing it without a choice uses the system language (Spanish if the system is Spanish, English otherwise). The prompt appears only once; change the language later in Settings.

## Expected package layout

```text
RadioMaker-linux/
├── RadioMaker
├── run.sh
├── runtimes/linux-x64/native/
│   ├── libbass.so
│   ├── libbassmix.so
│   └── libbass_fx.so
├── Addons/RMVoice/RMVoice
├── Addons/RMWave/RMWave
└── README-LINUX.md
```

## Troubleshooting

### `./run.sh: Permission denied`

The executable bit may have been lost when copying the package. Run:

```bash
chmod +x run.sh RadioMaker
./run.sh
```

### The app does not start or closes during startup

Review the latest log:

```bash
tail -50 ~/.config/RadioMaker/log/*.log
```

Search for `[ERROR]` or `[FATAL]`.

### `DllNotFoundException: bass` / `libbass.so` not found

Check the native libraries:

```bash
ls runtimes/linux-x64/native/libbass*.so
```

The folder should contain `libbass.so`, `libbassmix.so`, and `libbass_fx.so`. In some environments, the libraries may also need to be copied to the application directory.

### Avalonia Skia initialization error

Install the native dependencies listed above, for example on Debian/Ubuntu:

```bash
sudo apt install libice6 libsm6 libfontconfig1 libgdiplus
```

### No audio device or `BASS_ERROR_DEVICE`

Check that the audio server has active devices:

```bash
pactl info       # PulseAudio
pw-cli info 0    # PipeWire
aplay -l         # ALSA
```

Start or configure the audio server if no output device is available.

### ALSA `unable to open slave`

This can happen when ALSA cannot access the mixer device or the default PipeWire/PulseAudio server. Install the appropriate bridge:

```bash
# Arch Linux
sudo pacman -S pipewire-alsa

# Ubuntu / Debian
sudo apt install pipewire-audio-client-libraries
```

### Wayland vs. X11

Avalonia 12 supports both. If Wayland has graphics issues, try X11:

```bash
GDK_BACKEND=x11 ./run.sh
```

If your user lacks audio-device permissions, review your distribution's audio group and device policy. A group change may require signing out and back in.

## Add-ons (RMVoice / RMWave)

Open them from RadioMaker's **Add-ons** menu or run:

```bash
./Addons/RMVoice/RMVoice
./Addons/RMWave/RMWave
```

## BASS licensing

BASS terms depend on the use case; commercial use may require a license from [un4seen.com](https://www.un4seen.com/). See the project README for third-party dependency notes.

## Support

- [Project repository](https://github.com/ChristianDelMonte/RadioMaker_2/)
- [Report an issue](https://github.com/ChristianDelMonte/RadioMaker_2/issues)

For information about supporting the project, contact `creadig@gmail.com` or `creadig@hotmail.com`.
