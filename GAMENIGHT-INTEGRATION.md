# Gamenight YARG integration

This source tree is a modified YARG checkout for the Gamenight Rockband page.

## Runtime settings

On startup, YARG reads `gamenight-yarg.ini` from the folder above `YARG_Data`.
If the file is missing, the integration creates one with:

```ini
[Server]
CommunicationEnabled=true
ServerBaseUrl=

[HomeAssistant]
HomeAssistantEnabled=true
HomeAssistantWebhookUrl=
HomeAssistantNowPlayingEntityId=input_boolean.yarg_nowplaying
HomeAssistantCurrentGenreEntityId=input_text.yarg_currentgenre
```

Set `ServerBaseUrl` to your Gamenight server URL. Set `HomeAssistantWebhookUrl`
to the LAN webhook URL for Home Assistant, such as
`http://homeassistant.local:8123/api/webhook/your_webhook_id`.

`CommunicationEnabled=false` disables both the Gamenight server link and Home
Assistant webhook calls. Leave `ServerBaseUrl` blank to disable only the
Gamenight server link. Leave `HomeAssistantWebhookUrl` blank, or set
`HomeAssistantEnabled=false`, to disable only Home Assistant.

## Integration behavior

- Polls `GET /api/rockband/yarg/commands` for play commands.
- Posts Quickplay status to `POST /api/rockband/yarg/status`.
- Posts song start/stop events to `POST /api/rockband/yarg/events`.
- Posts Home Assistant webhook updates for `input_text.yarg_currentgenre` first,
  then `input_boolean.yarg_nowplaying`; on stop, the genre is posted blank and
  the now-playing boolean is posted as `false` in the same order.
- Writes `gamenight-yarg.log` next to the INI with Home Assistant webhook
  attempts and their HTTP results.
- Starts a queued song only when the YARG music library is open in Quickplay mode.

## Build from this modified checkout

Double-click:

```text
BUILD-GAMENIGHT-YARG.bat
```

or run from PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-gamenight-yarg.ps1
```

The script:

- restores the `YARG.Core` submodule;
- installs the user-local .NET 9 runtime if NuGetForUnity needs it;
- installs/restores NuGetForUnity packages;
- runs Unity compile/import;
- attempts a Windows player build through `Assets/Editor/GamenightBuild.cs`.

If the automated Unity player build does not create `YARG.exe`, open this folder in
Unity Hub, let it finish importing, then use File > Build Profiles and build for
Windows manually. Copy `gamenight-yarg.ini` next to the built `YARG.exe`.

## Install into another YARG checkout

If you already have a clean YARG source checkout, unzip this package and run:

```text
INSTALL-INTO-YARG-SOURCE.bat
```

or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-gamenight-yarg.ps1 -YargSourcePath "C:\Path\To\YARG"
```

Then run this from inside that YARG checkout:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-gamenight-yarg.ps1
```
