using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Menu;
using YARG.Menu.MusicLibrary;
using YARG.Player;
using YARG.Song;

namespace YARG.Integration
{
    public class GamenightServerClient : MonoBehaviour
    {
        private const float PollSeconds = 1.0f;
        private const string DiagnosticLogFileName = "gamenight-yarg.log";

        public static GamenightServerClient Instance { get; private set; }

        private GamenightIni _settings;
        private string _lastSongKey;
        private bool _lastPlaying;
        private bool _lastPaused;
        private bool _quickplayActive;

        private bool ServerCommunicationEnabled =>
            _settings.CommunicationEnabled && !string.IsNullOrWhiteSpace(_settings.ServerBaseUrl);

        private bool HomeAssistantCommunicationEnabled =>
            _settings.CommunicationEnabled &&
            _settings.HomeAssistantEnabled &&
            !string.IsNullOrWhiteSpace(_settings.HomeAssistantWebhookUrl);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _settings = GamenightIni.Load();
            WriteDiagnostic($"Started. Home Assistant enabled={HomeAssistantCommunicationEnabled}; webhook={_settings.HomeAssistantWebhookUrl}");
        }

        private void OnEnable()
        {
            GameStateFetcher.GameStateChange += OnGameStateChange;
            StartCoroutine(PollCommands());
        }

        private void OnDisable()
        {
            GameStateFetcher.GameStateChange -= OnGameStateChange;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void SetQuickplayActive(bool active)
        {
            if (Instance == null || Instance._quickplayActive == active)
            {
                return;
            }

            Instance._quickplayActive = active;
            if (!Instance.ServerCommunicationEnabled)
            {
                return;
            }

            Instance.StartCoroutine(Instance.PostStatus(active, null));
        }

        private IEnumerator PollCommands()
        {
            while (enabled)
            {
                if (ServerCommunicationEnabled)
                {
                    using var request = UnityWebRequest.Get(Url("/api/rockband/yarg/commands"));
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var commands = JsonConvert.DeserializeObject<List<GamenightCommand>>(request.downloadHandler.text) ?? new List<GamenightCommand>();
                        foreach (var command in commands)
                        {
                            if (string.Equals(command.action, "play", StringComparison.OrdinalIgnoreCase))
                            {
                                PlaySong(command);
                            }
                        }
                    }
                }

                yield return new WaitForSeconds(PollSeconds);
            }
        }

        private void PlaySong(GamenightCommand command)
        {
            if (!_quickplayActive)
            {
                return;
            }

            var song = FindSong(command.songPath);
            if (song == null)
            {
                YargLogger.LogFormatWarning("Gamenight could not find queued song path: {0}", command.songPath);
                return;
            }

            if (PlayerContainer.Players.Count <= 0)
            {
                YargLogger.LogWarning("Gamenight play command ignored because no players are active.");
                return;
            }

            MusicLibraryMenu.ResetMainLibraryIndex();
            MusicLibraryMenu.SetReload(MusicLibraryReloadState.Partial);

            GlobalVariables.State.CurrentSong = song;
            GlobalVariables.State.ShowSongs.Clear();
            GlobalVariables.State.ShowSongs.Add(song);
            GlobalVariables.State.PlayingAShow = false;

            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);
        }

        private SongEntry FindSong(string songPath)
        {
            if (string.IsNullOrWhiteSpace(songPath))
            {
                return null;
            }

            var normalized = NormalizePath(songPath);
            return SongContainer.Songs.FirstOrDefault(song =>
                string.Equals(NormalizePath(song.ActualLocation), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizePath(song.SortBasedLocation), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private void OnGameStateChange(GameStateFetcher.State state)
        {
            if (!ServerCommunicationEnabled && !HomeAssistantCommunicationEnabled)
            {
                return;
            }

            if (state.CurrentScene == SceneIndex.Gameplay && state.SongEntry != null)
            {
                var key = state.SongEntry.ActualLocation;
                var isPlaying = !state.Paused;
                if (!_lastPlaying || !string.Equals(_lastSongKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lastPlaying = true;
                    _lastPaused = state.Paused;
                    _lastSongKey = key;
                    WriteDiagnostic($"Song started: {state.SongEntry.Name.Original}");
                    StartCoroutine(PostEvent("song-started", state.SongEntry));
                    StartCoroutine(PostHomeAssistantSongStarted(state.SongEntry, isPlaying));
                }
                else if (_lastPaused != state.Paused)
                {
                    _lastPaused = state.Paused;
                    WriteDiagnostic(state.Paused ? "Song paused." : "Song resumed.");
                    StartCoroutine(PostHomeAssistantNowPlaying(state.SongEntry, isPlaying, state.Paused ? "song-paused" : "song-resumed"));
                }
            }
            else if (_lastPlaying)
            {
                _lastPlaying = false;
                _lastPaused = false;
                _lastSongKey = "";
                WriteDiagnostic("Song ended.");
                StartCoroutine(PostEvent("song-ended", null));
                StartCoroutine(PostHomeAssistantSongEnded());
            }
        }

        private IEnumerator PostStatus(bool quickplayActive, bool? isPlaying)
        {
            var json = JsonConvert.SerializeObject(new GamenightStatus
            {
                quickplayActive = quickplayActive,
                isPlaying = isPlaying
            });
            yield return PostJson("/api/rockband/yarg/status", json);
        }

        private IEnumerator PostEvent(string type, SongEntry song)
        {
            var json = JsonConvert.SerializeObject(new GamenightEvent
            {
                type = type,
                songPath = song?.ActualLocation ?? "",
                title = song?.Name.Original ?? "",
                genre = song?.Genre.Original ?? ""
            });
            yield return PostJson("/api/rockband/yarg/events", json);
        }

        private IEnumerator PostJson(string path, string json)
        {
            if (!ServerCommunicationEnabled)
            {
                yield break;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(Url(path), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
        }

        private IEnumerator PostHomeAssistantSongStarted(SongEntry song, bool isPlaying)
        {
            var title = song?.Name.Original ?? "";
            var genre = song?.Genre.Original ?? "";

            yield return PostHomeAssistantEntity(_settings.HomeAssistantCurrentGenreEntityId, genre, "song-started", title, genre);
            yield return PostHomeAssistantEntity(_settings.HomeAssistantNowPlayingEntityId, isPlaying, "song-started", title, genre);
        }

        private IEnumerator PostHomeAssistantNowPlaying(SongEntry song, bool isPlaying, string eventType)
        {
            var title = song?.Name.Original ?? "";
            var genre = song?.Genre.Original ?? "";
            yield return PostHomeAssistantEntity(_settings.HomeAssistantNowPlayingEntityId, isPlaying, eventType, title, genre);
        }

        private IEnumerator PostHomeAssistantSongEnded()
        {
            yield return PostHomeAssistantEntity(_settings.HomeAssistantCurrentGenreEntityId, "", "song-ended", "", "");
            yield return PostHomeAssistantEntity(_settings.HomeAssistantNowPlayingEntityId, false, "song-ended", "", "");
        }

        private IEnumerator PostHomeAssistantEntity(string entityId, object value, string eventType, string title, string genre)
        {
            if (!HomeAssistantCommunicationEnabled || string.IsNullOrWhiteSpace(entityId))
            {
                yield break;
            }

            var json = JsonConvert.SerializeObject(new HomeAssistantWebhookPayload
            {
                Event = eventType,
                EntityId = entityId,
                Value = value,
                Title = title,
                Genre = genre
            });

            var bytes = Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(_settings.HomeAssistantWebhookUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            WriteDiagnostic($"Home Assistant {eventType}: {entityId}={value}; result={request.result}; code={request.responseCode}; error={request.error ?? "none"}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                YargLogger.LogWarning($"Gamenight Home Assistant webhook failed for {entityId}: {request.error}");
            }
        }

        private string Url(string path)
        {
            return _settings.ServerBaseUrl.TrimEnd('/') + path;
        }

        private static void WriteDiagnostic(string message)
        {
            try
            {
                var installDirectory = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
                var path = Path.Combine(installDirectory, DiagnosticLogFileName);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never interfere with gameplay or integration calls.
            }
        }

        private static string NormalizePath(string path)
        {
            return (path ?? "").Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        }

        private class GamenightCommand
        {
            public string action;
            public string songPath;
        }

        private class GamenightEvent
        {
            public string type;
            public string songPath;
            public string title;
            public string genre;
        }

        private class GamenightStatus
        {
            public bool quickplayActive;
            public bool? isPlaying;
        }

        private class HomeAssistantWebhookPayload
        {
            [JsonProperty("event")]
            public string Event;

            [JsonProperty("entity_id")]
            public string EntityId;

            [JsonProperty("value")]
            public object Value;

            [JsonProperty("title")]
            public string Title;

            [JsonProperty("genre")]
            public string Genre;
        }

        private class GamenightIni
        {
            public bool CommunicationEnabled = true;
            public string ServerBaseUrl = string.Empty;
            public bool HomeAssistantEnabled = true;
            public string HomeAssistantWebhookUrl = string.Empty;
            public string HomeAssistantNowPlayingEntityId = "input_boolean.yarg_nowplaying";
            public string HomeAssistantCurrentGenreEntityId = "input_text.yarg_currentgenre";

            public static GamenightIni Load()
            {
                var path = GetPath();
                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "[Server]\r\n" +
                        "CommunicationEnabled=true\r\n" +
                        "ServerBaseUrl=\r\n" +
                        "\r\n" +
                        "[HomeAssistant]\r\n" +
                        "HomeAssistantEnabled=true\r\n" +
                        "HomeAssistantWebhookUrl=\r\n" +
                        "HomeAssistantNowPlayingEntityId=input_boolean.yarg_nowplaying\r\n" +
                        "HomeAssistantCurrentGenreEntityId=input_text.yarg_currentgenre\r\n");
                }

                var ini = new GamenightIni();
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("[") || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    var index = line.IndexOf('=');
                    if (index < 1)
                    {
                        continue;
                    }

                    var key = line.Substring(0, index).Trim();
                    var value = line.Substring(index + 1).Trim();
                    if (string.Equals(key, "CommunicationEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.CommunicationEnabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "ServerBaseUrl", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.ServerBaseUrl = value;
                    }
                    else if (string.Equals(key, "HomeAssistantEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.HomeAssistantEnabled = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(key, "HomeAssistantWebhookUrl", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.HomeAssistantWebhookUrl = value;
                    }
                    else if (string.Equals(key, "HomeAssistantNowPlayingEntityId", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.HomeAssistantNowPlayingEntityId = value;
                    }
                    else if (string.Equals(key, "HomeAssistantCurrentGenreEntityId", StringComparison.OrdinalIgnoreCase))
                    {
                        ini.HomeAssistantCurrentGenreEntityId = value;
                    }
                }

                return ini;
            }

            private static string GetPath()
            {
                var installDirectory = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
                return Path.Combine(installDirectory, "gamenight-yarg.ini");
            }
        }
    }
}
