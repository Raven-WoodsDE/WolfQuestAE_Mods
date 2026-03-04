using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BepInEx;
using WolfQuestEp3;
using SharedCommons;
using
    Newtonsoft.Json; // Using JSON for easier serialization/deserialization if available, or just standard text/binary. 
// Actually, InventoryManager used pipe/comma separated text. I'll stick to a simple clean format or JSON. 
// WolfQuest uses BepInEx, which usually has JSON capability or we can just use `JsonUtility` from Unity.
// `JsonUtility` is safest and built-in.

namespace WQRPG
{
    [System.Serializable]
    public class WolfData
    {
        public int persistentId;
        public string wolfName;

        // RPG Stats
        public int currentLevel = 1;
        public int currentXP = 0;
        public int maxXP = 1000;
        public int statPoints = 0;
        public int currency = 0;

        // Base Stats
        public int baseStrength = 0;
        public int baseAgility = 0;
        public int baseStamina = 0;
        public int baseHealth = 0;

        // Inventory: List of Item IDs
        public List<string> inventoryIds = new List<string>();

        // Equipment: parallel lists (JsonUtility reliably serializes List<string>)
        public List<string> equipSlotNames = new List<string>();
        public List<string> equipItemIds = new List<string>();

        // Kill Counters
        public List<string> killedSpecies = new List<string>();
        public List<int> killedCounts = new List<int>();
    }

    // Kept for backward compatibility when reading old saves
    [System.Serializable]
    public class EquipmentSlotData
    {
        public string slot;
        public string itemId;
    }

    public static class RPGDataStore
    {
        private static string SaveDirectory =>
            Path.Combine(BepInEx.Paths.PluginPath, "roleplay_assets", "saves", "rpg_data");

        public static void SaveWolfData(WolfDefinition wolfDef, InventoryManager manager)
        {
            if (manager == null || wolfDef == null)
            {
                Globals.Log($"[RPGDataStore] SaveWolfData called with null manager or wolfDef");
                return;
            }

            string wolfPath = wolfDef.filePath;

            // Fallback: If runtime wolf has no path, check the SaveManager's record
            if (string.IsNullOrEmpty(wolfPath))
            {
                var saveManager = UnityEngine.Object.FindObjectOfType<WolfSaveManager>();
                if (saveManager != null)
                {
                    var savedWolf = saveManager.FindWolf(wolfDef.persistentId);
                    if (savedWolf != null)
                    {
                        wolfPath = savedWolf.filePath;
                    }
                }
            }

            if (string.IsNullOrEmpty(wolfPath))
            {
                Globals.Log(
                    $"[RPGDataStore] SaveWolfData: wolfDef.filePath is null/empty for '{wolfDef.NickNameOrBirthName}'. Cannot save.");
                return;
            }

            string rpgPath = Path.ChangeExtension(wolfPath, ".rpgdat");

            WolfData data = new WolfData();
            data.persistentId = wolfDef.persistentId;
            data.wolfName = wolfDef.NickNameOrBirthName;

            data.currentLevel = manager.currentLevel;
            data.currentXP = manager.currentXP;
            data.maxXP = manager.maxXP;
            data.statPoints = manager.statPoints;
            data.currency = manager.currency;

            data.baseStrength = manager.baseStrength;
            data.baseAgility = manager.baseAgility;
            data.baseStamina = manager.baseStamina;
            data.baseHealth = manager.baseHealth;

            Globals.Integers["rp_strength"] = data.baseStrength;
            Globals.Integers["rp_agility"] = data.baseAgility;
            Globals.Integers["rp_stamina"] = data.baseStamina;
            Globals.Integers["rp_health"] = data.baseHealth;

            foreach (var item in manager.playerInventory)
            {
                data.inventoryIds.Add(item.id);
            }

            foreach (var kvp in manager.equipmentSlots)
            {
                data.equipSlotNames.Add(kvp.Key);
                data.equipItemIds.Add(kvp.Value.id);
            }

            foreach (var kvp in manager.killCounters)
            {
                data.killedSpecies.Add(kvp.Key);
                data.killedCounts.Add(kvp.Value);
            }

            string json = JsonUtility.ToJson(data, true);

            try
            {
                string dir = Path.GetDirectoryName(rpgPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(rpgPath, json);
                Globals.Log(
                    $"[RPGDataStore] Saved RPG data to {rpgPath} (Inv:{data.inventoryIds.Count}, Equip:{data.equipSlotNames.Count}, Coins:{data.currency})");
            }
            catch (System.Exception ex)
            {
                Globals.Log($"[RPGDataStore] Failed to save RPG data to {rpgPath}: {ex.Message}");
            }
        }

        public static bool LoadWolfData(WolfDefinition wolfDef, InventoryManager manager)
        {
            if (wolfDef == null) return false;

            string wolfPath = wolfDef.filePath;
            Globals.Log($"[RPGDataStore] wolfDef.filePath: {wolfDef.filePath}");

            if (string.IsNullOrEmpty(wolfPath))
            {
                Globals.Log(
                    $"[RPGDataStore] LoadWolfData: wolfDef.filePath is null/empty for '{wolfDef.NickNameOrBirthName}'. Cannot load.");
                return false;
            }

            string rpgPath = Path.ChangeExtension(wolfPath, ".rpgdat");
            Globals.Log($"[RPGDataStore] Attempting to load RPG data from: {rpgPath}");

            if (!File.Exists(rpgPath))
            {
                Globals.Log($"[RPGDataStore] No save file found at: {rpgPath}");
                return false;
            }

            try
            {
                string json = File.ReadAllText(rpgPath);
                WolfData data = JsonUtility.FromJson<WolfData>(json);

                if (data == null) return false;

                manager.currentLevel = data.currentLevel;
                manager.currentXP = data.currentXP;
                manager.maxXP = data.maxXP;
                manager.statPoints = data.statPoints;
                manager.currency = data.currency;

                manager.baseStrength = data.baseStrength;
                manager.baseAgility = data.baseAgility;
                manager.baseStamina = data.baseStamina;
                manager.baseHealth = data.baseHealth;

                manager.playerInventory.Clear();
                if (data.inventoryIds != null)
                {
                    foreach (string id in data.inventoryIds)
                    {
                        if (Plugin._rpgDatabase.allItems.ContainsKey(id))
                        {
                            manager.playerInventory.Add(CloneItem(Plugin._rpgDatabase.allItems[id]));
                        }
                        else
                        {
                            Globals.Log(
                                $"[RPGDataStore] Saved inventory item '{id}' not found in database — item was lost!");
                        }
                    }
                }

                manager.equipmentSlots.Clear();
                if (data.equipSlotNames != null && data.equipItemIds != null)
                {
                    int count = Mathf.Min(data.equipSlotNames.Count, data.equipItemIds.Count);
                    for (int i = 0; i < count; i++)
                    {
                        string slot = data.equipSlotNames[i];
                        string itemId = data.equipItemIds[i];
                        if (Plugin._rpgDatabase.allItems.ContainsKey(itemId))
                        {
                            manager.equipmentSlots[slot] = CloneItem(Plugin._rpgDatabase.allItems[itemId]);
                        }
                        else
                        {
                            Globals.Log(
                                $"[RPGDataStore] Saved equipment item '{itemId}' (slot '{slot}') not found in database — item was lost!");
                        }
                    }
                }

                manager.killCounters.Clear();
                if (data.killedSpecies != null && data.killedCounts != null)
                {
                    int kcCount = Mathf.Min(data.killedSpecies.Count, data.killedCounts.Count);
                    for (int i = 0; i < kcCount; i++)
                    {
                        manager.killCounters[data.killedSpecies[i]] = data.killedCounts[i];
                    }
                }

                if (Globals.Integers.ContainsKey("rp_strength") == false)
                {
                    Globals.Integers.Add("rp_strength", data.baseStrength);
                }
                else
                {
                    Globals.Integers["rp_strength"] = data.baseStrength;
                }

                if (Globals.Integers.ContainsKey("rp_agility") == false)
                {
                    Globals.Integers.Add("rp_agility", data.baseAgility);
                }
                else
                {
                    Globals.Integers["rp_agility"] = data.baseAgility;
                }

                if (Globals.Integers.ContainsKey("rp_stamina") == false)
                {
                    Globals.Integers.Add("rp_stamina", data.baseStamina);
                }
                else
                {
                    Globals.Integers["rp_stamina"] = data.baseStamina;
                }

                if (Globals.Integers.ContainsKey("rp_health") == false)
                {
                    Globals.Integers.Add("rp_health", data.baseHealth);
                }
                else
                {
                    Globals.Integers["rp_health"] = data.baseHealth;
                }

                Globals.Log(
                    $"[RPGDataStore] Loaded RPG data: Lv{data.currentLevel}, Inv:{manager.playerInventory.Count}, Equip:{manager.equipmentSlots.Count}, Coins:{manager.currency}");
                return true;
            }
            catch (System.Exception e)
            {
                Globals.Log(
                    $"[RPGDataStore] Exception loading data for wolf {wolfDef.NickNameOrBirthName}: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        private static RPGDatabase.ItemData CloneItem(RPGDatabase.ItemData template)
        {
            return new RPGDatabase.ItemData
            {
                id = template.id, itemName = template.itemName, icon = template.icon,
                category = template.category, price = template.price,
                modStrength = template.modStrength, modAgility = template.modAgility,
                modStamina = template.modStamina, modHealth = template.modHealth,
                foodValue = template.foodValue, healValue = template.healValue
            };
        }
    }
}
