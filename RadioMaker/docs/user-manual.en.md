# RadioMaker User Manual

> Initial user manual for RadioMaker and its RMVoice and RMWave add-ons. The interface and features may change between releases.

[Versión en español](manual-usuario.md) · **English**

## 1. What is RadioMaker?

RadioMaker is the main radio automation and playback application. It lets you organize audio into playlists, Cart buttons, sequences, and scheduled lists. It also includes a File Manager, output-bus routing, and audio tools.

The add-ons included in the package extend its features:

- **RMVoice** manages voice profiles and creates time and weather announcements.
- **RMWave** edits multitrack `.rmw` projects and exports mixes.

## 2. Getting started

On the first launch, a window asks you to choose English or Spanish before the splash screen. If you close it without choosing, RadioMaker uses the system language: Spanish when the system is set to Spanish, English for any other language. This choice is saved and the prompt will not appear again. You can change the language later in Settings.

### 2.1 Check the audio output

1. Open **Settings** from the gear icon in the main window.
2. Open **Output Buses** and configure the available audio devices.
3. Under **Module Routing**, assign a bus to each module that has a selector: Cart, Playlist, Scheduled, and File Manager.
4. Check the current bus in RadioMaker's bottom bar.

Sequences and Chainers do not have their own bus selector. They inherit routing from the Scheduler or playlist that plays them. The overall level is controlled from the top panel.

### 2.2 Main window layout

- **Top bar:** File, Edit, Playlists, Settings, Add-ons, and Help menus; on-air indicator and theme selector.
- **Top information panel:** current and next items, time, clock, volume, announcements, Ducking, and Emergency controls.
- **Left column:** Events and work tabs such as Scheduled, Sequences, Playlists, Chainers, and Notes.
- **Center column:** Cart boards and work tabs. Spectrum, Equalizer, and Log are in the lower area.
- **Right column:** File Manager and favorite folders.
- **Bottom bar:** playback status, messages, tips, and selected bus.

The exact contents of each column depend on the tabs you have open.

### 2.3 Work tabs

- Use **+** on a tab bar to create a tab type available in that column.
- Double-click a tab name to rename it.
- Use **X** to close tabs that can be closed.
- Drag files from File Manager to a compatible tab to add them.
- Each editor has its own controls to create, open, and save files in its native format.

RadioMaker saves the workspace so it can restore the tabs and layout at the next startup. Audio files referenced by playlists, sequences, and other projects remain at their original locations; keep those paths available.

## 3. File Manager and favorite folders

File Manager browses folders, filters by file type, searches by name, and previews audio. Its preview bus is configured separately from the module routing.

1. Browse to a folder or use the personal folder, Desktop, and parent-folder shortcuts.
2. Choose a filter and enter part of a name in the search field.
3. Preview the file and stop playback with the corresponding control.
4. Drag the file to a playlist, sequence, Cart, or another compatible tab.

Depending on the file type, the context menu can edit tags, open a project in RMWave, or load a native project into a tab. You can also add folders to Favorites and open them from dedicated tabs.

## 4. Playlists

A playlist contains audio tracks and, when RMVoice is available, voice-announcement commands. It is saved as **`.rmf`**.

### Create and edit a playlist

1. Create a **Playlist** tab with **+**, or open an existing `.rmf` file.
2. Add tracks with **+** or drag them from File Manager.
3. Reorder tracks with the move controls. Disable a track to exclude it from playback.
4. Choose **Edit** from the context menu to change track properties. You can adjust volume, fades, mixing, and the announcement type for voice commands.
5. Save with the toolbar buttons. Use **Save As** to create another file.

Playlist modes are **Once**, **Repeat**, and **Shuffle**. Playback controls include previous, play, resume, pause, stop, and next. Pausing an RMVoice announcement stops it; resuming starts the announcement again from the beginning.

Fades and crossfades are configured in **Settings > Fade**. RMVoice announcements are commands, not ordinary audio files.

Playlists can be imported from and exported to **M3U/M3U8** and **PLS**. External formats may not preserve all RadioMaker-specific properties. Use **Edit > Playlists > Automatic Generator** to build a playlist from a folder and save or load generator presets.

## 5. Scheduled lists and Events

A **Scheduled** tab contains items that the Scheduler triggers on configured days and times. Each list is saved as **`.rms`** and has a switch to enable or disable it. A disabled list will not trigger.

Scheduled content types:

- **File:** plays one audio file.
- **Sequence:** plays a `.tnd` sequence.
- **Playlist:** plays a `.rmf` playlist.
- **Block:** plays an ordered group of playlists, sequences, and files.

Add an item, open its editor, and configure its content, days, time, priority, and other available options. Save the `.rms` file and enable the list when you want the Scheduler to process it. Use the tab's bus selector to choose the Scheduled output.

The **Events** panel shows upcoming triggers from enabled lists, including their times and durations, plus Scheduler status and logs.

Scheduled tab controls can start or stop list playback and move between, pause, or resume items. A **High** priority scheduled item can take precedence over a manually playing playlist; when it finishes, the playlist resumes where it stopped.

## 6. Sequences

A sequence is an ordered group of tracks that plays as one unit. Edit it in a **Sequence** tab and save it as **`.tnd`**.

1. Create a sequence or open an existing `.tnd` file.
2. Add and reorder audio files.
3. Enable or disable tracks with their switches.
4. Adjust individual volume and the delay between tracks if needed.
5. Save the file and add it to a Scheduled list.

The sequence editor has no playback controls. A sequence inherits routing from the Scheduler and is not interrupted for Chainer insertion.

## 7. Chainers

Chainers insert audio between tracks in a playlist or Scheduler sequence. They do not apply to Carts or sequences themselves. Configure them in their own tab and save as **`.rme`**.

1. Create a Chainers tab in the left or center column.
2. Drag audio from File Manager and order it in the list.
3. Enable the tab with its switch. Only one Chainer list can be active; enabling another disables the previous one.
4. Choose sequential or random playback.
5. Set how many source tracks play between Chainers.
6. Choose whether the counter resets when the source changes, for example from a playlist to Scheduled playback.
7. Save the configuration as `.rme`.

Chainers have no fades of their own and use the global volume. When fades are enabled, they fit into playlist or Scheduler transitions: the current track fades out and the next track fades in according to the configured duration. Routing is inherited from the source that inserts them.

The tab configures the list only; it has no playback controls.

## 8. Carts

Cart is a soundboard for firing audio directly. Each button can have an assigned file, label, and keyboard shortcut. A board is saved as **`.rcb`**.

- Choose **Edit** on a button to assign a file and configure its options.
- Select a button and choose **Play** to trigger it.
- Use **Stop All** to stop audio started from the board.
- Enable or disable Cart crossfade from its toolbar or Settings.
- Choose the output from the Cart bus selector.
- Right-click a button to edit it, edit tags, open its project in RMWave, play, stop, or clear its assignment.

You can keep multiple Cart boards open. Chainers are not inserted into Cart playback.

## 9. Spectrum, VU, and audio processing

In the lower center panel:

- **Spectrum** offers FFT, bands, waterfall, and waveform views. Click the visualizer to change modes.
- **Equalizer** adjusts RadioMaker's EQ for common audio files.
- **Log** displays playback and system messages.

The top panel shows the current and next audio. The mini visualizer and VU meter follow the active source, including RMVoice announcements when a stream is available.

Use **Settings** to configure:

- **Fade:** fade-in, fade-out, and crossfade.
- **Silence:** silence detection and trimming where supported.
- **Normalization:** level adjustment where supported.
- **Ducking:** automatic audio attenuation when microphone signal is detected.
- **Output Buses:** available devices and buses.
- **Module Routing:** buses for Cart, Playlist, Scheduled, and File Manager.
- **VU Meter:** meter appearance and behavior.
- **Format Plugins:** audio formats available in the package.
- **Appearance:** visual options.
- **Weather:** settings used by weather announcements.
- **Behavior:** coexistence policies when playback modules compete for resources.

Settings apply immediately where supported. Save bus settings from their section.

### Audio tags and logs

Use **Edit > Edit Tags** or a file's context menu to change metadata where the format allows it. `.rmw` projects open in RMWave instead of being treated as audio files with tags.

The **Log** panel shows playback records. Its export options can save the log in one of the formats offered by the application.

### Notes

Use **File > New > Note** to open a plain-text editor. Notes are automatically saved as drafts while you type and can be opened or saved as **`.txt`** files. Until you choose a location, the draft remains associated with its workspace tab.

## 10. RMVoice: time and weather announcements

RMVoice provides audio and profiles that RadioMaker uses for time and weather announcements. Open it from **Add-ons > RM Voice** or the options in the top panel.

### Configure a profile

1. Open RMVoice and select a voice profile.
2. Under **Playback**, assign audio and the corresponding tables for hours, minutes, temperature, and humidity.
3. Use **Reload Profile** to reload resources and **Save Profiles** to save changes.
4. Test with **Play Selection** or the quick announcement buttons.
5. Configure the location and weather updates in the weather settings.

RadioMaker provides quick buttons for time and weather announcements. Voice commands can also be added to playlist tracks and compatible Scheduler sequences. Announcements cannot play if RMVoice is unavailable or the selected profile lacks configured resources.

## 11. RMWave: audio editing and export

RMWave is a multitrack editor. Open it from **Add-ons > RM Wave**, from a supported file's context menu, or from an **Edit in RMWave** button.

### Create a project

1. Choose **File > New** or the new-project button.
2. Add a track and place audio clips on the timeline.
3. Select a clip to edit its start, offset, duration, gain, fade-in, and fade-out.
4. Use editing commands to duplicate, split at the cursor, delete, or normalize clips.
5. Save the project as **`.rmw`**.

Projects refer to source files; do not move or delete those files if you want to reopen the project correctly.

### Play, record, and export

- Use **Play**, **Pause**, and **Stop** on the transport bar. Space toggles play/pause.
- Select the device from **Devices**.
- To record, choose **Record Audio** and select the input and destination in the dialog.
- Add, manage, and import region markers.
- Use **Export Mix** to render the full project and **Export Selection** to render a selected range.

RMWave creates `.rmw` files for use as sources in RadioMaker. RadioMaker plays these projects through the integrated RMWave engine.

Main RMWave shortcuts:

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | New project |
| `Ctrl+O` | Open project |
| `Ctrl+S` | Save project |
| `Ctrl+E` | Export mix |
| `Ctrl+Shift+E` | Export selection |
| `Ctrl+D` | Duplicate selected clip |
| `Ctrl+B` | Split clip at cursor |
| `Ctrl+R` | Open audio recording |
| `Space` | Play or pause |
| `Delete` | Delete selected clip or range |

## 12. Emergency

Configure Emergency under **Settings > Emergency**. Set up the intended source or playlist and its options before using it on air. The Emergency button starts the configured mode; the top banner indicates it is active and offers **Stop Emergency**.

Test the source, volume, and bus off air before relying on this feature in a real situation.

## 13. RadioMaker file formats

| Extension | Contents |
| --- | --- |
| `.rmf` | Playlist |
| `.rms` | Scheduled list |
| `.tnd` | Sequence |
| `.rme` | Chainer list |
| `.rcb` | Cart board |
| `.rmw` | RMWave multitrack project |
| `.txt` | Plain-text note |

The playlist generator uses **`.rpg`** presets. These store generator options and are not playlists.

RadioMaker native files store settings and media paths. To move projects to another computer, also copy the referenced audio or make sure it is available at the same path.

## 14. Troubleshooting

### There is no audio

1. Check that the device is available under **Output Buses**.
2. Check the bus assigned to the playback module.
3. Check global and item volume.
4. Confirm that the file exists and its format is supported by the available plugins.

### An announcement is silent

1. Check that RMVoice is available from the panel or **Add-ons** menu.
2. Check that a profile is selected and its audio and tables are configured.
3. Check the location and weather data for weather announcements.
4. Test an announcement directly in RMVoice.

### A project cannot find its audio

Projects store paths to external media. Restore the files to their original locations or reassign missing files in the appropriate editor.

### Audio uses the wrong output device

Check **Settings > Module Routing** and the tab's bus selector. Sequences and Chainers inherit routing from Scheduled playback or the playlist that plays them.

## 15. Glossary

- **Output bus:** an audio route associated with a playback device.
- **Playlist:** a sequence of tracks that can be played manually.
- **Scheduled/Scheduler:** playback automated by day and time.
- **Sequence:** a group of audio tracks played as one unit.
- **Cart:** a button that directly triggers audio.
- **Chainer:** audio inserted between playlist or Scheduled tracks.
- **Mix:** audio or an announcement associated with a playlist track according to its settings.
- **Fade/Crossfade:** a gradual transition between audio items.
