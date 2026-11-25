# RadioMaker — macOS installation guide

Version: 2.01 · macOS 11 (Big Sur) or later · Apple Silicon (arm64) and Intel (x64)

[Versión en español](README-MAC.md) · **English**

## Requirements

- macOS 11 or later.
- .NET does not need to be installed; it is included in the package.
- Approximately 500 MB of free disk space.
- An audio device configured in macOS.

## Important: first-run signing setup

The binaries are not signed with an Apple Developer certificate. On Apple Silicon, macOS may refuse to open unsigned binaries. Before launching the app for the first time, run `first-run.sh`. It applies executable permissions, ad-hoc code signing, and removes the quarantine attribute. This does not require an Apple certificate and is intended for personal use.

## Installation

1. Copy `RadioMaker-mac-arm64.tar.gz` or `RadioMaker-mac-x64.tar.gz` to a local disk. Do not run it directly from a removable drive.
2. Extract and enter the package directory:

   ```bash
   tar -xzf RadioMaker-mac-arm64.tar.gz
   cd RadioMaker-mac-arm64
   ```

3. Run the setup once:

   ```bash
   bash first-run.sh
   ```

4. Launch the app:

   ```bash
   ./run.sh
   ```

On the first RadioMaker launch, choose English or Spanish in the language window. Closing it without a choice uses the system language (Spanish if the system is Spanish, English otherwise). The prompt appears only once; you can later change the language in Settings.

## Expected package layout

```text
RadioMaker-mac-arm64/
├── RadioMaker
├── first-run.sh
├── run.sh
├── runtimes/osx-arm64/native/
│   ├── libbass.dylib
│   ├── libbassmix.dylib
│   └── libbass_fx.dylib
├── Addons/RMVoice/RMVoice
├── Addons/RMWave/RMWave
└── README-MAC.md
```

## Troubleshooting

### “Damaged and can't be opened” or invalid code signature

Run `first-run.sh`. For manual ad-hoc signing:

```bash
codesign --force --sign - RadioMaker
codesign --force --sign - Addons/RMVoice/RMVoice
codesign --force --sign - Addons/RMWave/RMWave
```

### “Cannot be opened because the developer cannot be verified”

Use Finder's right-click **Open**, then confirm **Open** again, or remove quarantine attributes:

```bash
xattr -d com.apple.quarantine RadioMaker
xattr -d com.apple.quarantine Addons/RMVoice/RMVoice
xattr -d com.apple.quarantine Addons/RMWave/RMWave
```

### `DllNotFoundException`

Check that `runtimes/osx-arm64/native/` or `runtimes/osx-x64/native/` contains `libbass.dylib`, `libbassmix.dylib`, and `libbass_fx.dylib`.

### The app does not start or closes during startup

Check the logs (the location can vary by .NET version):

```bash
tail -50 ~/.config/RadioMaker/log/*.log 2>/dev/null
# or
tail -50 ~/Library/Application\ Support/RadioMaker/log/*.log 2>/dev/null
```

Search for `[ERROR]` or `[FATAL]`.

### BASS could not initialize

Check that an audio device is enabled in **System Settings > Sound**.

## Apple Silicon vs. Intel

`RadioMaker-mac-arm64.tar.gz` is for Apple Silicon. Use `RadioMaker-mac-x64.tar.gz` for Intel Macs. Run `uname -m`: `arm64` indicates Apple Silicon; `x86_64` indicates Intel.

## Add-ons (RMVoice / RMWave)

Open the add-ons from RadioMaker's **Add-ons** menu, or run:

```bash
./Addons/RMVoice/RMVoice
./Addons/RMWave/RMWave
```

If macOS blocks an add-on, apply the same ad-hoc signing procedure to its executable.

## BASS licensing

BASS terms depend on the use case; commercial use may require a license from [un4seen.com](https://www.un4seen.com/). See the project README for third-party dependency notes.

## Support

- [Project repository](https://github.com/ChristianDelMonte/RadioMaker_2/)
- [Report an issue](https://github.com/ChristianDelMonte/RadioMaker_2/issues)

For information about supporting the project, contact `creadig@gmail.com` or `creadig@hotmail.com`.
