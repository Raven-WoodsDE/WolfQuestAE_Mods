using UnityEngine;
using System.Collections.Generic;
using WolfQuestEp3;
using System.IO;
using UnityEngine.SceneManagement;
using SharedCommons;

namespace WQRPG
{
    public class InventoryManager : MonoBehaviour
    {
        public static InventoryManager Instance;

        // Player Inventory
        public List<RPGDatabase.ItemData> playerInventory = new List<RPGDatabase.ItemData>();

        // Equipment Slots: Key = Category (e.g., "Head", "Body"), Value = Item
        public Dictionary<string, RPGDatabase.ItemData> equipmentSlots = new Dictionary<string, RPGDatabase.ItemData>();

        public const int MaxEquipSlots = 8;

        // RPG Stats
        public int currentLevel = 1;
        public int currentXP = 0;
        public int maxXP = 1000;
        public int statPoints = 0;
        public int currency = 0;

        public int LastLoadedId = -1;

        // Base Stats (Upgradeable)
        public int baseStrength = 0;
        public int baseAgility = 0;
        public int baseStamina = 0;
        public int baseHealth = 0;

        // Cached Stats
        private int _cachedStrength;
        private int _cachedAgility;
        private int _cachedStamina;
        private int _cachedHealth;

        // Cached GUI resources (Bug 8 fix: avoid per-frame allocations)
        private Texture2D greenTex;
        private GUIStyle headerStyle;
        private GUIStyle xpLabelStyle;
        private GUIStyle colHeaderStyle;
        private GUIStyle nameRowStyle;
        private GUIStyle valRowStyle;
        private GUIStyle bonusRowStyle;
        private GUIStyle totalRowStyle;
        private GUIStyle pointsStyle;
        private GUIStyle hintStyle;
        private GUIStyle smallLabelStyle;
        private GUIStyle categoryLabelStyle;
        private GUIStyle coinLabelStyle;
        private bool guiStylesInitialized = false;

        // UI State
        private bool showMenus = false;
        private GUIStyle windowStyle;
        private Texture2D darkBackground;

        // draggabld window rects & IDs
        private Rect inventoryWindowRect;
        private Rect statsWindowRect;
        private bool windowRectsInitialized = false;
        private readonly int inventoryWindowId = 9001;
        private readonly int statsWindowId = 9002;

        public Dictionary<string, int> killCounters = new Dictionary<string, int>();

        private enum StatsTab { Stats, Kills }
        private StatsTab activeTab = StatsTab.Stats;

        // Paths
        // private string savePath; // Logic mvaed to RPGDataStore

        public void Init()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            // Create background texture for UI
            darkBackground = new Texture2D(1, 1);
            darkBackground.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            darkBackground.Apply();

            // Reset GUI state for new session (textures get destroyed across scene loads)
            windowStyle = null;
            guiStylesInitialized = false;
            windowRectsInitialized = false;
            greenTex = null;
            showMenus = false;

            RecalculateStats();
            Globals.Log("[Roleplay] InventoryManager Initialized.");
        }

        void Update()
        {
            if (Globals.MenuIsOpen)
            {
                showMenus = false;
                return;
            }

            // Toggle both windows with 'I' or Escape to close
            if (Input.GetKeyDown(Plugin.CfgInventoryKey.Value) || (showMenus && Input.GetKeyDown(KeyCode.Escape)))
            {
                showMenus = !showMenus;

                if (InputControls.DisableInput != showMenus)
                {
                    InputControls.DisableInput = showMenus;
                    InputControls.DisableCameraInput = showMenus;
                    InputControls.ForceAllowCursor = showMenus;
                }

                if (!showMenus) SaveData();
            }
        }

        public void ForceClose()
        {
            if (showMenus)
            {
                showMenus = false;
                InputControls.DisableInput = false;
                InputControls.DisableCameraInput = false;
                InputControls.ForceAllowCursor = false;
                SaveData();
            }

            UpdateStatBroadcasters();

            // Robust Data Loading: Check if we need to load data and if we can
            if (LastLoadedId == -1 && Globals.animalManager?.LocalPlayer?.WolfDef != null)
            {
                Globals.Log("[Roleplay] Late-loading Inventory data...");
                LoadData(Globals.animalManager.LocalPlayer.WolfDef);
            }
        }

        // Cache for Broadcasters
        private float broadcasterTimer = 0f;
        private const float broadcasterInterval = 5f;
        void UpdateStatBroadcasters()
        {
            broadcasterTimer += Time.deltaTime;
            if (broadcasterTimer < broadcasterInterval) return;

            broadcasterTimer = 0f;

            if (Globals.Integers.ContainsKey("rp_strength"))
                Globals.Integers["rp_strength"] = GetTotalStat("Strength");
            else
                Globals.Integers.Add("rp_strength", GetTotalStat("Strength"));
            
            
            if (Globals.Integers.ContainsKey("rp_agility"))
                Globals.Integers["rp_agility"] = GetTotalStat("Agility");
            else
                Globals.Integers.Add("rp_agility", GetTotalStat("Agility"));
            

            if (Globals.Integers.ContainsKey("rp_health"))
                Globals.Integers["rp_health"] = GetTotalStat("Health");
            else
                Globals.Integers.Add("rp_health", GetTotalStat("Health"));
            

            if (Globals.Integers.ContainsKey("rp_stamina"))
                Globals.Integers["rp_stamina"] = GetTotalStat("Stamina");
            else
                Globals.Integers.Add("rp_stamina", GetTotalStat("Stamina"));
        }

        public void AddXP(int amount)
        {
            currentXP += amount;
            while (currentXP >= maxXP) LevelUp();
            SaveData();
        }

        private void LevelUp()
        {
            currentXP -= maxXP;
            currentLevel++;
            statPoints += 3; // 3 points per level

            // Formula: Level^2 * 1000
            maxXP = (currentLevel * currentLevel) * 1000;

            // Notification
            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null)
            {
                nc.QueueNotification(NotificationType.Corner, "Level Up!", $"You are now Level {currentLevel}!",
                    NotificationPriority.Important, 5f);
            }
        }

        public void AddCurrency(int amount)
        {
            currency += amount;
            // Optional: Notification for currency?
            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null && amount > 10) // Only notify for significant amounts
            {
                nc.QueueNotification(NotificationType.Corner, "Currency Gained", $"+{amount} Coins",
                    NotificationPriority.Low, 3f);
            }

            SaveData();
        }

        public void AddKill(string speciesName)
        {
            if (killCounters.ContainsKey(speciesName))
            {
                killCounters[speciesName]++;
            }
            else
            {
                killCounters.Add(speciesName, 1);
            }
            SaveData();
        }

        public void AddItem(RPGDatabase.ItemData item)
        {
            playerInventory.Add(item);
            SaveData();
        }

        public void RemoveItem(RPGDatabase.ItemData item)
        {
            if (playerInventory.Contains(item))
            {
                playerInventory.Remove(item);
                SaveData();
            }
        }

        public void UseItem(RPGDatabase.ItemData item)
        {
            Globals.Log($"[Roleplay] UseItem clicked: {item.itemName} (Category: {item.category})");
            
            if (item.category.Equals("Statpoint", System.StringComparison.OrdinalIgnoreCase))
            {
                statPoints++;
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    nc.QueueNotification(NotificationType.Corner, "Statpoint Gained", $"Consumed {item.itemName} and gained 1 Statpoint!", NotificationPriority.Low, 3f);
                }
                RemoveItem(item);
                SaveData();
                return;
            }

            if (item.category.Equals("Soulstone", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeSoulstone(item);
                return;
            }

            if (item.category.Equals("Lifesource", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeLifesource(item);
                return;
            }

            if (item.category.Equals("Wolf Token", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeWolfToken(item);
                return;
            }

            if (item.category.Equals("Pup Token", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumePupToken(item);
                return;
            }

            if (item.category.Equals("Dog Collar", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeDogCollar(item);
                return;
            }

            if (item.category.Equals("FixHerb", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeFixHerb(item);
                return;
            }

            if (item.category.Equals("CureHerb", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeCureHerb(item);
                return;
            }

            if (item.category.Equals("HealHerb", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumeHealHerb(item);
                return;
            }

            if (item.category.Equals("Poison", System.StringComparison.OrdinalIgnoreCase))
            {
                ConsumePoison(item);
                return;
            }

            // 1. Check if Consumable (Food/Medicine)
            if (item.foodValue > 0 || item.healValue > 0)
            {
                ConsumeItem(item);
            }
            // 2. Otherwise assume it is Equipment
            else
            {
                EquipItem(item);
            }
        }

        private void ConsumeSoulstone(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            string targetSpecies = item.itemName.Replace("Soulstone", "").Trim().ToLower();

            FlockSpawningManager flockSpawningManager = FindObjectOfType<FlockSpawningManager>();
            SceneAssetContainer sceneAssets = FindObjectOfType<SceneAssetContainer>();

            if (sceneAssets == null || flockSpawningManager == null) return;

            Flock flockPrefab = null;

            if (targetSpecies.Contains("mule")) targetSpecies = "muledeer";
            if (targetSpecies.Contains("elk")) targetSpecies = "elk";
            if (targetSpecies.Contains("moose")) targetSpecies = "moose";
            if (targetSpecies.Contains("bison")) targetSpecies = "bison";
            if (targetSpecies.Contains("hare")) targetSpecies = "hare";
            if (targetSpecies.Contains("fox")) targetSpecies = "fox";
            if (targetSpecies.Contains("coyote")) targetSpecies = "coyote";

            foreach (var fp in sceneAssets.flockPrefabs)
            {
                if (fp.name.ToLower().Contains(targetSpecies))
                {
                    flockPrefab = fp;
                    break;
                }
            }

            if (flockPrefab == null)
            {
                Globals.Log($"[Roleplay] Could not find Flock Prefab for {item.itemName} ({targetSpecies})");
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                   nc.QueueNotification(NotificationType.Corner, "Soulstone Failed", $"Species {targetSpecies} not found!", NotificationPriority.Normal, 3f);
                return;
            }

            Vector3 spawnPos = Globals.animalManager.LocalPlayer.Position + Globals.animalManager.LocalPlayer.Physical.MovingPartTransform.forward * 15f;

            flockSpawningManager.SpawnFlock(flockPrefab, spawnPos, Quaternion.identity, "Soulstone Spawn", ushort.MaxValue,
                initializer => 
                {
                    initializer.Bake();

                    // Force single spawn
                    var spawnerSetup = initializer.baking.setup.spawning;
                    if (spawnerSetup.compositions != null && spawnerSetup.compositions.Length > 0)
                    {
                        var comp = spawnerSetup.compositions[0];
                        if (comp.subtypes != null && comp.subtypes.Length > 0)
                        {
                            comp.subtypes[0].minNumber = 1;
                            comp.subtypes[0].maxNumber = 1;
                            comp.subtypes = new FlockCompositionSubtypeEntry[] { comp.subtypes[0] };
                            spawnerSetup.compositions = new FlockCompositionEntry[] { comp };
                        }
                    }

                    initializer.Initialize(true, null, false);
                });

            RemoveItem(item);
        }

        private void ConsumeLifesource(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer?.WolfDef == null) return;

            // Reset the old-age death markers
            Globals.animalManager.LocalPlayer.WolfDef.dieInSleepOdds = 0f;
            Globals.animalManager.LocalPlayer.WolfDef.dieInSleepTriggered = false;

            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null)
            {
                nc.QueueNotification(NotificationType.Corner, "Lifesource Restored!", $"Consumed {item.itemName}. You feel invigorated, and your lifespan has been renewed.", NotificationPriority.Important, 5f);
            }

            RemoveItem(item);
            SaveData();
        }

        private void ConsumeWolfToken(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null || Globals.persistentWolfCalculator == null) return;

            PersistentWolf wolf = Globals.persistentWolfCalculator.GetRandomWolf(WolfLifeStage.Adult, false, null);
            wolf.WolfDef.isPlayerDescendant = false;
            wolf.WolfDef.raisedByPlayer = false;
            wolf.WolfDef.originPackName = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.ActualName;

            Vector3 spawnPos = Globals.animalManager.LocalPlayer.Position + Globals.animalManager.LocalPlayer.Physical.MovingPartTransform.forward * 5f;

            Animal animal = Globals.animalManager.LocalPlayer.Pack.MainFlock.ForceSpawnWolf(wolf, spawnPos);
            animal.Teleport(spawnPos, Quaternion.identity, GroundingMode.GroundUpwardOrDownwardFully, false);

            try
            {
                Flock playerFlock = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.MainFlock;
                if (animal.Flock != playerFlock)
                    playerFlock.TransferWolfFrom(animal.Flock, animal);
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AddMember(animal, true);
            }
            catch (System.Exception ex)
            {
                Globals.Log($"[RoleplayV2] Failed to add token wolf to pack: {ex}");
            }

            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null)
            {
                nc.QueueNotification(NotificationType.Corner, "A New Ally!", $"{animal.WolfDef.GUIFormattedName} has joined your pack!", NotificationPriority.Important, 5f);
            }

            RemoveItem(item);
        }

        private void ConsumePupToken(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null || Globals.persistentWolfCalculator == null) return;

            PlayerPackHandler pph = Globals.animalManager.LocalPlayer.Pack.PlayerPackData;
            if (pph == null || pph.PupsLifeStage != WolfLifeStage.SpringPup)
            {
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    nc.QueueNotification(NotificationType.Corner, "Cannot Use Token", "You can only use a Pup Token while raising pups in spring!", NotificationPriority.Important, 5f);
                }
                return;
            }

            PersistentWolf wolf = Globals.persistentWolfCalculator.GetRandomWolf(WolfLifeStage.SpringPup, false, null);
            wolf.WolfDef.birthName = "Adopted " + (wolf.WolfDef.sex == Sex.Male ? "(M)" : "(F)");
            wolf.WolfDef.nickName = wolf.WolfDef.birthName;
            wolf.WolfDef.isPlayerDescendant = false;
            wolf.WolfDef.raisedByPlayer = true;
            wolf.WolfDef.originPackName = pph.ActualName;
            wolf.WolfDef.weightInPounds = UnityEngine.Random.Range(5f, 8f);

            Vector3 spawnPos = Globals.animalManager.LocalPlayer.Position + Globals.animalManager.LocalPlayer.Physical.MovingPartTransform.forward * 2f;

            Animal animal = pph.MainFlock.ForceSpawnWolf(wolf, spawnPos);
            animal.Teleport(spawnPos, Quaternion.identity, GroundingMode.GroundUpwardOrDownwardFully, false);

            try
            {
                Flock playerFlock = pph.MainFlock;
                if (animal.Flock != playerFlock)
                    playerFlock.TransferWolfFrom(animal.Flock, animal);
                pph.AddMember(animal, true);
            }
            catch (System.Exception ex)
            {
                Globals.Log($"[RoleplayV2] Failed to add token pup to pack: {ex}");
            }

            var notificationControls = FindObjectOfType<NotificationControls>();
            if (notificationControls != null)
            {
                notificationControls.QueueNotification(NotificationType.Corner, "A New Pup!", $"{animal.WolfDef.GUIFormattedName} has joined your pack!", NotificationPriority.Important, 5f);
            }

            RemoveItem(item);
        }

        private void ConsumeDogCollar(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            // Find the targeting system to get the locked target
            var targetingControls = FindObjectOfType<TargetingControls>();
            if (targetingControls == null)
            {
                Globals.ShowMessage("Dog Collar", "No targeting system found.");
                return;
            }

            // Must have a locked target
            if (!targetingControls.IsManuallyLocked || targetingControls.TargetAnimal == null)
            {
                Globals.ShowMessage("Dog Collar", "You must lock onto a wolf first!");
                return;
            }

            Animal targetAnimal = targetingControls.TargetAnimal;

            // Must be a wolf (has WolfDef)
            if (targetAnimal.WolfDef == null)
            {
                Globals.ShowMessage("Dog Collar", "This can only be used on wolves!");
                return;
            }

            // Must NOT be in the player's pack already
            if (targetAnimal.IsInPlayerPack)
            {
                Globals.ShowMessage("Dog Collar", "This wolf is already in your pack!");
                return;
            }

            if (targetAnimal.State != null)
            {
                float healthPercent = targetAnimal.State.Health / targetAnimal.State.MaxHealth;
                if (healthPercent > 0.3f)
                {
                    Globals.ShowMessage("Dog Collar", "The wolf is too strong to be collared. Weaken it first! (<30% Health)");
                    return;
                }
            }

            // Transfer the wolf from its current flock into the player's MainFlock,
            // then register it as a pack member. Without flock transfer the wolf
            // stays in its old flock and won't follow the pack properly.
            try
            {
                Flock playerFlock = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.MainFlock;
                playerFlock.TransferWolfFrom(targetAnimal.Flock, targetAnimal);
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AddMember(targetAnimal, true);
            }
            catch (System.Exception ex)
            {
                Globals.Log($"[RoleplayV2] Failed to add collared wolf to pack: {ex}");
                Globals.ShowMessage("Dog Collar", "Something went wrong recruiting this wolf.");
                return;
            }

            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null)
            {
                nc.QueueNotification(NotificationType.Corner, "A New Ally!",
                    $"{targetAnimal.WolfDef.GUIFormattedName} has been tamed and joined your pack!",
                    NotificationPriority.Important, 5f);
            }

            RemoveItem(item);
        }

        private void ConsumeFixHerb(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var targetingControls = FindObjectOfType<TargetingControls>();
            Animal targetAnimal = (targetingControls != null && targetingControls.IsManuallyLocked && targetingControls.TargetAnimal != null) 
                ? targetingControls.TargetAnimal : Globals.animalManager.LocalPlayer;

            if (targetAnimal.WolfDef == null)
            {
                Globals.ShowMessage("Fix Herb", "This can only be used on wolves!");
                return;
            }

            if (targetAnimal.WolfState == null)
            {
                Globals.ShowMessage("Fix Herb", "Something went wrong. Target state is missing.");
                return;
            }

            bool healed = false;
            if (targetAnimal.WolfState.MinorInjuryIndex != -1)
            {
                targetAnimal.RaiseInjuryHealed(InjurySeverity.Minor, targetAnimal.WolfState.MinorInjuryIndex);
                targetAnimal.WolfState.MinorInjuryIndex = -1;
                healed = true;
            }
            if (targetAnimal.WolfState.MajorInjuryIndex != -1)
            {
                targetAnimal.RaiseInjuryHealed(InjurySeverity.Major, targetAnimal.WolfState.MajorInjuryIndex);
                targetAnimal.WolfState.MajorInjuryIndex = -1;
                healed = true;
            }

            if (healed)
            {
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    string targetName = targetAnimal == Globals.animalManager.LocalPlayer ? "You" : targetAnimal.WolfDef.GUIFormattedName;
                    string verb = targetAnimal == Globals.animalManager.LocalPlayer ? "have" : "has";
                    nc.QueueNotification(NotificationType.Corner, "Injuries Fixed!",
                        $"{targetName} {verb} been completely healed of all injuries.",
                        NotificationPriority.Important, 5f);
                }
                RemoveItem(item);
            }
            else
            {
                Globals.ShowMessage("Fix Herb", "Target has no injuries to fix.");
            }
        }

        private void ConsumeCureHerb(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var targetingControls = FindObjectOfType<TargetingControls>();
            Animal targetAnimal = (targetingControls != null && targetingControls.IsManuallyLocked && targetingControls.TargetAnimal != null) 
                ? targetingControls.TargetAnimal : Globals.animalManager.LocalPlayer;

            bool cured = false;
            if (targetAnimal.State != null)
            {
                if (targetAnimal.State.IsSickPup || targetAnimal.State.SicknessState != SicknessState.NotSick)
                {
                    targetAnimal.State.SicknessState = SicknessState.NotSick;
                    targetAnimal.State.SicknessHealthLossPerHour = 0f;

                    // WQ RPG: Reset the underlying sickness timers so the sickness doesn't immediately return on the next frame.
                    SicknessCalculator calculator = FindObjectOfType<SicknessCalculator>();
                    if (calculator != null && targetAnimal.WolfDef != null)
                    {
                        targetAnimal.State.RegularSicknessHourTimer = calculator.GetIntervalHours(targetAnimal.WolfDef, SicknessState.RegularSickness, false);
                        targetAnimal.State.StarvationSicknessHourTimer = calculator.GetIntervalHours(targetAnimal.WolfDef, SicknessState.StarvationSickness, false);
                    }
                    else
                    {
                        // Fallback (20 days)
                        targetAnimal.State.RegularSicknessHourTimer = 480f;
                        targetAnimal.State.StarvationSicknessHourTimer = 480f;
                    }

                    cured = true;
                }
                
                if (targetAnimal.State.IsPoisoned)
                {
                    targetAnimal.State.IsPoisoned = false;
                    targetAnimal.State.HealthToLoseFromPoison = 0f;
                    targetAnimal.State.HealthLostFromPoison = 0f;
                    cured = true;
                }
            }

            if (cured)
            {
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    string targetName = targetAnimal == Globals.animalManager.LocalPlayer ? "You" : targetAnimal.WolfDef.GUIFormattedName;
                    string verb = targetAnimal == Globals.animalManager.LocalPlayer ? "are" : "is";
                    nc.QueueNotification(NotificationType.Corner, "Sickness Cured!",
                        $"{targetName} {verb} no longer sick or poisoned.",
                        NotificationPriority.Important, 5f);
                }
                RemoveItem(item);
            }
            else
            {
                Globals.ShowMessage("Cure Herb", "Target is not sick or poisoned.");
            }
        }

        private void ConsumeHealHerb(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var targetingControls = FindObjectOfType<TargetingControls>();
            Animal targetAnimal = (targetingControls != null && targetingControls.IsManuallyLocked && targetingControls.TargetAnimal != null) 
                ? targetingControls.TargetAnimal : Globals.animalManager.LocalPlayer;

            if (targetAnimal.State == null)
            {
                Globals.ShowMessage("Heal Herb", "Something went wrong. Target state is missing.");
                return;
            }

            if (targetAnimal.State.Health < targetAnimal.State.MaxHealth)
            {
                targetAnimal.State.Health = targetAnimal.State.MaxHealth;

                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    string targetName = targetAnimal == Globals.animalManager.LocalPlayer ? "You" : targetAnimal.WolfDef.GUIFormattedName;
                    string verb = targetAnimal == Globals.animalManager.LocalPlayer ? "have" : "has";
                    nc.QueueNotification(NotificationType.Corner, "Fully Healed!",
                        $"{targetName} {verb} been fully healed.",
                        NotificationPriority.Important, 5f);
                }
                RemoveItem(item);
            }
            else
            {
                Globals.ShowMessage("Heal Herb", "Target is already at full health.");
            }
        }

        private void ConsumeItem(RPGDatabase.ItemData item)
        {
            var targetingControls = FindObjectOfType<TargetingControls>();
            Animal targetAnimal = (targetingControls != null && targetingControls.IsManuallyLocked && targetingControls.TargetAnimal != null) 
                ? targetingControls.TargetAnimal : Globals.animalManager.LocalPlayer;

            if (targetAnimal.State == null)
            {
                Globals.ShowMessage("Item Usage", "Something went wrong. Target state is missing.");
                return;
            }

            // Apply Effects
            string targetName = targetAnimal == Globals.animalManager.LocalPlayer ? "You" : targetAnimal.WolfDef.GUIFormattedName;

            bool effectApplied = false;

            if (item.healValue > 0)
            {
                if (targetAnimal.State.Health < targetAnimal.State.MaxHealth)
                {
                    targetAnimal.State.Health += item.healValue;
                    effectApplied = true;
                }
            }

            if (item.foodValue > 0)
            {
                targetAnimal.State.Food += item.foodValue;
                if (targetAnimal.State.Food > 100f) targetAnimal.State.Food = 100f;
                effectApplied = true;
            }

            if (effectApplied)
            {
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null && targetAnimal != Globals.animalManager.LocalPlayer)
                {
                    nc.QueueNotification(NotificationType.Corner, "Item Fed",
                        $"{targetName} was fed the {item.itemName}.",
                        NotificationPriority.Normal, 3f);
                }
                RemoveItem(item);
            }
            else
            {
                Globals.ShowMessage("Item Usage", $"{targetName} does not need that right now.");
            }
        }

        private void ConsumePoison(RPGDatabase.ItemData item)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var targetingControls = FindObjectOfType<TargetingControls>();
            Animal targetAnimal = (targetingControls != null && targetingControls.IsManuallyLocked && targetingControls.TargetAnimal != null) 
                ? targetingControls.TargetAnimal : Globals.animalManager.LocalPlayer;

            if (targetAnimal.WolfDef == null || targetAnimal.State == null)
            {
                Globals.ShowMessage("Poison", "This can only affect wolves or your state is missing.");
                return;
            }

            targetAnimal.State.IsPoisoned = true;
            // Use item.healValue as negative damage
            float poisonDamage = item.healValue > 0 ? item.healValue : 100f; // fallback if undefined
            targetAnimal.State.HealthToLoseFromPoison = targetAnimal.State.Health + poisonDamage; 

            var nc = FindObjectOfType<NotificationControls>();
            if (nc != null)
            {
                string targetName = targetAnimal == Globals.animalManager.LocalPlayer ? "You" : targetAnimal.WolfDef.GUIFormattedName;
                string verb = targetAnimal == Globals.animalManager.LocalPlayer ? "have" : "has";
                nc.QueueNotification(NotificationType.Corner, "Poisoned!",
                    $"{targetName} {verb} been poisoned!",
                    NotificationPriority.Important, 5f);
            }

            RemoveItem(item);
        }

        public void EquipItem(RPGDatabase.ItemData item)
        {
            string slot = item.category;

            // If the same slot is occupied, unequip first (swap)
            if (equipmentSlots.ContainsKey(slot))
            {
                UnequipItem(slot);
            }
            else if (equipmentSlots.Count >= MaxEquipSlots)
            {
                // Refuse if at cap and not swapping
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    nc.QueueNotification(NotificationType.Corner, "Equipment Full", "Unequip an item first! (Max 8)",
                        NotificationPriority.Important, 3f);
                }

                return;
            }

            if (playerInventory.Contains(item))
            {
                playerInventory.Remove(item);
                equipmentSlots[slot] = item;
                SaveData();
                RecalculateStats();
            }
        }

        public void UnequipItem(string slot)
        {
            Globals.Log($"[Roleplay] UnequipItem clicked for slot: {slot}");
            if (equipmentSlots.ContainsKey(slot))
            {
                var item = equipmentSlots[slot];
                equipmentSlots.Remove(slot);
                playerInventory.Add(item);
                SaveData();
                RecalculateStats();
            }
        }

        public void RecalculateStats()
        {
            _cachedStrength = baseStrength;
            _cachedAgility = baseAgility;
            _cachedStamina = baseStamina;
            _cachedHealth = baseHealth;

            foreach (var kvp in equipmentSlots)
            {
                var item = kvp.Value;
                _cachedStrength += item.modStrength;
                _cachedAgility += item.modAgility;
                _cachedStamina += item.modStamina;
                _cachedHealth += item.modHealth;
            }
        }

        public int GetTotalStat(string statName)
        {
            switch (statName)
            {
                case "Strength": return _cachedStrength;
                case "Agility": return _cachedAgility;
                case "Stamina": return _cachedStamina;
                case "Health": return _cachedHealth;
                default: return 0;
            }
        }

        public void ResetStats()
        {
            // Sum up all base stats
            int totalSpent = baseStrength + baseAgility + baseStamina + baseHealth;

            // Refund
            statPoints += totalSpent;

            // Reset
            baseStrength = 0;
            baseAgility = 0;
            baseStamina = 0;
            baseHealth = 0;

            SaveData();
            RecalculateStats();
        }

        // --- SAVE / LOAD ---

        public void SaveData()
        {
            if (Globals.animalManager?.LocalPlayer?.WolfDef != null)
            {
                RPGDataStore.SaveWolfData(Globals.animalManager.LocalPlayer.WolfDef, this);
            }
        }

        public void LoadData(WolfDefinition wolfDef = null)
        {
            // If wolfDef not provided, try to find it
            if (wolfDef == null)
            {
                if (Globals.animalManager?.LocalPlayer?.WolfDef != null)
                {
                    wolfDef = Globals.animalManager.LocalPlayer.WolfDef;
                }
            }

            if (wolfDef == null) return;

            bool loaded = RPGDataStore.LoadWolfData(wolfDef, this);

            if (!loaded)
            {
                // Reset to defaults if no save found
                currentLevel = 1;
                currentXP = 0;
                maxXP = 1000;
                statPoints = 0;
                currency = 50;
                baseStrength = 0;
                baseAgility = 0;
                baseStamina = 0;
                baseHealth = 0;
                playerInventory.Clear();
                equipmentSlots.Clear();
            }

            LastLoadedId = wolfDef.persistentId;
            RecalculateStats();
        }

        // --- GUI ---

        private Matrix4x4 _originalMatrix;
        private Vector2 _virtualRes = new Vector2(1920, 1080);

        void OnGUI()
        {
            if (!showMenus) return;
            if (Globals.MenuIsOpen) return;

            // Apply scaling matrix
            _originalMatrix = GUI.matrix;
            float scaleX = (float)Screen.width / _virtualRes.x;
            float scaleY = (float)Screen.height / _virtualRes.y;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1f));

            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(GUI.skin.window);
                windowStyle.normal.background = darkBackground;
                windowStyle.onNormal.background = darkBackground;
                windowStyle.active.background = darkBackground;
                windowStyle.onActive.background = darkBackground;
                windowStyle.focused.background = darkBackground;
                windowStyle.onFocused.background = darkBackground;
                windowStyle.hover.background = darkBackground;
                windowStyle.onHover.background = darkBackground;
                windowStyle.normal.textColor = Color.white;
                windowStyle.onNormal.textColor = Color.white;
                windowStyle.active.textColor = Color.white;
                windowStyle.onActive.textColor = Color.white;
                windowStyle.focused.textColor = Color.white;
                windowStyle.onFocused.textColor = Color.white;
                windowStyle.hover.textColor = Color.white;
                windowStyle.onHover.textColor = Color.white;
                windowStyle.fontSize = 16;
                windowStyle.alignment = TextAnchor.UpperCenter;
                windowStyle.padding = new RectOffset(4, 4, 20, 4);
            }

            // Lazy Init Background if missing
            if (darkBackground == null)
            {
                Globals.Log("[Roleplay] Warning: Re-creating darkBackground in OnGUI");
                darkBackground = new Texture2D(1, 1);
                darkBackground.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.95f));
                darkBackground.Apply();

                // Re-apply to style
                if (windowStyle != null)
                {
                    windowStyle.normal.background = darkBackground;
                    windowStyle.onNormal.background = darkBackground;
                    // ... (applying to all states is verbose, just main ones for now or re-create style)
                    windowStyle = null; // Force re-creation next frame
                }
            }

            if (!windowRectsInitialized)
            {
                float invW = 800, invH = 500;
                float stsW = 360, stsH = 420;
                float totalW = invW + 10 + stsW;
                float startX = (1920f - totalW) * .5f;
                float invY = (1080f - invH) * .5f;
                float stsY = (1080f - stsH) * .5f;
                inventoryWindowRect = new Rect(startX, invY, invW, invH);
                statsWindowRect = new Rect(startX + invW + 10, stsY, stsW, stsH);
                windowRectsInitialized = true;
            }

            inventoryWindowRect = GUI.Window(inventoryWindowId, inventoryWindowRect, DrawInventoryWindow, "Inventory",
                windowStyle);
            statsWindowRect = GUI.Window(statsWindowId, statsWindowRect, DrawStatsWindow, "Character Stats",
                windowStyle);

            GUI.matrix = _originalMatrix;
        }

        void DrawInventoryWindow(int windowId)
        {
            // Close Button (top-right, in local coords)
            if (GUI.Button(new Rect(inventoryWindowRect.width - 30, 5, 25, 25), "X"))
            {
                showMenus = false;
                InputControls.DisableInput = false;
                InputControls.DisableCameraInput = false;
                InputControls.ForceAllowCursor = false;
                SaveData();
            }

            DrawInventoryTab(0, 0);

            GUI.DragWindow(new Rect(0, 0, inventoryWindowRect.width - 35, 25));
        }

        private Vector2 backpackScrollPos;

        void DrawInventoryTab(float x, float y)
        {
            // Currency Display
            GUI.Label(new Rect(x + 550, y + 10, 200, 30), $"Coins: {currency}",
                new GUIStyle(GUI.skin.label)
                    { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight });

            // --- BACKPACK (Left Side) ---
            float gridX = x + 20;
            float gridY = y + 80;
            int columns = 4;
            float itemSize = 65;
            float padding = 12;
            float backpackWidth = columns * (itemSize + padding);
            float backpackHeight = 390;

            GUI.Label(new Rect(gridX, gridY - 25, 200, 20), "<b>Backpack</b>");

            // Calculate content height for scroll
            int backpackRows = (playerInventory.Count + columns - 1) / columns;
            float contentHeight = Mathf.Max(backpackHeight, backpackRows * (itemSize + 40 + padding));

            backpackScrollPos = GUI.BeginScrollView(
                new Rect(gridX, gridY, backpackWidth + 20, backpackHeight),
                backpackScrollPos,
                new Rect(0, 0, backpackWidth, contentHeight));

            for (int i = 0; i < playerInventory.Count; i++)
            {
                var item = playerInventory[i];
                int row = i / columns;
                int col = i % columns;
                float itemX = col * (itemSize + padding);
                float itemY = row * (itemSize + 40 + padding);
                Rect itemRect = new Rect(itemX, itemY, itemSize, itemSize);

                if (item.icon != null)
                {
                    if (GUI.Button(itemRect, item.icon))
                    {
                        UseItem(item);
                        break;
                    }
                }
                else
                {
                    if (GUI.Button(itemRect, item.id))
                    {
                        UseItem(item);
                        break;
                    }
                }

                GUIStyle smallLabel = new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.UpperCenter, fontSize = 10, wordWrap = true };
                GUI.Label(new Rect(itemX, itemY + itemSize, itemSize, 30), item.itemName, smallLabel);
                GUI.Label(new Rect(itemX, itemY + itemSize + 26, itemSize, 20), item.category,
                    new GUIStyle(GUI.skin.label)
                        { alignment = TextAnchor.UpperCenter, fontSize = 9, fontStyle = FontStyle.Italic });
            }

            GUI.EndScrollView();

            // --- EQUIPMENT GRID (Right Side, 2x4) ---
            float equipX = x + 420;
            float equipY = y + 80;
            float slotSize = 60;
            int equipCols = 2;
            float equipPad = 10;

            GUI.Label(new Rect(equipX, equipY - 25, 350, 20),
                $"<b>Equipped ({equipmentSlots.Count}/{MaxEquipSlots})</b>");

            int slotIndex = 0;
            foreach (var kvp in equipmentSlots)
            {
                string slotName = kvp.Key;
                var item = kvp.Value;

                int eRow = slotIndex / equipCols;
                int eCol = slotIndex % equipCols;

                float eBaseX = equipX + eCol * (slotSize + 130 + equipPad);
                float eBaseY = equipY + eRow * (slotSize + equipPad);

                Rect slotRect = new Rect(eBaseX, eBaseY, slotSize, slotSize);

                if (item.icon != null)
                {
                    if (GUI.Button(slotRect, item.icon)) UnequipItem(slotName);
                }
                else
                {
                    if (GUI.Button(slotRect, item.itemName)) UnequipItem(slotName);
                }

                // Labels beside the slot
                GUI.Label(new Rect(eBaseX + slotSize + 4, eBaseY, 130, 20), $"{slotName}",
                    new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold });
                GUI.Label(new Rect(eBaseX + slotSize + 4, eBaseY + 18, 130, 20), $"{item.itemName}",
                    new GUIStyle(GUI.skin.label) { fontSize = 10 });
                GUI.Label(new Rect(eBaseX + slotSize + 4, eBaseY + 34, 130, 20),
                    $"S:{item.modStrength} A:{item.modAgility} St:{item.modStamina} H:{item.modHealth}",
                    new GUIStyle(GUI.skin.label) { fontSize = 9 });

                slotIndex++;
            }
        }

        private void InitGUIStyles()
        {
            if (guiStylesInitialized) return;

            greenTex = new Texture2D(1, 1);
            greenTex.SetPixel(0, 0, new Color(0.3f, 0.8f, 0.3f, 0.9f));
            greenTex.Apply();

            headerStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            xpLabelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            colHeaderStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            nameRowStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            valRowStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            bonusRowStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            totalRowStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            pointsStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            hintStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
            hintStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            smallLabelStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.UpperCenter, fontSize = 10, wordWrap = true };
            categoryLabelStyle = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.UpperCenter, fontSize = 9, fontStyle = FontStyle.Italic };
            coinLabelStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };

            guiStylesInitialized = true;
        }

        void DrawStatsWindow(int windowId)
        {
            InitGUIStyles();

            float width = statsWindowRect.width;
            float height = statsWindowRect.height;

            // Close Button
            if (GUI.Button(new Rect(width - 30, 5, 25, 25), "X"))
            {
                showMenus = false;
                InputControls.DisableInput = false;
                InputControls.DisableCameraInput = false;
                InputControls.ForceAllowCursor = false;
                SaveData();
            }

            float pad = 20;
            float innerW = width - pad * 2;
            float cy = 40;

            // --- TABS ---
            GUI.BeginGroup(new Rect(pad, cy, innerW, 30));
            if (GUI.Toggle(new Rect(0, 0, innerW / 2, 25), activeTab == StatsTab.Stats, "Stats", "button"))
            {
                activeTab = StatsTab.Stats;
            }
            if (GUI.Toggle(new Rect(innerW / 2, 0, innerW / 2, 25), activeTab == StatsTab.Kills, "Kills", "button"))
            {
                activeTab = StatsTab.Kills;
            }
            GUI.EndGroup();
            
            cy += 40;

            if (activeTab == StatsTab.Stats)
            {
                DrawStatsTab(pad, cy, innerW, height, width);
            }
            else
            {
                DrawKillsTab(pad, cy, innerW, height, width);
            }

            // --- Hint ---
            GUI.Label(new Rect(pad, height - 30, innerW, 20), $"Press [{Plugin.CfgInventoryKey.Value}] to close",
                hintStyle);

            GUI.DragWindow(new Rect(0, 0, width - 35, 25));
        }

        private Vector2 killsScrollPos;

        private void DrawKillsTab(float pad, float cy, float innerW, float height, float width)
        {
            GUI.Label(new Rect(pad, cy, innerW, 28), "Kill Counters", headerStyle);
            cy += 35;

            // --- Separator ---
            GUI.Box(new Rect(pad, cy, innerW, 1), "");
            cy += 10;

            float listHeight = height - cy - 40;
            float contentHeight = killCounters.Count * 30;

            killsScrollPos = GUI.BeginScrollView(
                new Rect(pad, cy, innerW + 10, listHeight),
                killsScrollPos,
                new Rect(0, 0, innerW - 10, contentHeight));

            float yOffset = 0;
            
            if (killCounters.Count == 0)
            {
                GUI.Label(new Rect(0, yOffset, innerW, 25), "No kills recorded yet.", valRowStyle);
            }
            else
            {
                // Sort by highest kills first
                var sortedKills = new List<KeyValuePair<string, int>>(killCounters);
                sortedKills.Sort((a, b) => b.Value.CompareTo(a.Value));

                foreach (var entry in sortedKills)
                {
                    GUI.Label(new Rect(10, yOffset, 150, 25), entry.Key, nameRowStyle);
                    GUI.Label(new Rect(180, yOffset, 60, 25), entry.Value.ToString(), valRowStyle);
                    yOffset += 30;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawStatsTab(float pad, float cy, float innerW, float height, float width)
        {
            // --- Level & XP ---
            GUI.Label(new Rect(pad, cy, innerW, 28), $"Level {currentLevel}", headerStyle);
            cy += 30;

            // XP Bar
            float barH = 18;
            GUI.Box(new Rect(pad, cy, innerW, barH), "");
            float xpFill = (maxXP > 0) ? (float)currentXP / maxXP : 0f;
            GUI.DrawTexture(new Rect(pad + 1, cy + 1, (innerW - 2) * xpFill, barH - 2), greenTex);
            GUI.Label(new Rect(pad, cy, innerW, barH), $"{currentXP} / {maxXP} XP", xpLabelStyle);
            cy += barH + 14;

            // --- Separator ---
            GUI.Box(new Rect(pad, cy, innerW, 1), "");
            cy += 8;

            // --- Stats Table Headers ---
            GUI.Label(new Rect(pad + 100, cy, 55, 20), "Base", colHeaderStyle);
            GUI.Label(new Rect(pad + 155, cy, 55, 20), "Equip", colHeaderStyle);
            GUI.Label(new Rect(pad + 210, cy, 55, 20), "Total", colHeaderStyle);
            cy += 22;

            DrawStatRowNew("Strength", ref baseStrength, pad, cy, innerW);
            cy += 36;
            DrawStatRowNew("Agility", ref baseAgility, pad, cy, innerW);
            cy += 36;
            DrawStatRowNew("Stamina", ref baseStamina, pad, cy, innerW);
            cy += 36;
            DrawStatRowNew("Health", ref baseHealth, pad, cy, innerW);
            cy += 42;

            // --- Stat Points (below stats) ---
            GUI.Box(new Rect(pad, cy, innerW, 1), "");
            cy += 8;
            pointsStyle.normal.textColor = statPoints > 0 ? new Color(0.4f, 1f, 0.4f) : Color.white;
            GUI.Label(new Rect(pad, cy, innerW, 22), $"Stat Points: {statPoints}", pointsStyle);

            // --- Reset Button ---
            if (GUI.Button(new Rect(width - 110, cy, 90, 22), "Reset Stats"))
            {
                ResetStats();
            }
        }

        void DrawStatRowNew(string name, ref int baseStat, float lx, float y, float w)
        {
            int total = GetTotalStat(name);
            int bonus = total - baseStat;

            bonusRowStyle.normal.textColor = bonus > 0 ? new Color(0.5f, 0.9f, 0.5f) : Color.white;

            GUI.Label(new Rect(lx, y, 95, 28), name, nameRowStyle);
            GUI.Label(new Rect(lx + 100, y, 55, 28), $"{baseStat}", valRowStyle);
            GUI.Label(new Rect(lx + 155, y, 55, 28), bonus > 0 ? $"+{bonus}" : "0", bonusRowStyle);
            GUI.Label(new Rect(lx + 210, y, 55, 28), $"{total}", totalRowStyle);

            if (statPoints > 0)
            {
                if (GUI.Button(new Rect(lx + 275, y + 2, 30, 24), "+"))
                {
                    Globals.Log($"[Roleplay] Increasing stat: {name}");
                    baseStat++;
                    statPoints--;
                    SaveData();
                    RecalculateStats();
                }
            }
        }
    }
}
