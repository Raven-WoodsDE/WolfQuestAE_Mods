using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace Customizer
{
    [BepInPlugin("com.rw.customizer", "Customizer", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }

        // Logger for debugging
        internal static new ManualLogSource Logger;

        // Assets root folder
        public const string AssetRoot = "customizer_assets";

        // Biography cache
        private static Dictionary<string, CustomizationData> _biographyCache =
            new Dictionary<string, CustomizationData>();

        // Configuration entries
        private ConfigEntry<float> _howlVolume;
        private ConfigEntry<KeyCode> _toggleKey;

        // Audio cache for loaded voice clips (keyed by filename)
        private static Dictionary<string, AudioClip> _voiceCache = new Dictionary<string, AudioClip>();

        // Audio source for playing custom howls
        private static AudioSource _howlAudioSource;

        // Currently loading voices (to prevent duplicate loads)
        private static HashSet<string> _loadingVoices = new HashSet<string>();

        // Outfitter module
        private Outfitter _outfitter;

        // Property getters for patches
        public float HowlVolume => _howlVolume.Value;
        public KeyCode ToggleKey => _toggleKey.Value;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            // Setup configuration
            _howlVolume = Config.Bind(
                "Voice",
                "HowlVolume",
                1.0f,
                new ConfigDescription(
                    "Volume of the custom howl sound (0.0 to 1.0, values > 1.0 will boost)",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            _toggleKey = Config.Bind(
                "Outfitter",
                "ToggleKey",
                KeyCode.Keypad3,
                "Key to toggle the Transform Explorer / Outfitter window"
            );

            // Create required folders
            EnsureFoldersExist();

            // Apply Harmony patches
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(SkinsPatches));
            Harmony.CreateAndPatchAll(typeof(VoicePatches));

            // Initialize outfitter module
            _outfitter = gameObject.AddComponent<Outfitter>();

            Globals.Log("[Customizer] Initialised.");
        }

        private void EnsureFoldersExist()
        {
            string assetRoot = Path.Combine(Paths.PluginPath, AssetRoot);
            if (!Directory.Exists(assetRoot))
                Directory.CreateDirectory(assetRoot);

            string skinsFolder = Path.Combine(assetRoot, "Skins");
            string voicesFolder = Path.Combine(assetRoot, "Voices");
            string outfitsFolder = Path.Combine(assetRoot, "Outfits");
            string bundlesFolder = Path.Combine(assetRoot, "AssetBundles");

            if (!Directory.Exists(skinsFolder))
                Directory.CreateDirectory(skinsFolder);
            if (!Directory.Exists(voicesFolder))
                Directory.CreateDirectory(voicesFolder);
            if (!Directory.Exists(outfitsFolder))
                Directory.CreateDirectory(outfitsFolder);
            if (!Directory.Exists(bundlesFolder))
                Directory.CreateDirectory(bundlesFolder);
        }

        public static AudioSource GetHowlAudioSource(Transform followTransform)
        {
            if (_howlAudioSource == null)
            {
                var go = new GameObject("CustomWolf_HowlAudio");
                DontDestroyOnLoad(go);
                _howlAudioSource = go.AddComponent<AudioSource>();
                _howlAudioSource.spatialBlend = 1f; // 3D sound
                _howlAudioSource.rolloffMode = AudioRolloffMode.Linear;
                _howlAudioSource.minDistance = 5f;
                _howlAudioSource.maxDistance = 500f;
                _howlAudioSource.playOnAwake = false;
            }

            // Update position to follow the wolf
            if (followTransform != null)
            {
                _howlAudioSource.transform.position = followTransform.position;
            }

            return _howlAudioSource;
        }

        public void LoadCustomVoice(string filename, Action<AudioClip> onLoaded)
        {
            // Check cache first
            if (_voiceCache.TryGetValue(filename, out AudioClip cached))
            {
                onLoaded?.Invoke(cached);
                return;
            }

            // Don't start loading if already loading
            if (_loadingVoices.Contains(filename))
                return;

            StartCoroutine(LoadVoiceCoroutine(filename, onLoaded));
        }

        private IEnumerator LoadVoiceCoroutine(string filename, Action<AudioClip> onLoaded)
        {
            _loadingVoices.Add(filename);

            // Ensure .wav or .ogg extension
            string actualFilename = filename;
            if (!actualFilename.EndsWith(".wav") && !actualFilename.EndsWith(".ogg"))
                actualFilename += ".wav";

            string fullPath = Path.Combine(Paths.PluginPath, AssetRoot, "Voices", actualFilename);

            if (!File.Exists(fullPath))
            {
                Globals.Log($"Custom voice file not found: {fullPath}");
                _loadingVoices.Remove(filename);
                yield break;
            }

            // Determine audio type
            AudioType audioType = fullPath.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;

            string fileUrl = "file:///" + fullPath.Replace("\\", "/");
            using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(fileUrl, audioType))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                    clip.name = "CustomVoice_" + filename;
                    _voiceCache[filename] = clip;
                    Globals.Log($"Loaded custom voice: {filename} ({clip.length:F2}s)");
                    onLoaded?.Invoke(clip);
                }
            }

            _loadingVoices.Remove(filename);
        }

        public static AudioClip GetCachedVoice(string filename)
        {
            _voiceCache.TryGetValue(filename, out AudioClip clip);
            return clip;
        }

        public static void PlayCustomHowl(Transform wolfTransform, AudioClip clip, float animationDuration)
        {
            if (clip == null) return;

            var audioSource = GetHowlAudioSource(wolfTransform);
            audioSource.clip = clip;
            audioSource.volume = Instance.HowlVolume;
            audioSource.Play();

            // Follow the wolf during playback
            if (wolfTransform != null)
                Instance.StartCoroutine(FollowTransformWhilePlaying(audioSource, wolfTransform));
        }

        private static IEnumerator FollowTransformWhilePlaying(AudioSource source, Transform target)
        {
            while (source.isPlaying && target != null)
            {
                source.transform.position = target.position;
                yield return null;
            }
        }

        public static CustomizationData ParseBiography(string biography)
        {
            if (string.IsNullOrEmpty(biography))
                return new CustomizationData();

            if (_biographyCache.TryGetValue(biography, out CustomizationData cached))
                return cached;

            var data = new CustomizationData();

            try
            {
                string[] entries = biography.Split(';');
                foreach (string entry in entries)
                {
                    string trimmed = entry.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    string[] parts = trimmed.Split('=');
                    if (parts.Length < 2) continue;

                    string key = parts[0].Trim().ToLowerInvariant();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "skin":
                            data.SkinFile = value;
                            break;

                        case "eyes":
                            string[] rgb = value.Split(',');
                            if (rgb.Length == 3)
                            {
                                data.EyeColor = new Color(
                                    float.Parse(rgb[0]) / 255f,
                                    float.Parse(rgb[1]) / 255f,
                                    float.Parse(rgb[2]) / 255f
                                );
                                data.HasCustomEyes = true;
                            }

                            break;

                        case "voice":
                            data.VoiceFile = value;
                            break;

                        case "outfit":
                            data.OutfitName = value;
                            break;

                        case "size":
                            if (float.TryParse(value, out float size))
                            {
                                data.Size = size;
                            }

                            break;

                        case "injury":
                            data.Injury = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error parsing biography: {ex.Message}"); // Log is static helper
            }

            _biographyCache[biography] = data;
            return data;
        }

        public static void Log(string message)
        {
            Globals.Log(message);
        }
    }
}
