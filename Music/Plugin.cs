using BepInEx;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace RavenMusic
{
    [BepInPlugin("com.rw.ravenmusic", "RavenMusic", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        // --- CONFIG ---
        private BepInEx.Configuration.ConfigEntry<float> _cfgMinSilence;
        private BepInEx.Configuration.ConfigEntry<float> _cfgMaxSilence;
        private BepInEx.Configuration.ConfigEntry<float> _cfgVolume;
        private BepInEx.Configuration.ConfigEntry<bool> _cfgEnabled;
        private BepInEx.Configuration.ConfigEntry<string> _cfgMusicPack;

        // --- STATE ---
        private AudioSource _currentSource;
        private float _silenceTimer = 30f;
        private bool _isPlaying;
        private bool _isLoading;
        private GameObject _currentPlayerObj;

        // --- PLAYLIST LOGIC ---
        private List<string> _allFiles = new List<string>();
        private List<string> _remainingTracks = new List<string>();

        void Awake()
        {
            _cfgMinSilence = Config.Bind("General", "MinSilence", 180f, "Minimum silence between tracks (seconds)");
            _cfgMaxSilence = Config.Bind("General", "MaxSilence", 600f, "Maximum silence between tracks (seconds)");

            _cfgVolume = Config.Bind("General", "Volume", 0.25f, "Music volume (0.0 to 1.0)");
            _cfgVolume.Value = Mathf.Clamp(_cfgVolume.Value, 0f, 1f);

            _cfgEnabled = Config.Bind("General", "Enabled", true, "Whether music is enabled");
            _cfgMusicPack = Config.Bind("General", "MusicPack", "", "Subfolder within MusicPath to load");

            Globals.Log(
                $"[Music] Mod Awake. Enabled: {_cfgEnabled.Value}, Path: {Path.Combine(Paths.PluginPath, "music_assets")}, Pack: {_cfgMusicPack.Value}");

            try
            {
                // Ensure the root music folder exists
                if (!Directory.Exists(Path.Combine(Paths.PluginPath, "music_assets")))
                {
                    Directory.CreateDirectory(Path.Combine(Paths.PluginPath, "music_assets"));
                    Globals.Log($"[Music] Created directory: {Path.Combine(Paths.PluginPath, "music_assets")}");
                }
            }
            catch (System.Exception ex)
            {
                Globals.Log($"[Music] ERROR creating directories: {ex.Message}");
            }

            RefreshPlaylist();
        }

        void Update()
        {
            if (Globals.MenuIsOpen) return;
            if (!_cfgEnabled.Value) return;
            if (_isLoading) return;

            if (_isPlaying)
            {
                if (_currentSource == null || !_currentSource.isPlaying)
                {
                    Globals.Log("[Music] Track finished or source lost.");
                    OnTrackFinished();
                }
            }
            else
            {
                _silenceTimer -= Time.deltaTime;

                if (_silenceTimer <= 0)
                {
                    Globals.Log("[Music] Silence timer ended. Picking next track.");
                    PlayNextTrack();
                }
            }
        }

        void OnTrackFinished()
        {
            _isPlaying = false;
            _silenceTimer = Random.Range(_cfgMinSilence.Value, _cfgMaxSilence.Value);
        }

        void RefreshPlaylist()
        {
            _allFiles.Clear();
            _remainingTracks.Clear();

            string pack = _cfgMusicPack.Value;
            string fullpath = Path.Combine(Paths.PluginPath, "music_assets", pack);

            Globals.Log($"[Music] RefreshPlaylist. Path: {fullpath}, Pack: {pack}");

            if (Directory.Exists(fullpath))
            {
                var files = Directory.GetFiles(fullpath, "*.*")
                    .Where(s => s.EndsWith(".ogg") || s.EndsWith(".wav") || s.EndsWith(".mp3"))
                    .ToList();
                _allFiles = new List<string>(files);
                Globals.Log($"[Music] Found {_allFiles.Count} tracks in {fullpath}");
            }
            else
            {
                Globals.Log($"[Music] Directory NOT FOUND: {fullpath}");
            }

            if (_allFiles.Count > 0)
            {
                _remainingTracks = new List<string>(_allFiles);
                Globals.Log($"[Music] Playlist refreshed. Total tracks: {_allFiles.Count}");
            }
            else
            {
                Globals.Log("[Music] Playlist is EMPTY. No tracks found.");
            }
        }

        void PlayNextTrack()
        {
            if (_remainingTracks.Count == 0) RefreshPlaylist();
            if (_remainingTracks.Count == 0)
            {
                _silenceTimer = 30f;
                return;
            }

            int index = Random.Range(0, _remainingTracks.Count);
            string trackPath = _remainingTracks[index];
            _remainingTracks.RemoveAt(index);

            StartCoroutine(LoadAndStream(trackPath));
        }

        IEnumerator LoadAndStream(string path)
        {
            Globals.Log($"[Music] Loading: {Path.GetFileName(path)}");

            _isLoading = true; // <--- LOCK THE UPDATE LOOP

            string url = "file://" + path;
            using (WWW www = new WWW(url))
            {
                yield return www;

                if (!string.IsNullOrEmpty(www.error))
                {
                    Globals.Log($"[Music] Error: {www.error}");
                    _isLoading = false; // Unlock
                    OnTrackFinished(); // Treat error as finished so timer resets
                }
                else
                {
                    AudioClip clip = www.GetAudioClip(false, true, AudioType.UNKNOWN);
                    if (clip != null)
                    {
                        while (clip.loadState == AudioDataLoadState.Loading) yield return null;

                        if (clip.loadState == AudioDataLoadState.Loaded)
                        {
                            clip.name = Path.GetFileNameWithoutExtension(path);
                            SpawnAudioSource(clip);

                            // NOW we are officially playing
                            _isPlaying = true;
                        }
                    }
                }
            }

            _isLoading = false; // <--- UNLOCK THE UPDATE LOOP
        }

        void SpawnAudioSource(AudioClip clip)
        {
            _currentPlayerObj = new GameObject("RavenMusic_" + clip.name);

            if (Camera.main != null)
                _currentPlayerObj.transform.position = Camera.main.transform.position;

            AudioSource source = _currentPlayerObj.AddComponent<AudioSource>();
            _currentSource = source;
            source.clip = clip;
            source.volume = _cfgVolume.Value;
            source.spatialBlend = 0f;
            source.Play();

            Destroy(_currentPlayerObj, clip.length + 0.5f);
            Globals.Log($"[Music] Now Playing: {clip.name}");
        }

        void StopCurrentTrack()
        {
            if (_currentPlayerObj != null)
            {
                Destroy(_currentPlayerObj);
                _currentPlayerObj = null;
            }

            _isPlaying = false;
            _isLoading = false;
        }

        void OnGUI()
        {
            // Button style
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 16;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.alignment = TextAnchor.MiddleCenter;

            // Set colors based on state
            string buttonText;
            Color bgColor;

            if (_cfgEnabled.Value)
            {
                buttonText = "♪ ON";
                bgColor = new Color(0.3f, 0.7f, 0.25f, 0.85f); // Green
            }
            else
            {
                buttonText = "♪ OFF";
                bgColor = new Color(0.7f, 0.3f, 0.25f, 0.85f); // Red
            }

            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;

            // Draw button
            GUI.backgroundColor = bgColor;
            if (GUI.Button(new Rect(10, 10, 70, 35), buttonText, buttonStyle))
            {
                ToggleMusic();
                // BepInEx handles saving config values
            }

            GUI.backgroundColor = Color.white;
        }

        void ToggleMusic()
        {
            _cfgEnabled.Value = !_cfgEnabled.Value;

            if (!_cfgEnabled.Value)
            {
                StopCurrentTrack();
            }
            else
            {
                _silenceTimer = Random.Range(5f, 15f); // Start playing soon after enabling
            }
        }
    }
}