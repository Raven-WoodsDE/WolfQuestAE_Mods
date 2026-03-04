using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using SharedCommons;

namespace QualityOfLife
{
    [BepInPlugin("com.rw.qol", "QualityOfLife", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static string[] months =
        {
            "January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
            "November", "December"
        };

        public static Month[] monthEnums =
        {
            Month.January, Month.February, Month.March, Month.April, Month.May, Month.June, Month.July, Month.August,
            Month.September, Month.October, Month.November, Month.December
        };

        FieldInfo nextWeather;
        float tmrUpdate;

        // Settings Menu
        bool showSettingsMenu = false;
        Rect settingsWindowRect;
        Vector2 scrollPosition = Vector2.zero;
        bool windowInitialized = false;
        int currentTab = 0;
        string[] tabNames = { "QoL", "Date", "Teleporter", "Dangers" };

        // Runtime state
        string[] weatherNames =
        {
            "None", "Clear Sky", "Cloudy 1", "Cloudy 2", "Cloudy 3", "Foggy", "Light Rain", "Heavy Rain", "Light Snow",
            "Heavy Snow", "Storm", "Blizzard"
        };

        // WeatherType enum uses bit flags: None=0, ClearSky=2, Cloudy1=4, Cloudy2=8, etc.
        int[] weatherValues = { 0, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048 };

        // ShowDate Fields
        bool showDateUI;
        int daySlider;

        // Teleporter Fields
        private List<Waypoint> _waypoints = new List<Waypoint>();
        private string _inputName = "My Waypoint";
        private Vector2 _teleporterScrollPosition;

        // Pup Management Fields
        private string _savePath;

        // WorldWithoutDanger Fields
        public static List<string> BannedNames = new List<string>();

        public class Waypoint
        {
            public string name;
            public Vector3 position;
        }

        #region Configuration

        public static ConfigEntry<bool> CfgrwCooldownPatch;
        public static ConfigEntry<bool> CfgrwNoSlippingPatch;
        public static ConfigEntry<bool> CfgrwHealthUpdaterPatch;
        public static ConfigEntry<bool> CfgrwHungerAndThirstUpdaterPatch;
        public static ConfigEntry<bool> CfgrwDiscoverDensPatch;
        public static ConfigEntry<bool> CfgrwEatPatch;
        public static ConfigEntry<bool> CfgrwLostStatePatch;
        public static ConfigEntry<bool> CfgrwDropEverythingToSwimPatch;
        public static ConfigEntry<bool> CfgrwFleaPatch;
        public static ConfigEntry<bool> CfgrwQuickSleep;
        public static ConfigEntry<bool> CfgrwNoFallingDamage;
        public static ConfigEntry<bool> CfgrwNoTerritoryDecay;
        public static ConfigEntry<bool> CfgrwNoFleePrey;
        public static ConfigEntry<bool> CfgrwDisableStamina;
        public static ConfigEntry<bool> CfgrwDisableHealth;
        public static ConfigEntry<bool> CfgrwDisableSleep;
        public static ConfigEntry<bool> CfgrwDisableAffinity;
        public static ConfigEntry<bool> CfgrwDisableHunger;
        public static ConfigEntry<bool> CfgrwDisableRally;
        public static ConfigEntry<bool> CfgrwNoInjuries;
        public static ConfigEntry<bool> CfgrwNoScentPostRequirement;
        public static ConfigEntry<float> CfgrwTerritoryQualityMultiplier;
        public static ConfigEntry<int> CfgrwMaxLitterSize;
        public static ConfigEntry<int> CfgrwLockedWeather;
        public static ConfigEntry<bool> CfgrwImmortalPups;
        public static ConfigEntry<bool> CfgrwImmortalMate;
        public static ConfigEntry<bool> CfgrwImmortalPack;
        public static ConfigEntry<bool> CfgrwMaxFoodlPups;
        public static ConfigEntry<bool> CfgrwMaxFoodMate;

        public static ConfigEntry<bool> CfgrwMaxFoodPack;

        // ShowDate Config
        public static ConfigEntry<bool> CfgrwShowDateEnabled;

        // WorldWithoutDanger Config
        public static ConfigEntry<string> CfgrwBannedSpecies;

        // Scent Disable Config
        public static ConfigEntry<bool> CfgrwDisablePackScent;

        // Keybind Config
        public static ConfigEntry<KeyCode> CfgMenuToggleKey;
        public static ConfigEntry<KeyCode> CfgDateToggleKey;

        // Merged Plugins Config
        public static ConfigEntry<bool> CfgrwNoFloodedDens;
        public static ConfigEntry<bool> CfgrwNoHunters;

        // New QoL Features Config
        public static ConfigEntry<bool> CfgrwNoDeathByAge;
        public static ConfigEntry<bool> CfgrwAutoClaimTerritories;
        public static ConfigEntry<bool> CfgrwAutoUnclaimTerritories;
        public static ConfigEntry<bool> CfgrwPreventDispersalReturn;
        public static ConfigEntry<bool> CfgrwNonPausingDispersalReturnNotification;

        #endregion

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));

            CfgrwCooldownPatch = Config.Bind("Emote Cooldowns", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwCooldownPatch)); // Always apply, check at runtime

            CfgrwNoSlippingPatch = Config.Bind("No Slippery Slopes", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwNoSlippingPatch)); // Always apply, check at runtime

            CfgrwHealthUpdaterPatch = Config.Bind("Everything Dies", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwHealthUpdaterPatch)); // Always apply, check at runtime

            CfgrwHungerAndThirstUpdaterPatch = Config.Bind("Can always feed pups", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwHungerAndThirstUpdaterPatch)); // Always apply, check at runtime

            CfgrwDiscoverDensPatch = Config.Bind("Discover all Dens", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwDiscoverDensPatch)); // Always apply, check at runtime

            CfgrwEatPatch = Config.Bind("Eating Restores Health", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwEatPatch)); // Always apply, check at runtime

            CfgrwLostStatePatch = Config.Bind("No Lost pups", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwLostStatePatch)); // Always apply, check at runtime
            Harmony.CreateAndPatchAll(typeof(rwNoPupsLostPatch)); // Prevents pups from entering Lost state at the source
            Harmony.CreateAndPatchAll(typeof(rwNoLostInstantlyPatch)); // Prevents fudging "Unknown fate" kills

            CfgrwDropEverythingToSwimPatch = Config.Bind("No Drop-On-Swim", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwDropEverythingToSwimPatch)); // Always apply, check at runtime

            CfgrwFleaPatch = Config.Bind("Fleas Be Gone!", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwFleaPatch)); // Always apply, check at runtime

            CfgrwQuickSleep = Config.Bind("Quick Sleep", "", true, "");

            CfgrwNoFallingDamage = Config.Bind("No Falling Damage", "", true, "");
            Harmony.CreateAndPatchAll(typeof(rwTakeFallingDamagePatch)); // Always apply, check at runtime

            CfgrwNoTerritoryDecay = Config.Bind("No Territory Decay", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwTerritoryManagerPatch2)); // Always apply, check at runtime

            CfgrwNoFleePrey = Config.Bind("Disable prey fleeing", "", false, "");

            // These cheats work at runtime via ApplyRuntimeStats() - no Harmony patches needed
            CfgrwDisableStamina = Config.Bind("Disable Stamina", "", false, "");
            CfgrwDisableHealth = Config.Bind("Disable Health", "", false, "");
            CfgrwDisableSleep = Config.Bind("Disable Sleep", "", false, "");
            CfgrwDisableHunger = Config.Bind("Disable Hunger", "", false, "");
            CfgrwDisableAffinity = Config.Bind("Disable Affinity", "", false, "");

            CfgrwDisableRally = Config.Bind("Disable Rally", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwDisableRallyPatch)); // Always apply, check value at runtime
            Harmony.CreateAndPatchAll(typeof(rwDisableRallyAnimationPatch)); // Always apply, check value at runtime

            CfgrwNoInjuries = Config.Bind("No Injuries", "", false, "Prevent the player wolf from getting injured");
            Harmony.CreateAndPatchAll(typeof(rwNoInjuriesPatch)); // Always apply, check value at runtime

            CfgrwNoScentPostRequirement = Config.Bind("No Scent Post Requirement", "", false, "");
            Harmony.CreateAndPatchAll(typeof(rwNoScentPostRequirementPatch)); // Always apply, check value at runtime

            CfgrwTerritoryQualityMultiplier = Config.Bind("Territory Quality Multiplier", "", 1f, "");
            if (CfgrwTerritoryQualityMultiplier.Value != 1f)
                Harmony.CreateAndPatchAll(typeof(rwTerritoryQualityMultiplierPatch));

            CfgrwMaxLitterSize = Config.Bind("Max Litter Size", "", 0, "");
            if (CfgrwMaxLitterSize.Value > 0) Harmony.CreateAndPatchAll(typeof(rwMaxLitterSizePatch));

            CfgrwLockedWeather = Config.Bind("Locked Weather", "", 0, "");
            CfgrwImmortalPups = Config.Bind("Immortal Pups", "", false, "");
            CfgrwImmortalMate = Config.Bind("Immortal Mate", "", false, "");
            CfgrwImmortalPack = Config.Bind("Immortal Pack", "", false, "");
            CfgrwMaxFoodlPups = Config.Bind("Max Food Pups", "", false, "");
            CfgrwMaxFoodMate = Config.Bind("Max Food Mate", "", false, "");
            CfgrwMaxFoodPack = Config.Bind("Max Food Pack", "", false, "");

            // ShowDate Config
            CfgrwShowDateEnabled = Config.Bind("Show Date", "Enabled", true, "Show the current date on screen");

            // Scent Disable Config
            CfgrwDisablePackScent = Config.Bind("Disable Pack Scent", "", false,
                "Disable scent placement for your pack wolves to improve performance");
            Harmony.CreateAndPatchAll(typeof(rwAnimalScentHandlerPatch));

            // Keybind Config
            CfgMenuToggleKey = Config.Bind("Keybinds", "MenuToggleKey", KeyCode.Keypad4,
                "Key to toggle the QoL settings menu");
            CfgDateToggleKey = Config.Bind("Keybinds", "DateToggleKey", KeyCode.Keypad0,
                "Key to toggle the date display");

            // WorldWithoutDanger Config
            string defaultBans =
                "Grizzly, Cougar, Coyote, Bear, Bald Eagle, Golden Eagle, Raven, Lynx, Wolverine, Feral Dogs";
            CfgrwBannedSpecies = Config.Bind("World Without Danger", "BannedSpecies", defaultBans,
                "Comma-separated list of species names to prevent from spawning.");
            UpdateBanList();
            CfgrwBannedSpecies.SettingChanged += (sender, args) => UpdateBanList();

            Harmony.CreateAndPatchAll(typeof(rwTerritoryManagerPatch1));
            Harmony.CreateAndPatchAll(typeof(rwCameraPatch));
            Harmony.CreateAndPatchAll(typeof(rwNoScentPostNotificationPatch));
            Harmony.CreateAndPatchAll(typeof(rwTroubleshootingSceneManagerPatch));

            // Merged Plugins Config Init
            CfgrwNoFloodedDens = Config.Bind("Merged", "No Flooded Dens", true,
                "Prevents dens from flooding during heavy rain.");
            Harmony.CreateAndPatchAll(typeof(PatchNoFloodedDens));

            CfgrwNoHunters = Config.Bind("Merged", "No Hunters", true, "Prevents hunters from spawning.");
            Harmony.CreateAndPatchAll(typeof(PatchNoHunters));
            Harmony.CreateAndPatchAll(typeof(PatchNoHunters_Hunter));

            // New QoL Features Config Init
            CfgrwNoDeathByAge = Config.Bind("QoL", "No Death By Age", false,
                "Prevents death by old age (refreshed constantly).");
            CfgrwAutoClaimTerritories = Config.Bind("QoL", "Auto Claim Territories", false,
                "Automatically claims all territories every 5 seconds.");
            CfgrwAutoUnclaimTerritories = Config.Bind("QoL", "Auto Unclaim Territories", false,
                "Automatically unclaims all territories every 5 seconds.");
            CfgrwPreventDispersalReturn = Config.Bind("Pup Settings", "Prevent Dispersal Return", true,
                "Prevents dispersed pups from returning to the pack.");
            CfgrwNonPausingDispersalReturnNotification = Config.Bind("Pup Settings", "Non-pausing Return Alert", true,
                "Makes the returning pup notification non-pausing and corner-type.");

            Harmony.CreateAndPatchAll(typeof(rwPreventDispersalReturnPatch));
            Harmony.CreateAndPatchAll(typeof(rwNonPausingDispersalReturnNotificationPatch));

            // WorldWithoutDanger patches
            Harmony.CreateAndPatchAll(typeof(GatekeeperPatch));

            // Teleporter: Setup waypoints file
            _savePath = Path.Combine(Paths.ConfigPath, "RavenWaypoints.txt");
            if (!File.Exists(_savePath)) File.WriteAllText(_savePath, "");
            LoadWaypoints();

            daySlider = 1;

            nextWeather =
                typeof(WeatherManager).GetField("nextWeather", BindingFlags.NonPublic | BindingFlags.Instance);

            Globals.Log("[QoL] Initialised");
        }

        void Update()
        {
            if (Globals.MenuIsOpen)
            {
                showSettingsMenu = false;
                showDateUI = false;
                return;
            }

            if (Input.GetKeyDown(CfgMenuToggleKey.Value))
            {
                showSettingsMenu = !showSettingsMenu;

                InputControls.ForceAllowCursor = showSettingsMenu;
                InputControls.DisableCameraInput = showSettingsMenu;
                InputControls.DisableInput = showSettingsMenu;
            }

            // Toggle date display with configurable key (default: Numpad 0)
            if (Input.GetKeyDown(CfgDateToggleKey.Value))
            {
                showDateUI = !showDateUI;
            }

            tmrUpdate += Time.deltaTime;
            if (tmrUpdate > 1f)
            {
                tmrUpdate = 0;

                if (CfgrwAutoClaimTerritories.Value)
                    SetAllTerritoriesPlayer();

                if (CfgrwAutoUnclaimTerritories.Value)
                    SetAllTerritoriesUnowned();

                if (CfgrwNoDeathByAge.Value)
                {
                    Globals.animalManager.LocalPlayer.WolfDef.dieInSleepOdds = 0f;
                    Globals.animalManager.LocalPlayer.WolfDef.dieInSleepTriggered = false;
                }

                Globals.timeManager.realSecondsInGameHour.sleep = CfgrwQuickSleep.Value ? .1f : 1f;

                if (DevSceneSettings.Instance != null)
                {
                    DevSceneSettings.Instance.canAlwaysSleep =
                        true;

                    DevSceneSettings.Instance.preySetting =
                        CfgrwNoFleePrey.Value ? PreyDevSetting.AlwaysFight : PreyDevSetting.Normal;
                }

                ApplyPackSetting();
                ApplyRuntimeStats();
                ApplyMateStats();
                ApplyPupStats();
            }
        }

        void ApplyRuntimeStats()
        {
            if (Globals.animalManager.LocalPlayer != null)
            {
                // Infinite Stamina
                if (CfgrwDisableStamina.Value)
                    Globals.animalManager.LocalPlayer.State.Energy =
                        Globals.animalManager.LocalPlayer.State.MaxEnergy;

                // Infinite Health
                if (CfgrwDisableHealth.Value)
                    Globals.animalManager.LocalPlayer.State.Health =
                        Globals.animalManager.LocalPlayer.State.MaxHealth;

                // No Sleep Needed
                if (CfgrwDisableSleep.Value)
                    Globals.animalManager.LocalPlayer.State.Wakefulness = 1f;

                // Max Food
                if (CfgrwDisableHunger.Value)
                    Globals.animalManager.LocalPlayer.State.Food =
                        Globals.animalManager.LocalPlayer.State.MaxFoodWithoutRegurgitant;
            }

            // Max Affinity
            if (Globals.affinityManager != null)
            {
                if (CfgrwDisableAffinity.Value)
                    Globals.affinityManager.affinity = 100f;
            }
        }

        void ApplyMateStats()
        {
            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null)
                return;

            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate == null ||
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate.State.DeadOrDying)
                return;

            if (CfgrwImmortalMate.Value)
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate.State.Health =
                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate.State.MaxHealth;

            if (CfgrwMaxFoodMate.Value)
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate.State.Food =
                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate.State.MaxFoodWithoutRegurgitant;
        }

        void ApplyPupStats()
        {
            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null)
                return;

            foreach (Animal pup in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups)
            {
                if (pup == null || pup.State == null || pup.State.DeadOrDying)
                    continue;

                if (CfgrwImmortalPups.Value)
                    pup.State.Health = pup.State.MaxHealth;

                if (CfgrwMaxFoodlPups.Value)
                    pup.State.Food = pup.State.MaxFoodWithoutRegurgitant;
            }
        }

        #region WorldWithoutDanger Methods

        private void UpdateBanList()
        {
            BannedNames = CfgrwBannedSpecies.Value
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        public static bool IsBanned(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string ban in BannedNames)
            {
                if (name.IndexOf(ban, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        #endregion

        #region Teleporter Methods

        private void LoadWaypoints()
        {
            if (!File.Exists(_savePath)) return;

            _waypoints.Clear();
            string[] lines = File.ReadAllLines(_savePath);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split by our custom separator
                string[] parts = line.Split(new string[] { "::" }, StringSplitOptions.None);

                if (parts.Length == 4)
                {
                    string name = parts[0];

                    // Parse Coordinates safely
                    if (float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out float z))
                    {
                        _waypoints.Add(new Waypoint { name = name, position = new Vector3(x, y, z) });
                    }
                }
            }
        }

        private void SaveWaypointsToFile()
        {
            List<string> lines = new List<string>();
            foreach (var wp in _waypoints)
            {
                string x = wp.position.x.ToString(CultureInfo.InvariantCulture);
                string y = wp.position.y.ToString(CultureInfo.InvariantCulture);
                string z = wp.position.z.ToString(CultureInfo.InvariantCulture);

                lines.Add($"{wp.name}::{x}::{y}::{z}");
            }

            try
            {
                File.WriteAllLines(_savePath, lines.ToArray());
                Globals.Log($"[QoL] Saved {_waypoints.Count} waypoints to {_savePath}");
            }
            catch (Exception e)
            {
                Globals.Log($"[QoL] Failed to write file: {e.Message}");
            }
        }

        private void SaveCurrentPosition()
        {
            if (Globals.animalManager == null || Globals.animalManager.LocalPlayer == null)
                return;

            // 1. GET POSITION
            Vector3 pos = Globals.animalManager.LocalPlayer.Position;

            string safeName = _inputName.Replace("::", " ");
            if (string.IsNullOrEmpty(safeName)) safeName = "Unnamed";

            // 3. ADD TO LIST
            _waypoints.Add(new Waypoint { name = safeName, position = pos });
            _inputName = ""; // Clear input

            // 4. SAVE
            SaveWaypointsToFile();
        }

        private void TeleportTo(Waypoint wp)
        {
            if (Globals.animalManager == null || Globals.animalManager.LocalPlayer == null) return;

            Globals.Log($"[QoL] Teleporting to {wp.name} at {wp.position}...");

            // Try to force grounding so you don't fall through the map
            Globals.animalManager.LocalPlayer.Teleport(
                wp.position,
                Globals.animalManager.LocalPlayer.transform.rotation,
                GroundingMode.GroundUpwardOrDownwardFully,
                true
            );
        }

        private void TeleportToCurrentDen()
        {
            if (Globals.animalManager == null || Globals.animalManager.LocalPlayer == null) return;
            
            var pack = Globals.animalManager.LocalPlayer.Pack;
            if (pack != null && pack.HomeSite != null)
            {
                Globals.Log($"[QoL] Teleporting to current den...");
                Globals.animalManager.LocalPlayer.Teleport(
                    pack.HomeSite.VisualPosition, 
                    Globals.animalManager.LocalPlayer.transform.rotation, 
                    GroundingMode.GroundUpwardOrDownwardFully, 
                    true
                );
            }
            else
            {
                Globals.Log($"[QoL] No active den found to teleport to!");
            }
        }

        private void TeleportAllPupsToPlayer()
        {
            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null) return;

            var pups = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups;
            if (pups == null) return;

            Vector3 playerPos = Globals.animalManager.LocalPlayer.Position;
            int teleportedCount = 0;

            foreach (var pup in pups)
            {
                if (pup == null) continue;

                pup.Teleport(
                    playerPos,
                    pup.transform.rotation,
                    GroundingMode.GroundUpwardOrDownwardFully,
                    true
                );
                teleportedCount++;
            }

            Globals.Log($"[QoL] Teleported {teleportedCount} pups to player position.");
        }

        private void MaxAllPupsXP()
        {
            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null) return;

            var pups = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups;
            if (pups == null) return;

            int maxedCount = 0;
            foreach (var pup in pups)
            {
                if (pup == null || pup.WolfState == null) continue;

                pup.WolfState.Xp = 9999f;
                maxedCount++;
            }

            Globals.Log($"[QoL] Maxed XP for {maxedCount} pups.");
        }

        private void ApplyAdvancedSenses()
        {
            if (Globals.animalManager.LocalPlayer == null) return;

            if (!Globals.animalManager.LocalPlayer.WolfDef.perks.Contains(Perk.SuperSmeller))
                Globals.animalManager.LocalPlayer.WolfDef.perks.Add(Perk.SuperSmeller);

            if (!Globals.animalManager.LocalPlayer.WolfDef.perks.Contains(Perk.GoodMemory))
                Globals.animalManager.LocalPlayer.WolfDef.perks.Add(Perk.GoodMemory);

            if (!Globals.animalManager.LocalPlayer.WolfDef.perks.Contains(Perk.HealthPerception))
                Globals.animalManager.LocalPlayer.WolfDef.perks.Add(Perk.HealthPerception);

            if (!Globals.animalManager.LocalPlayer.WolfDef.perks.Contains(Perk.Territorial))
                Globals.animalManager.LocalPlayer.WolfDef.perks.Add(Perk.Territorial);

            if (!Globals.animalManager.LocalPlayer.WolfDef.perks.Contains(Perk.Elder))
                Globals.animalManager.LocalPlayer.WolfDef.perks.Add(Perk.Elder);
        }

        private void ApplyPackSetting()
        {
            if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null) return;

            // Add adult NPCs (mate and other adults) from the pack
            if (Globals.animalManager.LocalPlayer.Pack != null &&
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData != null)
            {
                var adultNpcs = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AdultNpcs;
                if (adultNpcs != null)
                {
                    foreach (Animal wolf in adultNpcs)
                    {
                        if (wolf != null)
                        {
                            ToggleScentObjects(wolf.transform, CfgrwDisablePackScent.Value);

                            if (Globals.animalManager.LocalPlayer != null &&
                                wolf != Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Mate)
                            {
                                if (CfgrwImmortalPack.Value)
                                    wolf.State.Health = wolf.State.MaxHealth;

                                if (CfgrwMaxFoodPack.Value)
                                    wolf.State.Food = wolf.State.MaxFoodWithRegurgitant;
                            }
                        }
                    }
                }
            }
        }

        private int ToggleScentObjects(Transform parent, bool shouldDisable)
        {
            int count = 0;
            foreach (Transform child in parent)
            {
                if (child.name.IndexOf("Scent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (child.gameObject.activeSelf == shouldDisable)
                    {
                        child.gameObject.SetActive(!shouldDisable);
                        count++;
                    }
                }

                // Recurse into children
                count += ToggleScentObjects(child, shouldDisable);
            }

            return count;
        }

        #endregion

        void OnGUI()
        {
            // Draw ShowDate UI (independent from settings window)
            DrawShowDateUI();

            if (!showSettingsMenu) return;

            // Initialize window position to center of screen on first show
            if (!windowInitialized)
            {
                float windowWidth = Mathf.Min(500, Screen.width - 40);
                float windowHeight = Mathf.Min(700, Screen.height - 40);
                settingsWindowRect = new Rect(
                    (Screen.width - windowWidth) * .5f,
                    (Screen.height - windowHeight) * .5f,
                    windowWidth,
                    windowHeight
                );
                windowInitialized = true;
            }

            // Clamp window to screen bounds
            settingsWindowRect.x = Mathf.Clamp(settingsWindowRect.x, 0, Screen.width - settingsWindowRect.width);
            settingsWindowRect.y = Mathf.Clamp(settingsWindowRect.y, 0, Screen.height - settingsWindowRect.height);

            // Style setup - darker background for readability
            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            GUI.contentColor = Color.white;
            GUI.skin.window.fontSize = 14;
            GUI.skin.label.fontSize = 12;
            GUI.skin.toggle.fontSize = 12;
            GUI.skin.button.fontSize = 12;

            settingsWindowRect = GUI.Window(12345, settingsWindowRect, DrawSettingsWindow,
                $"QoL Settings ({CfgMenuToggleKey.Value} to close)");

            // Reset colors after window
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
        }

        void DrawShowDateUI()
        {
            if (!showDateUI || !Globals.yearCycleManager) return;

            Rect rect = new Rect(Screen.width * .5f - 60, 2, 120, 26);
            GUI.Box(rect, $"{Globals.yearCycleManager.Month}, {Globals.yearCycleManager.Day}");
        }

        void DrawSettingsWindow(int windowID)
        {
            // Tab bar
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabNames.Length; i++)
            {
                GUI.backgroundColor = (currentTab == i) ? new Color(0.3f, 0.6f, 1f) : new Color(0.2f, 0.2f, 0.2f);
                if (GUILayout.Button(tabNames[i], GUILayout.Height(30)))
                {
                    currentTab = i;
                }
            }

            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            switch (currentTab)
            {
                case 0:
                    DrawQoLTab();
                    break;
                case 1:
                    DrawDateTab();
                    break;
                case 2:
                    DrawTeleporterTab();
                    break;
                case 3:
                    DrawDangersTab();
                    break;
            }

            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        void DrawQoLTab()
        {
            // Territory Settings
            GUILayout.Label("=== Territory ===", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Territory Quality Multiplier: {CfgrwTerritoryQualityMultiplier.Value:F1}x",
                GUILayout.Width(250));
            float newMultiplier = GUILayout.HorizontalSlider(CfgrwTerritoryQualityMultiplier.Value, 0.1f, 10f);
            if (newMultiplier != CfgrwTerritoryQualityMultiplier.Value)
            {
                CfgrwTerritoryQualityMultiplier.Value = Mathf.Round(newMultiplier * 10f) / 10f;
            }

            GUILayout.EndHorizontal();
            DrawToggle(CfgrwNoTerritoryDecay, "No Territory Decay");
            DrawToggle(CfgrwNoScentPostRequirement, "No Scent Post Requirement");
            DrawToggle(CfgrwAutoClaimTerritories, "Auto Claim All Territories");
            DrawToggle(CfgrwAutoUnclaimTerritories, "Auto Unclaim All Territories");

            GUILayout.Space(15);

            // Gameplay Settings
            GUILayout.Label("=== Gameplay ===", new GUIStyle(GUI.skin.label) { richText = true });
            DrawToggle(CfgrwNoSlippingPatch, "No Slippery Slopes");
            DrawToggle(CfgrwDropEverythingToSwimPatch, "No Drop-On-Swim");
            DrawToggle(CfgrwQuickSleep, "Quick Sleep");
            DrawToggle(CfgrwDisableRally, "Disable Rally");
            DrawToggle(CfgrwDisableStamina, "Max Energy");
            DrawToggle(CfgrwDisableHealth, "Max Health");
            DrawToggle(CfgrwDisableSleep, "Max Wakefulness");
            DrawToggle(CfgrwDisableAffinity, "Max Affinity");
            DrawToggle(CfgrwDisableHunger, "Max Food");
            DrawToggle(CfgrwCooldownPatch, "No Cooldown in Courtship");
            DrawToggle(CfgrwEatPatch, "Eating Restores Health");
            DrawToggle(CfgrwNoFleePrey, "Disable Prey Fleeing");
            DrawToggle(CfgrwDiscoverDensPatch, "Discover All Dens");
            DrawToggle(CfgrwDisablePackScent, "Disable Pack Scents");
            if (GUILayout.Button("Grant Advanced Senses", GUILayout.Height(25)))
                ApplyAdvancedSenses();
            // Weather control
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Set Weather: {weatherNames[CfgrwLockedWeather.Value]}");
            int newWeather =
                Mathf.RoundToInt(GUILayout.HorizontalSlider(CfgrwLockedWeather.Value, 0, weatherNames.Length - 1));
            CfgrwLockedWeather.Value = newWeather;
            if (GUILayout.Button("Apply")) ApplyLockedWeather();
            GUILayout.EndHorizontal();

            GUILayout.Space(15);

            // Pup Settings
            GUILayout.Label("=== Pack ===", new GUIStyle(GUI.skin.label) { richText = true });
            DrawToggle(CfgrwLostStatePatch, "No Lost Pups");
            DrawToggle(CfgrwHungerAndThirstUpdaterPatch, "Can Always Feed Pups");
            DrawToggle(CfgrwImmortalPups, "Immortal Pups");
            DrawToggle(CfgrwMaxFoodlPups, "Max Food Pups");
            DrawToggle(CfgrwImmortalMate, "Immortal Mate");
            DrawToggle(CfgrwMaxFoodMate, "Max Food Mate");
            DrawToggle(CfgrwImmortalPack, "Immortal Pack Members");
            DrawToggle(CfgrwMaxFoodPack, "Max Food Pack Members");
            DrawToggle(CfgrwPreventDispersalReturn, "Prevent Dispersals Returning");
            DrawToggle(CfgrwNonPausingDispersalReturnNotification, "Non-pausing Return Alert");

            // Max All Pups XP button
            if (GUILayout.Button("Max All Pups XP", GUILayout.Height(25)))
                MaxAllPupsXP();

            // Teleport all pups to player
            if (GUILayout.Button("Teleport All Pups To Player", GUILayout.Height(25)))
                TeleportAllPupsToPlayer();
        }

        void DrawDateTab()
        {
            GUILayout.Label("<b>Show Date</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(10);
            GUILayout.Label(
                $"Press <color=yellow>{CfgDateToggleKey.Value}</color> to toggle the date display on screen.",
                new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(10);

            if (Globals.yearCycleManager != null)
            {
                GUILayout.Label(
                    $"Current Date: <b>{Globals.yearCycleManager.Month}, {Globals.yearCycleManager.Day}</b>",
                    new GUIStyle(GUI.skin.label) { richText = true });

                GUILayout.Space(15);

                GUILayout.Label("=== Jump to Date ===", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.Space(5);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Day {daySlider}", new GUIStyle(GUI.skin.label) { richText = true });
                daySlider = (int)GUILayout.HorizontalSlider(daySlider, 1, 31);
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                for (int i = 0; i < months.Length; i++)
                {
                    if (GUILayout.Button(months[i], GUILayout.Height(25)))
                    {
                        Globals.yearCycleManager.AdvanceTo(new CyclicDate(monthEnums[i], daySlider));
                    }
                }
            }
        }

        void DrawTeleporterTab()
        {
            if (Globals.animalManager?.LocalPlayer == null)
                return;

            GUILayout.Label("<b>Teleporter</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(10);

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
            if (GUILayout.Button("Teleport to Current Den", GUILayout.Height(30)))
            {
                TeleportToCurrentDen();
            }

            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            GUILayout.Space(10);

            // Save section
            GUILayout.BeginVertical("box");
            GUILayout.Label("New Location Name:");
            _inputName = GUILayout.TextField(_inputName, 30);

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Save Current Position", GUILayout.Height(30)))
            {
                SaveCurrentPosition();
            }

            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.Label($"Saved Waypoints: {_waypoints.Count}");

            _teleporterScrollPosition =
                GUILayout.BeginScrollView(_teleporterScrollPosition, "box", GUILayout.Height(250));

            for (int i = 0; i < _waypoints.Count; i++)
            {
                Waypoint wp = _waypoints[i];
                GUILayout.BeginHorizontal();

                // Teleport
                if (GUILayout.Button($"<b>{wp.name}</b>", new GUIStyle(GUI.skin.button) { richText = true },
                        GUILayout.Height(25)))
                {
                    TeleportTo(wp);
                }

                // Delete
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("X", GUILayout.Width(25), GUILayout.Height(25)))
                {
                    _waypoints.RemoveAt(i);
                    SaveWaypointsToFile();
                    i--;
                }

                GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        void DrawDangersTab()
        {
            GUILayout.Label("<b>Dangers</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(10);
            GUILayout.Label("Species Banlist", new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.Space(5);

            CfgrwBannedSpecies.Value = GUILayout.TextArea(CfgrwBannedSpecies.Value, GUILayout.Height(100));

            GUILayout.Space(15);

            GUILayout.Label("=== Misc ===", new GUIStyle(GUI.skin.label) { richText = true });
            DrawToggle(CfgrwNoFloodedDens, "No Flooded Dens");
            DrawToggle(CfgrwFleaPatch, "No Fleas");
            DrawToggle(CfgrwNoFallingDamage, "No Falling Damage");
            DrawToggle(CfgrwNoDeathByAge, "No Death By Age");
            DrawToggle(CfgrwNoHunters, "No Hunters");
            DrawToggle(CfgrwNoInjuries, "No Injuries");
            DrawToggle(CfgrwHealthUpdaterPatch, "No Invincible NPCs");
        }

        void DrawToggle(ConfigEntry<bool> config, string label)
        {
            string checkmark = config.Value ? "[X]" : "[  ]";
            string buttonText = $"{checkmark} {label}";
            if (GUILayout.Button(buttonText, GUILayout.Height(22)))
            {
                config.Value = !config.Value;
            }
        }

        void SetAllTerritoriesUnowned()
        {
            if (Globals.territoryManager == null) return;

            foreach (var hex in Globals.territoryManager.Hexes)
            {
                if (hex.MarkingPost != null)
                {
                    Globals.territoryManager.EliminateMarkingPost(hex, null);
                }

                hex.SetTerritoryQuality(0f, 0f);
                hex.SetOwner(null);
            }
        }

        void SetAllTerritoriesPlayer()
        {
            if (Globals.territoryManager == null ||
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData == null) return;

            var playerPack = Globals.animalManager.LocalPlayer.Pack.PlayerPackData;
            if (playerPack == null) return;

            foreach (var hex in Globals.territoryManager.Hexes)
            {
                if (hex.MarkingPost != null && hex.MarkingPost.Pack != playerPack)
                {
                    Globals.territoryManager.EliminateMarkingPost(hex, null);
                }

                hex.SetOwner((RivalWolfPack)Globals.animalManager.LocalPlayer.Pack);
                hex.SetTerritoryQuality(1f, 0f);
            }
        }

        void ApplyLockedWeather()
        {
            if (CfgrwLockedWeather.Value == 0) return;

            WeatherType targetWeather = (WeatherType)weatherValues[CfgrwLockedWeather.Value];

            if (nextWeather != null)
                nextWeather.SetValue(Globals.weatherManager, targetWeather);

            Globals.weatherManager.ChangeWeather(targetWeather);
        }


        #region Fixes

        [HarmonyPatch(typeof(TroubleshootingSceneManager))]
        [HarmonyPatch("Awake")]
        public static class rwTroubleshootingSceneManagerPatch
        {
            // Completely bypasses the crash screen detection.
            static bool Prefix(TroubleshootingSceneManager __instance)
            {
                Time.timeScale = 1f;
                __instance.canvasGroup.alpha = 0f;
                PlayerPrefs.SetInt("TroubleshootingSceneNeeded", 0);
                SceneUtilities.LoadSceneAsync(SceneUtilities.SceneIdentity.MAIN_MENU, true);
                return false;
            }
        }

        // Removes some ridiculously expensive operations
        [HarmonyPatch(typeof(NormalCameraMover))]
        public static class rwCameraPatch
        {
            [HarmonyPatch("IsViewClear")]
            [HarmonyPrefix]
            public static bool Prefix_IsViewClear(ref bool __result)
            {
                __result = true;
                return false;
            }
        }

        // Makes all territories claimable
        [HarmonyPatch(typeof(TerritoryManager))]
        public static class rwTerritoryManagerPatch1
        {
            [HarmonyPatch("IsHexOwnerLocked")]
            [HarmonyPrefix]
            public static bool Prefix_IsHexOwnerLocked(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        #endregion


        #region Optional  Patches

        [HarmonyPatch(typeof(AffinityManager))]
        public static class rwCheatAffinity
        {
            [HarmonyPatch("UpdateAffinity")]
            [HarmonyPostfix]
            public static void Postfix_UpdateAffinity(AffinityManager __instance)
            {
                __instance.affinity = 200f;
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwCheatSleep
        {
            [HarmonyPatch("Wakefulness", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_Get(ref float ___wakefulness, ref float __result)
            {
                __result = 100;
                return false;
            }

            [HarmonyPatch("Wakefulness", MethodType.Setter)]
            [HarmonyPrefix]
            public static bool Prefix_Set(ref float ___wakefulness)
            {
                ___wakefulness = 1000;
                return false;
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwCheatFood
        {
            [HarmonyPatch("Food", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_Get(ref float ___food, ref float __result)
            {
                __result = 1000;
                return false;
            }

            [HarmonyPatch("Food", MethodType.Setter)]
            [HarmonyPrefix]
            public static bool Prefix_Set(ref float ___food)
            {
                ___food = 1000;
                return false;
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwCheatStamina
        {
            [HarmonyPatch("Energy", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_Get(ref float ___energy, ref float __result)
            {
                __result = 200;
                return false;
            }

            [HarmonyPatch("Energy", MethodType.Setter)]
            [HarmonyPrefix]
            public static bool Prefix_Set(ref float ___energy)
            {
                ___energy = 1000;
                return false;
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwCheatHealth
        {
            [HarmonyPatch("Health", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_Get(ref float ___health, ref float __result)
            {
                __result = 300;
                return false;
            }

            [HarmonyPatch("Health", MethodType.Setter)]
            [HarmonyPrefix]
            public static bool Prefix_Set(ref float ___health)
            {
                ___health = 1000;
                return false;
            }
        }

        [HarmonyPatch(typeof(WolfState))]
        public static class rwXPPatch
        {
            [HarmonyPatch("Xp", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_Get(ref float ___xp, ref float __result)
            {
                ___xp = 9999f;
                __result = 9999f;
                return false;
            }

            [HarmonyPatch("Xp", MethodType.Setter)]
            [HarmonyPrefix]
            public static bool Prefix_Set(ref float ___xp)
            {
                ___xp = 9999f;
                return false;
            }
        }

        [HarmonyPatch(typeof(TerritoryManager))]
        public static class rwTerritoryManagerPatch2 // Territories do not decay
        {
            [HarmonyPatch("UpdateDecay")]
            [HarmonyPrefix]
            public static bool Prefix_UpdateDecay(float hourStep)
            {
                if (!CfgrwNoTerritoryDecay.Value) return true; // Run original if disabled
                return false;
            }
        }

        [HarmonyPatch(typeof(HealthUpdater))]
        public static class rwTakeFallingDamagePatch // No falling to your death
        {
            [HarmonyPatch("TakeFallingDamage")]
            [HarmonyPrefix]
            public static bool Prefix_TakeFallingDamage(HealthUpdater __instance, ref int __result)
            {
                if (!CfgrwNoFallingDamage.Value) return true; // Run original if disabled
                __result = 0;
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerHomeManager))]
        public static class rwFleaPatch // No more fleas!
        {
            [HarmonyPatch("UpdatePlayerHome")]
            [HarmonyPostfix]
            public static void Postfix_UpdatePlayerHome(float hourStep, ref float ___baseFleaLoad,
                ref float ___penaltyFleaLoad)
            {
                if (!CfgrwFleaPatch.Value) return; // Skip if disabled
                ___baseFleaLoad = 0;
                ___penaltyFleaLoad = 0;
            }
        }

        [HarmonyPatch(typeof(Animal))]
        public static class rwDropEverythingToSwimPatch // Should prevent items from getting lost on swim
        {
            [HarmonyPatch("DropEverythingToSwim")]
            [HarmonyPrefix]
            public static bool Prefix_DropEverythingToSwim(Animal __instance)
            {
                if (!CfgrwDropEverythingToSwimPatch.Value) return true; // Run original if disabled
                return false;
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwLostStatePatch // Safety net: always reports pups as NotLost
        {
            [HarmonyPatch("LostState", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_LostState(AnimalState __instance, ref PupLostState __result)
            {
                if (!CfgrwLostStatePatch.Value) return true; // Run original if disabled
                __result = PupLostState.NotLost;
                return false;
            }
        }

        [HarmonyPatch(typeof(PupsLostManager))]
        public static class rwNoPupsLostPatch // Prevents pups from entering Lost state at the source
        {
            [HarmonyPatch("UpdatePupsLost")]
            [HarmonyPrefix]
            public static bool Prefix_UpdatePupsLost()
            {
                if (!CfgrwLostStatePatch.Value) return true; // Run original if disabled
                return false; // Skip entire method - no pup can become lost
            }
        }

        [HarmonyPatch(typeof(FudgingManager))]
        public static class rwNoLostInstantlyPatch // Prevents fudging "Unknown fate" kills during sleep
        {
            [HarmonyPatch("CompleteLostInstantly")]
            [HarmonyPrefix]
            public static bool Prefix_CompleteLostInstantly(ref bool ___lostInstantlyTriggered)
            {
                if (!CfgrwLostStatePatch.Value) return true; // Run original if disabled
                ___lostInstantlyTriggered = false; // Clear the flag so WokenUp doesn't retry
                return false; // Skip the kill
            }
        }

        [HarmonyPatch(typeof(AnimalState))]
        public static class rwEatPatch // Heal on Eat
        {
            [HarmonyPatch("Eat")]
            [HarmonyPrefix]
            public static bool Prefix_Eat(
                AnimalState __instance, // Access properties like MaxHealth here
                float foodAmount,
                bool canAlwaysEat,

                // USE REF TO MODIFY VALUES
                ref float ___food,
                ref float ___health,
                ref bool ___canEat)
            {
                if (!CfgrwEatPatch.Value) return true; // Run original if disabled

                // 1. SETUP VALUES
                // Use properties from the instance if ___ injection is risky for Max values
                float maxFood = __instance.MaxFoodWithRegurgitant;
                float maxHealth = __instance.MaxHealth;

                // 2. INCREASE FOOD
                ___food = Mathf.Min(___food + foodAmount, maxFood);

                // 3. CHECK IF FULL
                if (___food == maxFood && ___health >= maxHealth && !canAlwaysEat)
                {
                    ___canEat = false;
                }
                else
                {
                    float healAmount = Mathf.Max(foodAmount, 1.2f);
                    ___health += healAmount;
                    if (___health > maxHealth)
                        ___health = maxHealth;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerKnownDensManager))]
        public static class rwDiscoverDensPatch // Unlocks all Dens
        {
            [HarmonyPatch("IsDiscoveredDen")]
            [HarmonyPrefix]
            public static bool Prefix_IsDiscoveredDen(HomeSite denSite, ref bool __result,
                System.Collections.Generic.List<HomeSite> ___discoveredDens)
            {
                if (!CfgrwDiscoverDensPatch.Value) return true; // Run original if disabled

                if (!___discoveredDens.Contains(denSite))
                    ___discoveredDens.Add(denSite);

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(HungerAndThirstUpdater))]
        public static class rwHungerAndThirstUpdaterPatch // Can feed pups always
        {
            [HarmonyPatch("MinFoodRatioToRegurgitate", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix(ref float __result)
            {
                if (!CfgrwHungerAndThirstUpdaterPatch.Value) return true; // Run original if disabled
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(HealthUpdater))]
        public static class rwHealthUpdaterPatch // There is no undying animals
        {
            [HarmonyPatch("MinHealth", MethodType.Getter)]
            [HarmonyPrefix]
            public static bool Prefix_MinHealth(ref int __result)
            {
                if (!CfgrwHealthUpdaterPatch.Value) return true; // Run original if disabled
                __result = 0;
                return false;
            }
        }

        [HarmonyPatch(typeof(PhysicalMover))]
        public static class rwNoSlippingPatch // Stops slope slipping
        {
            [HarmonyPatch("UpdateSlipping")]
            [HarmonyPrefix]
            public static bool Prefix(object ___self)
            {
                if (!CfgrwNoSlippingPatch.Value) return true; // Run original if disabled
                var animal = ___self as Animal;
                if (animal != null && animal.State != null)
                {
                    animal.State.SlippingState = SlippingState.NotSlipping;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(EmoteControls))]
        public static class rwCooldownPatch // Should allow for unlimited interactions without warnings
        {
            [HarmonyPatch("IsInCooldown")]
            [HarmonyPrefix]
            public static bool Prefix_IsInCooldown(AnimalSignal signal, ref bool __result)
            {
                if (!CfgrwCooldownPatch.Value) return true; // Run original if disabled
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(PackRallyManager))]
        public static class rwDisableRallyPatch // Disables rally when howling
        {
            [HarmonyPatch("UpdatePackRally")]
            [HarmonyPrefix]
            public static bool Prefix_UpdatePackRally(float timestep, float hourStep)
            {
                if (!CfgrwDisableRally.Value) return true; // Run original if disabled
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerAnimalControls))]
        public static class rwDisableRallyAnimationPatch // Forces normal howl animation instead of rally howl
        {
            [HarmonyPatch("CheckTransitionPackRallyHowl")]
            [HarmonyPrefix]
            public static bool Prefix_CheckTransitionPackRallyHowl(ref bool __result)
            {
                if (!CfgrwDisableRally.Value) return true; // Run original if disabled
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(TerritoryManager))]
        public static class rwNoScentPostRequirementPatch // Removes the scent post requirement for territory quality
        {
            [HarmonyPatch("GetMinQualityWithBuffer")]
            [HarmonyPrefix]
            public static bool Prefix_GetMinQualityWithBuffer(ref float __result)
            {
                if (!CfgrwNoScentPostRequirement.Value) return true; // Run original if option is disabled
                __result = 1f; // Return 100% so there's no cap without a scent post
                return false;
            }
        }

        [HarmonyPatch(typeof(TerritoryManager))]
        public static class rwTerritoryQualityMultiplierPatch // Multiplies territory quality gain
        {
            [HarmonyPatch("AddHexQualityForPack")]
            [HarmonyPrefix]
            public static void Prefix_AddHexQualityForPack(ref float qualityAdded)
            {
                qualityAdded *= CfgrwTerritoryQualityMultiplier.Value;
            }
        }

        [HarmonyPatch(typeof(LitterCalculator))]
        public static class rwMaxLitterSizePatch
        {
            [HarmonyPatch("GetInitialPupCount")]
            [HarmonyPostfix]
            public static void Postfix_GetInitialPupCount(ref int __result)
            {
                if (CfgrwMaxLitterSize.Value > 0)
                {
                    __result = CfgrwMaxLitterSize.Value;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerNotificationControls))]
        public static class rwNoScentPostNotificationPatch // Suppress the "needs marking post" notification
        {
            [HarmonyPatch("OnPlayerNeedsMarkingPost")]
            [HarmonyPrefix]
            public static bool Prefix_OnPlayerNeedsMarkingPost()
            {
                // Skip the notification entirely if scent posts aren't required
                return !CfgrwNoScentPostRequirement.Value;
            }
        }

        // Teleporter & Weather Patch
        /*
        // Teleporter & Weather Patch
        [HarmonyPatch(typeof(MasterInitializer))]
        public static class rwFetchAnimalManager
        {
            [HarmonyPatch("Initialize")]
            [HarmonyPostfix]
            public static void Postfix_Initialize(MasterInitializer __instance)
            {
                // FieldInfo animalField = AccessTools.Field(typeof(MasterInitializer), "animalManager");
                // if (animalField != null)
                //    _animalManager = (AnimalManager)animalField.GetValue(__instance);
            }
        }
        */

        // WorldWithoutDanger Patch
        [HarmonyPatch(typeof(FlockMemberSpawner))]
        public static class GatekeeperPatch
        {
            [HarmonyPatch("ShouldSpawn")]
            [HarmonyPrefix]
            public static bool Prefix_ShouldSpawn(ref bool __result, Flock ___flock)
            {
                // SAFETY CHECKS
                if (___flock == null || ___flock.Species == null) return false;

                if (IsBanned(___flock.Species.name))
                {
                    __result = false;
                    return false;
                }

                // Otherwise, let the original logic decide (distance, time of day, etc.)
                return true;
            }
        }

        // No Injuries Patch - Prevents all animals from getting injured
        [HarmonyPatch(typeof(HealthUpdater))]
        public static class rwNoInjuriesPatch
        {
            [HarmonyPatch("RollForInjury")]
            [HarmonyPrefix]
            public static bool Prefix_RollForInjury(ref InjurySeverity __result)
            {
                if (!CfgrwNoInjuries.Value) return true; // Run original if disabled

                __result = InjurySeverity.None;
                return false; // Skip execution, never return an injury
            }
        }

        [HarmonyPatch(typeof(AnimalScentHandler))]
        public static class rwAnimalScentHandlerPatch
        {
            [HarmonyPatch("get_ScentTrackLayingState")]
            [HarmonyPrefix]
            public static bool Prefix_get_ScentTrackLayingState(AnimalScentHandler __instance,
                ref ScentTrackLayingState __result, Animal ___self)
            {
                if (CfgrwDisablePackScent.Value && ___self.IsInPlayerPack)
                {
                    __result = ScentTrackLayingState.LayNoTrack;
                    return false; // Skip original getter
                }

                return true;
            }
        }

        // --- Merged Patches ---

        [HarmonyPatch(typeof(PlayerHomeManager), nameof(PlayerHomeManager.HeavyRainHappened))]
        public static class PatchNoFloodedDens
        {
            static bool Prefix()
            {
                if (!CfgrwNoFloodedDens.Value) return true;
                return false;
            }
        }

        [HarmonyPatch(typeof(HuntingZoneManager))]
        public static class PatchNoHunters
        {
            [HarmonyPatch("SpawnHunters")]
            [HarmonyPrefix]
            public static bool Prefix1()
            {
                if (!CfgrwNoHunters.Value) return true;
                return false;
            }

            [HarmonyPatch("RespawnHunters")]
            [HarmonyPrefix]
            public static bool Prefix2()
            {
                if (!CfgrwNoHunters.Value) return true;
                return false;
            }
        }

        [HarmonyPatch(typeof(HuntingZoneHunter))]
        public static class PatchNoHunters_Hunter
        {
            [HarmonyPatch("Spawn")]
            [HarmonyPrefix]
            public static bool Prefix1()
            {
                if (!CfgrwNoHunters.Value) return true;
                return false;
            }

            [HarmonyPatch("UpdateShooting")]
            [HarmonyPrefix]
            public static bool Prefix2()
            {
                if (!CfgrwNoHunters.Value) return true;
                return false;
            }

            [HarmonyPatch("ShootTarget")]
            [HarmonyPrefix]
            public static bool Prefix3()
            {
                if (!CfgrwNoHunters.Value) return true;
                return false;
            }
        }

        [HarmonyPatch(typeof(PackProgressionManager))]
        public static class rwPreventDispersalReturnPatch
        {
            [HarmonyPatch("ChanceByPackSize")]
            [HarmonyPostfix]
            public static void Postfix_ChanceByPackSize(PackProgressionManager.ChanceByPackSizeEntry[] chanceByPackSize, ref float __result, PackProgressionManager __instance)
            {
                if (!CfgrwPreventDispersalReturn.Value) return;

                var fieldInfo = AccessTools.Field(typeof(PackProgressionManager), "dispersalReturnToNatalOddsByNatalPackSize");
                if (fieldInfo != null)
                {
                    var fieldVal = fieldInfo.GetValue(__instance) as PackProgressionManager.ChanceByPackSizeEntry[];
                    if (chanceByPackSize == fieldVal)
                    {
                        __result = 0f;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PlayerPackControls))]
        public static class rwNonPausingDispersalReturnNotificationPatch
        {
            [HarmonyPatch("PackMemberReturned")]
            [HarmonyPrefix]
            public static bool Prefix_PackMemberReturned(Animal member, PlayerPackControls __instance)
            {
                if (!CfgrwNonPausingDispersalReturnNotification.Value) return true;

                var notifControlsField = AccessTools.Field(typeof(PlayerPackControls), "notificationControls");
                var pupReturnedNotifField = AccessTools.Field(typeof(PlayerPackControls), "pupDispersalReturnedNotification");

                if (notifControlsField != null && pupReturnedNotifField != null)
                {
                    var notifControls = notifControlsField.GetValue(__instance) as NotificationControls;
                    var pupReturnedNotif = pupReturnedNotifField.GetValue(__instance) as Notification;

                    if (notifControls != null && pupReturnedNotif != null)
                    {
                        var originalType = pupReturnedNotif.type;
                        pupReturnedNotif.type = NotificationType.Corner;
                        notifControls.QueueNotification(pupReturnedNotif, (string text) => text.Replace("[pupname]", member.WolfDef.NickNameOrBirthName), null, null, null, null, member.WolfDef.sex);
                        pupReturnedNotif.type = originalType;
                        return false; // Skip original
                    }
                }

                return true; // Fallback to original
            }
        }

        #endregion
    }
}