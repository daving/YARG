# YARG Gamenight Fork

This is a small, party-focused fork of [YARG](https://github.com/YARC-Official/YARG) for use with a local Gamenight Rockband web portal.

The base game is still YARG. This fork adds LAN control/status hooks and a song rating-notes display so guests can browse, queue, and understand songs from a separate web interface.

## What This Fork Adds

- Reads a `gamenight-yarg.ini` file next to `YARG.exe`.
- Polls a Gamenight server for queued song commands.
- Reports whether the Music Library is open in Quickplay.
- Reports song start and stop events to the Gamenight server.
- Posts current song and genre updates to a configured Home Assistant webhook.
- Starts queued songs only when YARG is on the Quickplay Music Library screen.
- Adds a `ratingnotes` song metadata field.
- Shows rating notes from the song rating badge in the Music Library sidebar.
- Keeps rating notes in YARG's song cache, with a cache version bump so songs rescan cleanly.

The Gamenight server can then use YARG's status/events for queue controls, current-song display, and optional Home Assistant automation.

## Runtime Config

YARG reads this file from the folder containing `YARG.exe`:

```ini
[Server]
CommunicationEnabled=true
ServerBaseUrl=

[HomeAssistant]
HomeAssistantEnabled=true
HomeAssistantWebhookUrl=
HomeAssistantCurrentSongEntityId=input_text.yarg_currentsong
HomeAssistantCurrentGenreEntityId=input_text.yarg_currentgenre
```

Set `ServerBaseUrl` to the base URL of your Gamenight server, for example:

```ini
[Server]
CommunicationEnabled=true
ServerBaseUrl=http://your-server-name

[HomeAssistant]
HomeAssistantEnabled=true
HomeAssistantWebhookUrl=http://homeassistant.local:8123/api/webhook/your_webhook_id
HomeAssistantCurrentSongEntityId=input_text.yarg_currentsong
HomeAssistantCurrentGenreEntityId=input_text.yarg_currentgenre
```

`CommunicationEnabled=false` disables both the Gamenight server link and Home Assistant webhook calls.
Leave `ServerBaseUrl` blank to disable only the Gamenight server link. Leave
`HomeAssistantWebhookUrl` blank, or set `HomeAssistantEnabled=false`, to disable only Home Assistant.

No real server or webhook URL is committed in this repository.

When a song starts, YARG posts two Home Assistant webhook calls in order:

```json
{ "event": "song-started", "entity_id": "input_text.yarg_currentgenre", "value": "Alternative", "title": "Song Title", "genre": "Alternative" }
{ "event": "song-started", "entity_id": "input_text.yarg_currentsong", "value": "Song Title", "title": "Song Title", "genre": "Alternative" }
```

When playback stops, it posts the same two entities in the same genre-then-song order with blank values. A Home Assistant automation can use `trigger.json.entity_id` and `trigger.json.value` to call `input_text.set_value`, or map those values into whatever helper entities you prefer.

## Expected Server API

This fork expects the Gamenight server to provide these endpoints:

```text
GET  /api/rockband/yarg/commands
POST /api/rockband/yarg/status
POST /api/rockband/yarg/events
```

Command polling expects queued play commands from the server. Status/events let the server know whether Quickplay is active and what song has started or stopped.

## Rating Notes

This fork adds support for a string metadata field named `ratingnotes`.

For unpacked songs, add it to the `[song]` section of `song.ini`:

```ini
[song]
name = Example Song
artist = Example Artist
rating = 0
ratingnotes = Mild language in the second verse; otherwise party-safe.
```

For `.sng` songs, the same key needs to be added to the embedded song metadata/INI data inside the `.sng` package:

```ini
ratingnotes = Mild language in the second verse; otherwise party-safe.
```

Simple single-line notes are recommended. If you need line breaks, use escaped newlines:

```ini
ratingnotes = Verse 2 has mild language.\nLong guitar solo near the end.
```

In YARG, select a song in the Music Library and click the circular rating badge, or press the `Select` menu action, to open the rating notes dialog. If no notes are present, YARG shows a simple "No rating notes" message.

After editing song metadata, rescan songs in YARG if the old song cache is still being used.

## Building

Double-click:

```text
BUILD-GAMENIGHT-YARG.bat
```

or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-gamenight-yarg.ps1
```

The build script restores dependencies, runs Unity compile/import, and attempts to build a Windows player through `Assets/Editor/GamenightBuild.cs`.

The generated Windows build includes `gamenight-yarg.ini`; edit that file next to `YARG.exe` for your server.

## Installing These Changes Into Another YARG Checkout

If you already have a clean YARG source checkout, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-gamenight-yarg.ps1 -YargSourcePath "C:\Path\To\YARG"
```

Then build from the target checkout:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-gamenight-yarg.ps1
```

## Repositories

This fork also uses a matching YARG.Core fork so the `ratingnotes` metadata changes are available through the submodule:

- Main fork: `daving/YARG`
- Core fork: `daving/YARG.Core`

Clone with submodules:

```powershell
git clone --recursive https://github.com/daving/YARG.git
```

## License

YARG is licensed under the GNU Lesser General Public License v3.0 or later. This fork keeps the upstream license intact. See [`LICENSE`](LICENSE).

For general YARG usage, contribution, platform, and community information, see the upstream project: [YARC-Official/YARG](https://github.com/YARC-Official/YARG).
