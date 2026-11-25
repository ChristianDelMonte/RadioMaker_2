# RadioMaker distribution guide

[Español](README.md) · **English**

RadioMaker is distributed as one package containing the main **RadioMaker** application and its **RMVoice** and **RMWave** add-ons. This folder contains operating-system-specific installation and troubleshooting instructions.

## Package contents

- `RadioMaker` (or `RadioMaker.exe` on Windows): the main radio automation app.
- `Addons/RMVoice/`: time and weather voice-announcement editor and engine.
- `Addons/RMWave/`: multitrack audio editor and `.rmw` playback support.
- `runtimes/`: platform-native audio libraries required by BASS.
- `run.sh` or `first-run.sh`: launch/setup scripts where applicable.

## First launch

On the first launch, RadioMaker displays a language chooser before the splash screen. Choose English or Spanish; the choice is saved and the prompt will not appear again. Closing the chooser without selecting a language uses the operating-system language: Spanish systems start in Spanish and all other system languages default to English. The language can later be changed in Settings.

## Platform instructions

- [Windows](README-WINDOWS.en.md)
- [macOS](README-MAC.en.md)
- [Linux](README-LINUX.en.md)

For product features, source layout, build instructions, and dependency notes, see the [English project README](../README.en.md). For step-by-step operation, see the [English user manual](../RadioMaker/docs/user-manual.en.md).

## Support

- [Project repository](https://github.com/ChristianDelMonte/RadioMaker_2/)
- [Report an issue](https://github.com/ChristianDelMonte/RadioMaker_2/issues)
