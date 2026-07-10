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

        public static GamenightServerClient Instance { get; private set; }

        private GamenightIni _settings;
        private string _lastSongKey;
        private bool _lastPlaying;
        private bool _quickplayActive;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _settings = GamenightIni.Load();
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
            if (!Instance._settings.CommunicationEnabled)
            {
                return;
            }

            Instance.StartCoroutine(Instance.PostStatus(active, null));
        }

        private IEnumerator PollCommands()
        {
            while (enabled)
            {
                if (_settings.CommunicationEnabled)
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
            if (!_settings.CommunicationEnabled)
            {
                return;
            }

            if (state.CurrentScene == SceneIndex.Gameplay && state.SongEntry != null)
            {
                var key = state.SongEntry.ActualLocation;
                if (!_lastPlaying || !string.Equals(_lastSongKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lastPlaying = true;
                    _lastSongKey = key;
                    StartCoroutine(PostEvent("song-started", state.SongEntry));
                }
            }
            else if (_lastPlaying)
            {
                _lastPlaying = false;
                _lastSongKey = "";
                StartCoroutine(PostEvent("song-ended", null));
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
            if (!_settings.CommunicationEnabled)
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

        private string Url(string path)
        {
            return _settings.ServerBaseUrl.TrimEnd('/') + path;
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

        private class GamenightIni
        {
            public bool CommunicationEnabled = true;
            public string ServerBaseUrl = string.Empty;

            public static GamenightIni Load()
            {
                var path = GetPath();
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "[Server]\r\nCommunicationEnabled=true\r\nServerBaseUrl=\r\n");
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
                }

                if (string.IsNullOrWhiteSpace(ini.ServerBaseUrl))
                {
                    ini.CommunicationEnabled = false;
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
