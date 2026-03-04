using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using RewiredConsts;
using WolfQuestEp3;

namespace SharedCommons
{
    [BepInPlugin("com.rw.globals", "Globals", "1.0.0")]
    public class Globals : BaseUnityPlugin
    {
        public static Globals Instance { get; private set; }

        public static GUIWindowControls guiWindowControls;
        public static TabMenuControls tabMenuControls;
        public static AnimalManager animalManager;
        public static PersistentWolfCalculator persistentWolfCalculator;
        public static NotificationControls notificationControls;
        public static InstantiationManager instantiationManager;
        public static HomeSiteControls homeSiteControls;
        public static HomeSiteManager homeSiteManager;
        public static MeatManager meatManager;
        public static YearCycleManager yearCycleManager;
        public static AffinityManager affinityManager;
        public static TerritoryManager territoryManager;
        public static WeatherManager weatherManager;
        public static TimeManager timeManager;
        public static InputControls inputControls;
        public static PackInfoGUI packInfoGUI;
        public static TabMenuGUI tabMenuGUI;

        public static Animal playerInstance;

        public static Dictionary<string, string> Strings;
        public static Dictionary<string, int> Integers;
        public static Dictionary<string, float> Floats;
        public static Dictionary<string, bool> Bools;

        private float tmrUpdate;

        public static bool MenuIsOpen;

        private void Awake()
        {
            // Setup Instance
            Instance = this;

            // Apply Harmony patches
            Harmony.CreateAndPatchAll(typeof(Globals));

            // Setup Dictionaries
            Strings = new Dictionary<string, string>();
            Integers = new Dictionary<string, int>();
            Floats = new Dictionary<string, float>();
            Bools = new Dictionary<string, bool>();

            // Setup Logger
            Log("[Globals] Initialised.");
        }

        void Update()
        {
            // Evaluate menu state every frame for instant response
            if (Strings.ContainsKey("Scene"))
            {
                bool isMenuScene = Strings["Scene"] == "0" || Strings["Scene"] == "9";
                bool isGuiPaused = guiWindowControls != null && guiWindowControls.ShouldPause;
                bool isTabMenuOpen = tabMenuControls != null && tabMenuControls.ShowTabMenu;
                bool isTabMenuGUIOpen = tabMenuGUI != null && tabMenuGUI.tabMenuEntries != null && tabMenuGUI.tabMenuEntries.Length > 2 && tabMenuGUI.tabMenuEntries[2].canvasGroup.gameObject.activeInHierarchy;

                MenuIsOpen = isMenuScene || isGuiPaused || isTabMenuOpen || isTabMenuGUIOpen;
            }

            tmrUpdate += Time.deltaTime;
            if (tmrUpdate < 1f) return;

            tmrUpdate = 0f;

            if (!Strings.ContainsKey("Scene"))
                Strings.Add("Scene", SceneManager.GetActiveScene().name[0].ToString());
            else
                Strings["Scene"] = SceneManager.GetActiveScene().name[0].ToString();

            if (MenuIsOpen) return;

            if (!guiWindowControls)
                guiWindowControls = FindFirstObjectByType<GUIWindowControls>();

            if (!tabMenuControls)
                tabMenuControls = FindFirstObjectByType<TabMenuControls>();

            if (!tabMenuGUI)
                tabMenuGUI = FindFirstObjectByType<TabMenuGUI>();

            if (!animalManager)
                animalManager = FindFirstObjectByType<AnimalManager>();

            if (!meatManager)
                meatManager = FindFirstObjectByType<MeatManager>();

            if (!homeSiteManager)
                homeSiteManager = FindFirstObjectByType<HomeSiteManager>();

            if (!yearCycleManager)
                yearCycleManager = FindFirstObjectByType<YearCycleManager>();

            if (!affinityManager)
                affinityManager = FindFirstObjectByType<AffinityManager>();

            if (!territoryManager)
                territoryManager = FindFirstObjectByType<TerritoryManager>();

            if (!weatherManager)
                weatherManager = FindFirstObjectByType<WeatherManager>();

            if (!timeManager)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (!persistentWolfCalculator)
                persistentWolfCalculator = FindFirstObjectByType<PersistentWolfCalculator>();

            if (!notificationControls)
                notificationControls = FindFirstObjectByType<NotificationControls>();

            if (!instantiationManager)
                instantiationManager = FindFirstObjectByType<InstantiationManager>();

            if (!homeSiteControls)
                homeSiteControls = FindFirstObjectByType<HomeSiteControls>();

            if (!inputControls)
                inputControls = FindFirstObjectByType<InputControls>();

            if (!packInfoGUI)
                packInfoGUI = FindFirstObjectByType<PackInfoGUI>();

            if (animalManager != null)
                playerInstance = animalManager.LocalPlayer;
        }

        public static void Log(string message)
        {
            System.IO.File.AppendAllText("log.txt", message + "\n");
        }

        public static bool MenuOpen()
        {
            return MenuIsOpen;
        }

        public static void ShowMessage(string heading, string body, float duration = 5f,
            NotificationType type = NotificationType.Corner)
        {
            if (notificationControls == null) return;

            notificationControls.QueueNotification
            (
                type,
                heading,
                body,
                NotificationPriority.Normal,
                duration,
                null, null, null, null, null,
                null, null, false, -1,
                Sex.None
            );
        }

        void Spawn()
        {
            if (animalManager != null)
            {
                // Looks like debug code to spawn a test flock
                FlockSpawningManager flockSpawningManager = FindObjectOfType<FlockSpawningManager>();
                SceneAssetContainer sceneAssets = FindObjectOfType<SceneAssetContainer>();
                if (sceneAssets && flockSpawningManager)
                {
                    flockSpawningManager.SpawnFlock(sceneAssets.flockPrefabs[0], animalManager.LocalPlayer.Position,
                        Quaternion.identity, "Test Spawn", ushort.MaxValue,
                        initializer => { initializer.Initialize(true, null, false); });
                }
            }
        }
    }
}