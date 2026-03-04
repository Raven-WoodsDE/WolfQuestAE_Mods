using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using WolfQuestEp3;
using SharedCommons;

namespace AltSmells
{
    [BepInPlugin("com.rw.altsmells", "AltSmells", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        class SmellProps
        {
            public string speciesName;
            public Texture2D icon;
        }

        SmellProps[]
            smells;

        Texture2D
            wolfScent, pupScent, denScent, hunterScent, carcassScent, scentPostScent, rendevouzSiteScent;

        private const float
            iconOffset = .8f,
            MaxRenderTime = 4f,
            MaxRenderDistanceIcon = 700f,
            iconSize = 20f;


        // Optimized Scent Tracking
        public static HashSet<TerritoryMarking> ScentPosts = new HashSet<TerritoryMarking>();
        public static HashSet<Carcass> Carcasses = new HashSet<Carcass>();
        public static HashSet<Toy> Toys = new HashSet<Toy>();
        public static HashSet<HomeSite> Dens = new HashSet<HomeSite>();
        public static HashSet<HuntingZoneHunter> Hunters = new HashSet<HuntingZoneHunter>();

        // GUI
        Texture2D icon;
        Rect rect;
        float renderTimerAlpha;
        Vector3 worldPos, screenPos;
        GameObject sndSniff;

        public static AudioClip sniffClip;
        public static AudioSource audioSource;

        public static ConfigEntry<bool>
            enableSniffSound,
            disableDefaultScentSystem;

        public static ConfigEntry<KeyCode>
            sniffKey;

        public static ConfigEntry<bool>
            showAnimals, showDens, showHunters, showToys, showCarcasses, showScentPosts;

        private void Awake()
        {
            enableSniffSound = Config.Bind<bool>("General", "Enable Sniff Sound", true, "");
            disableDefaultScentSystem = Config.Bind<bool>("General", "Disable Default Scent View", true, "");

            sniffKey = Config.Bind<KeyCode>("General", "Sniff Key", KeyCode.V, "Key to sniff");

            showAnimals = Config.Bind<bool>("Scents", "Animals", true, "");
            showDens = Config.Bind<bool>("Scents", "Dens", true, "");
            showHunters = Config.Bind<bool>("Scents", "Hunters", true, "");
            showToys = Config.Bind<bool>("Scents", "Toys", true, "");
            showCarcasses = Config.Bind<bool>("Scents", "Carcasses", true, "");
            showScentPosts = Config.Bind<bool>("Scents", "Scent Posts", true, "");

            Harmony.CreateAndPatchAll(typeof(Plugin));

            if (disableDefaultScentSystem.Value)
            {
                Harmony.CreateAndPatchAll(typeof(Patches));
            }

            LoadAssets();

            if (enableSniffSound.Value)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            rect = new Rect(0, 0, iconSize, iconSize);
            worldPos = Vector3.zero;
            screenPos = Vector3.zero;
            Globals.Log("[AltSmells] Initialised.");
        }

        IEnumerator LoadAudio()
        {
            string path = Path.Combine(Paths.PluginPath, "altsmells_assets", "sniff.ogg");
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    Globals.Log(www.error);
                }
                else
                {
                    sniffClip = DownloadHandlerAudioClip.GetContent(www);
                }
            }
        }

        private void LoadAssets()
        {
            if (enableSniffSound.Value)
            {
                StartCoroutine(LoadAudio());
            }

            try
            {
                byte[] fileData = null;
                List<string> assets = new List<string>();
                string folderPath = Path.Combine(Paths.PluginPath, "altsmells_assets");
                string[] filesInFolder = Directory.GetFiles(folderPath);
                foreach (string file in filesInFolder)
                {
                    if (file.EndsWith(".png"))
                        assets.Add(file);
                }

                smells = new SmellProps[assets.Count];
                for (int i = 0; i < assets.Count; i++)
                {
                    smells[i] = new SmellProps();
                    // Fix: Use GetFileNameWithoutExtension to only get the filename ("hare")
                    // instead of the full path which might contain folder names like "Shared" or "Downloads"
                    smells[i].speciesName = Path.GetFileNameWithoutExtension(assets[i]).ToLowerInvariant();

                    fileData = File.ReadAllBytes(assets[i]);
                    smells[i].icon = new Texture2D(16, 16);
                    smells[i].icon.LoadImage(fileData, false);

                    Globals.Log($"[AltSmells] Loaded icon for species: {smells[i].speciesName}");
                }

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "carcass.png"));
                carcassScent = new Texture2D(16, 16);
                carcassScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "hunter.png"));
                hunterScent = new Texture2D(16, 16);
                hunterScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "wolf.png"));
                wolfScent = new Texture2D(16, 16);
                wolfScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "pup.png"));
                pupScent = new Texture2D(16, 16);
                pupScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "den.png"));
                denScent = new Texture2D(16, 16);
                denScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "rendevouzsite.png"));
                rendevouzSiteScent = new Texture2D(16, 16);
                rendevouzSiteScent.LoadImage(fileData, false);

                fileData = File.ReadAllBytes(Path.Combine(folderPath, "scentpost.png"));
                scentPostScent = new Texture2D(16, 16);
                scentPostScent.LoadImage(fileData, false);

                assets.Clear();
            }
            catch (System.Exception error)
            {
                Globals.Log(error.Message);
            }
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;

            renderTimerAlpha -= Time.deltaTime;
            if (renderTimerAlpha < -1) renderTimerAlpha = -1;

            if (Input.GetKeyDown(sniffKey.Value))
            {
                if (renderTimerAlpha <= 0)
                {
                    if (enableSniffSound.Value)
                    {
                        audioSource.clip = sniffClip;
                        audioSource.Play();
                    }

                    renderTimerAlpha = MaxRenderTime;
                }
            }
        }

        void OnGUI()
        {
            if (Globals.MenuIsOpen) return;
            if (renderTimerAlpha <= 0) return;
            if (Globals.animalManager == null) return;

            if (showAnimals.Value)
            {
                foreach (Animal x in Globals.animalManager.LivingAnimals)
                {
                    if (x == null) continue;

                    worldPos = x.Position + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        GUI.color = Color.white;
                        if (x.IsWolf)
                        {
                            if (x.IsPlayer) continue;

                            // Check for Dispersal (Black color)
                            Color packColor = x.Pack.TerritoryColor;
                            if (packColor.r + packColor.g + packColor.b < 0.1f)
                            {
                                GUI.color = Color.white;
                            }
                            else
                            {
                                GUI.color = packColor;
                            }

                            if (x.WolfDef.lifeStage >= WolfLifeStage.Yearling)
                            {
                                icon = wolfScent;
                            }
                            else
                            {
                                icon = pupScent;
                            }
                        }
                        else
                        {
                            icon = GetIconFromSpecies(x.Species.name);
                        }

                        if (icon != null) DrawIcon(icon);
                    }
                }
            }

            if (showHunters.Value)
            {
                foreach (HuntingZoneHunter x in Hunters)
                {
                    if (x == null) continue;

                    worldPos = x.transform.position + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        if (hunterScent != null)
                        {
                            GUI.color = Color.red;
                            DrawIcon(hunterScent);
                        }
                    }
                }
            }

            if (showToys.Value)
            {
                foreach (Toy x in Toys)
                {
                    if (x == null) continue;

                    worldPos = x.Position + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        GUI.color = Color.cyan;
                        DrawIcon(hunterScent); // Note: Original code used hunterScent for toys? keeping it same.
                    }
                }
            }

            if (showDens.Value)
            {
                foreach (HomeSite den in Dens)
                {
                    if (den == null) continue;

                    if (Globals.animalManager.LocalPlayer.Pack.homeSite != null)
                    {
                        if (den != Globals.animalManager.LocalPlayer.Pack.homeSite)
                        {
                            continue;
                        }
                    }

                    worldPos = den.VisualPosition + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        GUI.color = Color.white;
                        if (den.homeSiteType == HomeSiteType.RendezvousNPC ||
                            den.homeSiteType == HomeSiteType.RendezvousPlayer)
                        {
                            DrawIcon(rendevouzSiteScent);
                        }
                        else
                        {
                            DrawIcon(denScent);
                        }
                    }
                }
            }

            if (showCarcasses.Value)
            {
                foreach (Carcass x in Carcasses)
                {
                    if (x == null) continue;

                    worldPos = x.Position + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        GUI.color = Color.white;
                        DrawIcon(carcassScent);
                    }
                }
            }

            if (showScentPosts.Value)
            {
                foreach (TerritoryMarking x in ScentPosts)
                {
                    if (x == null) continue;

                    worldPos = x.transform.position + Vector3.up * iconOffset;
                    screenPos = Camera.main.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                    {
                        if (x.Pack != null)
                        {
                            GUI.color = x.Pack.TerritoryColor;
                        }
                        else
                        {
                            GUI.color = Color.white;
                        }

                        DrawIcon(scentPostScent);
                    }
                }
            }
        }

        void DrawIcon(Texture2D icon)
        {
            GUI.color *= Mathf.Min(1.0f, renderTimerAlpha);
            rect.x = screenPos.x - (iconSize * .5f);
            rect.y = Screen.height - screenPos.y - (iconSize * .5f);
            rect.width = rect.height = iconSize;
            GUI.DrawTexture(rect, icon);
        }

        Texture2D GetIconFromSpecies(string n)
        {
            string species = n.ToLowerInvariant();

            // First pass: look for exact match
            for (int i = 0; i < smells.Length; i++)
            {
                if (smells[i].speciesName == species)
                {
                    return smells[i].icon;
                }
            }

            // Second pass: look for partial match (fallback)
            for (int i = 0; i < smells.Length; i++)
            {
                if (species.Contains(smells[i].speciesName) || smells[i].speciesName.Contains(species))
                {
                    return smells[i].icon;
                }
            }

            return null;
        }

        [HarmonyPatch(typeof(InstantiationHandle), "Awake")]
        [HarmonyPostfix]
        public static void InstantiationHandle_Awake_Postfix(InstantiationHandle __instance)
        {
            if (__instance is TerritoryMarking tm)
            {
                ScentPosts.Add(tm);
            }
            else if (__instance is HomeSite hs)
            {
                Dens.Add(hs);
            }
            else if (__instance is Carcass c)
            {
                Carcasses.Add(c);
            }
            else if (__instance is Toy t)
            {
                Toys.Add(t);
            }
        }

        [HarmonyPatch(typeof(InstantiationHandle), "OnDisable")]
        [HarmonyPostfix]
        public static void InstantiationHandle_OnDisable_Postfix(InstantiationHandle __instance)
        {
            if (__instance is TerritoryMarking tm)
            {
                ScentPosts.Remove(tm);
            }
            else if (__instance is HomeSite hs)
            {
                Dens.Remove(hs);
            }
            else if (__instance is Carcass c)
            {
                Carcasses.Remove(c);
            }
            else if (__instance is Toy t)
            {
                Toys.Remove(t);
            }
        }

        [HarmonyPatch(typeof(HuntingZoneHunter), "Spawn")]
        [HarmonyPostfix]
        public static void HuntingZoneHunter_Spawn_Postfix(HuntingZoneHunter __instance)
        {
            Hunters.Add(__instance);
        }

        [HarmonyPatch(typeof(HuntingZoneHunter), "Despawn")]
        [HarmonyPostfix]
        public static void HuntingZoneHunter_Despawn_Postfix(HuntingZoneHunter __instance)
        {
            Hunters.Remove(__instance);
        }
    }
}

