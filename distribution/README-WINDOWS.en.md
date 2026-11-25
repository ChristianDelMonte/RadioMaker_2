# RadioMaker — Windows installation guide

Version: 2.01 · Windows 10 (build 1809+) / Windows 11 · x64 and ARM64

[Versión en español](README-WINDOWS.md) · **English**

## Requirements

- Windows 10 build 1809 or later, or Windows 11.
- .NET does not need to be installed; it is included in the package.
- Approximately 500 MB of free disk space.
- An audio device configured in Windows.

## Installation

1. Extract the `.zip` contents to a local folder, for example `C:\RadioMaker\`. **Do not run the application directly from the zip file.**
2. Launch `RadioMaker.exe`.

There is no installer or system registry entry. To uninstall, delete the folder.

## First launch

Windows may show a **Windows SmartScreen** warning because the executable is not signed with a commercial certificate:

> Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting.

Choose **More info**, then **Run anyway**. The add-ons may show the same warning.

On the first RadioMaker launch, a language chooser appears before the splash screen. Choose English or Spanish. If you close it without choosing, the app uses the operating-system language (Spanish when the OS is Spanish, English otherwise). This prompt appears only once; change the language later in Settings.

## Expected package layout

```text
RadioMaker-windows-x64/
├── RadioMaker.exe
├── runtimes/win-x64/native/
│   ├── bass.dll
│   ├── bassmix.dll
│   └── bass_fx.dll
├── Addons/
│   ├── RMVoice/RMVoice.exe
│   └── RMWave/RMWave.exe
└── README-WINDOWS.md
```

## Troubleshooting

### The app does not start or closes during startup

Check `%APPDATA%\RadioMaker\log\` for `rms-log-YYYY-MM-DD.log` files and search for `[ERROR]`.

### BASS could not initialize

An audio device may be missing or disabled. Check **Settings > System > Sound** in Windows.

### `DllNotFoundException: bass`

Make sure `runtimes\win-x64\native\bass.dll` and the rest of the `runtimes/` folder were extracted.

### The app opens but there is no sound

1. Check the Windows system volume.
2. In RadioMaker, open **Settings > Output Buses**.
3. Choose **Test Tone** on the active bus.
4. Check the selected device if there is still no sound.

### Antivirus blocks the executable

Some antivirus products may block unsigned binaries. Review the alert and, if appropriate for your environment, add the `RadioMaker\` folder to the product's allow list.

## Add-ons (RMVoice / RMWave)

Open them from RadioMaker's **Add-ons** menu or run their executables from `Addons/`.

## Code signing

The binaries are not signed. A code-signing certificate is not included.

## Support

- [Project repository](https://github.com/ChristianDelMonte/RadioMaker_2/)
- [Report an issue](https://github.com/ChristianDelMonte/RadioMaker_2/issues)

For information about supporting the project, contact `creadig@gmail.com` or `creadig@hotmail.com`.
