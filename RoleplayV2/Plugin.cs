using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using System.IO;
using RewiredConsts;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using static WQRPG.Patches;
using SharedCommons;

namespace WQRPG
{
    [BepInPlugin("com.rw.wqrpg", "Roleplay V2", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private AssetBundle owlBundle;
        private GameObject ShopPrefab;
        private Animal player;

        public static RPGDatabase _rpgDatabase;
        public static InventoryManager _inventoryManager;

        public static BepInEx.Configuration.ConfigEntry<KeyCode> CfgInteractKey;
        public static BepInEx.Configuration.ConfigEntry<KeyCode> CfgInventoryKey;
        public static BepInEx.Configuration.ConfigEntry<KeyCode> CfgSellOneKey;

        private float lastRandomizationTime;
        private float randomizationInterval = 600f; // 10 minutes

        private void Awake()
        {
            CfgInteractKey = Config.Bind("Keybinds", "InteractKey", KeyCode.F, "Key to interact with shops");
            CfgInventoryKey = Config.Bind("Keybinds", "InventoryKey", KeyCode.I, "Key to open inventory");
            CfgSellOneKey = Config.Bind("Keybinds", "SellOneKey", KeyCode.LeftShift, "Key to hold to sell only one item from a stack");

            // Seems to be important
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(HealthUpdater_TakeDamage_Patch));
            Harmony.CreateAndPatchAll(typeof(Animal_GetAbilities_Patch));
            Harmony.CreateAndPatchAll(typeof(WolfDefinition_InnateAbilitiesCheatProof_Patch));
            Harmony.CreateAndPatchAll(typeof(WolfSaveManager_SaveWolf_Patch));
            Harmony.CreateAndPatchAll(typeof(GameplaySettingsControls_UpdateControlsAlways_Patch));
            Harmony.CreateAndPatchAll(typeof(MovableBackgroundBar_UpdatePosition_Patch));
            Harmony.CreateAndPatchAll(typeof(MovableFillBar_UpdatePosition_Patch));

            // Load assets from the bundle
            LoadAssets();

            // Feedback!
            Globals.Log("[RoleplayV2] Initialised successfully.");
        }

        void Update()
        {
            if (Globals.MenuIsOpen) return;

            if (player == null)
            {
                player = Globals.animalManager?.LocalPlayer;
                if (player != null)
                {
                    InitDatabase();
                    InitInventory();
                    SpawnForgeries();
                    lastRandomizationTime = Time.time;
                }
            }
            else
            {
                // Periodic randomization
                if (Time.time - lastRandomizationTime > randomizationInterval)
                {
                    lastRandomizationTime = Time.time;
                    _rpgDatabase?.RandomizeAllShops();
                }
            }
        }

        private void LoadAssets()
        {
            string bundlePath = Path.Combine(Paths.PluginPath, "roleplay_assets", "bundle");
            if (File.Exists(bundlePath))
            {
                owlBundle = AssetBundle.LoadFromFile(bundlePath);
                if (owlBundle != null)
                {
                    ShopPrefab = owlBundle.LoadAsset<GameObject>("Forgery");
                }
            }
        }

        private void InitDatabase()
        {
            _rpgDatabase = new RPGDatabase();
            _rpgDatabase.Init();
            _rpgDatabase.RandomizeAllShops();
        }

        private void InitInventory()
        {
            if (_inventoryManager == null)
                _inventoryManager = gameObject.AddComponent<InventoryManager>();

            try
            {
                _inventoryManager.Init();
                _inventoryManager.LoadData(Globals.animalManager.LocalPlayer.WolfDef);
                _inventoryManager.LastLoadedId = -1;
            }
            catch(System.Exception error)
            {
                Globals.Log($"[Roleplay] Error loading inventory: {error.Message}");
            }
        }

        private void SpawnForgeries()
        {
            if (ShopPrefab == null)               return;
            if (_rpgDatabase == null)             return;
            if (_rpgDatabase.allShops == null)    return;
            if (_rpgDatabase.allShops.Count == 0) return;

            Globals.Log($"[Roleplay] Spawning {_rpgDatabase.allShops.Count} shops...");

            char sceneName = SceneManager.GetActiveScene().name[0];
            foreach (var shopData in _rpgDatabase.allShops)
            {
                // Only spawn shops relevant to the current scene
                if (shopData.mapName[0] != sceneName) continue;

                // Create the Shop Object
                GameObject shopObj = Instantiate(ShopPrefab, shopData.position, Quaternion.identity, null);
                shopObj.name = "Shop_" + shopData.shopName;
                MenuScript menu = shopObj.AddComponent<MenuScript>();
                menu.Init(shopData);
            }
        }
    }
}
