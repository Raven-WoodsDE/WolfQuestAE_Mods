using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using BepInEx;
using SharedCommons;

namespace WQRPG
{
    public class RPGDatabase
    {
        // Paths
        private string rootPath;
        private string itemsPath;
        private string shopsPath;

        // The actual "Database" in memory
        public Dictionary<string, ItemData> allItems = new Dictionary<string, ItemData>();
        public List<ShopData> allShops = new List<ShopData>();
        public Dictionary<string, List<LootEntry>> lootTable = new Dictionary<string, List<LootEntry>>();

        // --- DATA STRUCTURES ---

        [System.Serializable]
        public class ItemData
        {
            public string id; // The filename without extension, used as ID
            public string itemName;
            public Texture2D icon;
            public string category;
            public int price;

            // Stats
            public int modStrength;
            public int modAgility;
            public int modStamina;
            public int modHealth;

            // Consumable values
            public float foodValue;
            public float healValue;
        }

        [System.Serializable]
        public class ShopData
        {
            public string mapName;
            public Vector3 position;
            public float rotationY;
            public string shopName;
            public int productCount;
            public List<ItemData> inventory = new List<ItemData>();
            public Dictionary<string, int> itemStock = new Dictionary<string, int>();
        }

        [System.Serializable]
        public class LootEntry
        {
            public string itemId; // filename (without .item) in roleplay_assets/items/
            public float dropChance; // 0.0 – 1.0
            public int minAmount;
            public int maxAmount;
        }

        // --- LOADING LOGIC ---

        public void Init()
        {
            // Define paths based on where your mod dll is, or a fixed path
            // Assuming BepInEx structure: BepInEx/plugins/WQRPG/roleplay_assets/
            rootPath = Path.Combine(BepInEx.Paths.PluginPath, "roleplay_assets/");
            itemsPath = Path.Combine(rootPath, "items/");
            shopsPath = Path.Combine(rootPath, "shops/");

            // 1. Load Items first (so shops and loot can reference them)
            LoadItems();

            // 2. Load Loot Table
            LoadLootTable();

            // 3. Load Shops
            LoadShops();
        }

        private void LoadItems()
        {
            if (!Directory.Exists(itemsPath))
            {
                Globals.Log($"Items directory not found at: {itemsPath}");
                return;
            }

            string[] files = Directory.GetFiles(itemsPath, "*.item");

            foreach (string file in files)
            {
                try
                {
                    string fileNameID = Path.GetFileNameWithoutExtension(file);
                    string content = File.ReadAllText(file);
                    string[] data = content.Split('|');

                    ItemData newItem = new ItemData();
                    newItem.id = fileNameID;
                    newItem.itemName = data[0];

                    // Load Icon
                    string iconName = data[1];
                    newItem.icon = LoadTexture(Path.Combine(itemsPath, iconName));

                    newItem.category = data[2];
                    int.TryParse(data[3], out newItem.price);
                    int.TryParse(data[4], out newItem.modStrength);
                    int.TryParse(data[5], out newItem.modAgility);
                    int.TryParse(data[6], out newItem.modStamina);
                    int.TryParse(data[7], out newItem.modHealth);
                    float.TryParse(data[8], out newItem.foodValue);
                    float.TryParse(data[9], out newItem.healValue);

                    // Add to dictionary
                    if (!allItems.ContainsKey(newItem.id))
                        allItems.Add(newItem.id, newItem);
                }
                catch (System.Exception e)
                {
                    Globals.Log($"Error parsing item {file}: {e.Message}");
                }
            }
        }

        private void LoadShops()
        {
            if (!Directory.Exists(shopsPath)) return;

            string[] files = Directory.GetFiles(shopsPath, "*.shop");

            foreach (string file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    string[] data = content.Split('|');

                    ShopData newShop = new ShopData();
                    newShop.mapName = data[0];

                    // Parse Position
                    float x, y, z, rot;
                    float.TryParse(data[1], out x);
                    float.TryParse(data[2], out y);
                    float.TryParse(data[3], out z);
                    float.TryParse(data[4], out rot);

                    // WQ uses Y as up, so X and Z are the map coordinates
                    // Usually you need to find the correct Y height (terrain height), 
                    // but for now we set it to 0 or a raycast logic later.
                    newShop.position = new Vector3(x, y, z);
                    newShop.rotationY = rot;

                    newShop.shopName = data[5];

                    // Parse Inventory List (comma separated) or Product Count (single number)
                    string inventoryData = data[6].Trim();
                    if (int.TryParse(inventoryData, out int pCount))
                    {
                        newShop.productCount = pCount;
                    }
                    else
                    {
                        string[] itemIds = inventoryData.Split(',');
                        foreach (string itemId in itemIds)
                        {
                            string cleanId = itemId.Trim();
                            if (allItems.ContainsKey(cleanId))
                            {
                                newShop.inventory.Add(allItems[cleanId]);
                            }
                        }
                        newShop.productCount = newShop.inventory.Count;
                    }

                    allShops.Add(newShop);
                }
                catch (System.Exception e)
                {
                    Globals.Log($"Error parsing shop {file}: {e.Message}");
                }
            }
        }

        private void LoadLootTable()
        {
            string lootPath = Path.Combine(Paths.PluginPath, "roleplay_assets", "loot_table.json");
            if (!File.Exists(lootPath))
            {
                Globals.Log($"[WQRPG] Loot table not found at: {lootPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(lootPath);
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, List<LootEntry>>>(json);

                if (parsed == null)
                {
                    Globals.Log("[WQRPG] Loot table parsed as null.");
                    return;
                }

                int totalEntries = 0;
                foreach (var kvp in parsed)
                {
                    // Validate that referenced items exist
                    var validEntries = new List<LootEntry>();
                    foreach (var entry in kvp.Value)
                    {
                        if (!allItems.ContainsKey(entry.itemId))
                        {
                            Globals.Log(
                                $"[WQRPG] Loot table references unknown item '{entry.itemId}' for species '{kvp.Key}'. Skipping.");
                            continue;
                        }

                        validEntries.Add(entry);
                    }

                    if (validEntries.Count > 0)
                    {
                        lootTable[kvp.Key] = validEntries;
                        totalEntries += validEntries.Count;
                    }
                }

                Globals.Log($"[WQRPG] Loaded loot table: {lootTable.Count} species, {totalEntries} entries.");
            }
            catch (System.Exception e)
            {
                Globals.Log($"[WQRPG] Error loading loot table: {e.Message}");
            }
        }

        public void RandomizeAllShops()
        {
            if (allItems.Count == 0) return;

            var itemList = allItems.Values.ToList();
            var rng = new System.Random();

            foreach (var shop in allShops)
            {
                shop.inventory.Clear();
                shop.itemStock.Clear();
                int maxCount = Mathf.Clamp(shop.productCount, 0, itemList.Count);
                int count = maxCount > 0 ? rng.Next(1, maxCount + 1) : 0;
                
                // Shuffle a copy of the list to pick unique items
                var shuffled = itemList.OrderBy(x => rng.Next()).ToList();
                for (int i = 0; i < count; i++)
                {
                    var item = shuffled[i];
                    shop.inventory.Add(item);
                    shop.itemStock[item.id] = rng.Next(1, 6); // 1 to 5 stock
                }
            }

            Globals.Log($"[WQRPG] Randomized {allShops.Count} shops.");
        }

        // Helper to load images from disk
        private Texture2D LoadTexture(string path)
        {
            if (File.Exists(path))
            {
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData); // This auto-resizes the texture dimensions
                return tex;
            }

            return null; // Or return a default 'missing' texture
        }
    }
}