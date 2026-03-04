using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using WolfQuestEp3;
using SharedCommons;

namespace WQRPG
{
    public class MenuScript : MonoBehaviour
    {
        public static MenuScript Instance;

        // State
        private bool showMenu;
        private bool closeEnough;
        private Vector3 playerPos;
        private float tmrCheckDistance;

        // The Data for this specific shop instance
        public RPGDatabase.ShopData currentShopData;

        // GUI Styles
        private GUIStyle windowStyle;
        private Texture2D darkBackground;
        private Matrix4x4 _originalMatrix;
        private Vector2 _virtualRes = new Vector2(1920, 1080);

        // Scroll positions
        private Vector2 buyScrollPos;
        private Vector2 sellScrollPos;

        public void Init(RPGDatabase.ShopData data)
        {
            Instance = this;
            currentShopData = data;

            // Set the physical position of this shop NPC based on the data
            transform.rotation = Quaternion.Euler(0, data.rotationY, 0);

            // Create background texture
            darkBackground = new Texture2D(1, 1);
            darkBackground.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            darkBackground.Apply();
        }

        public void Update()
        {
            if (Globals.MenuIsOpen) return;

            tmrCheckDistance += Time.deltaTime;
            if (tmrCheckDistance > 0.5f) 
            {
                if (Globals.animalManager?.LocalPlayer != null)
                {
                    playerPos = Globals.animalManager.LocalPlayer.Position;
                    float distSq = (playerPos - transform.position).sqrMagnitude;
                    closeEnough = distSq < 16; // 4 meters range (4*4=16)
                }

                tmrCheckDistance = 0f;
            }

            if (closeEnough)
            {
                if (Input.GetKeyDown(Plugin.CfgInteractKey.Value) && !showMenu)
                {
                    OpenMenu();
                }
                else if ((Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(Plugin.CfgInteractKey.Value)) && showMenu)
                {
                    CloseMenu();
                }
            }
            else
            {
                if (showMenu) CloseMenu();
            }
        }

        void OpenMenu()
        {
            showMenu = true;
            
            if (InventoryManager.Instance != null)
                InventoryManager.Instance.ForceClose();

            InputControls.DisableCameraInput = true;
            InputControls.DisableInput = true;
            InputControls.ForceAllowCursor = true;
        }

        void CloseMenu()
        {
            showMenu = false;
            InputControls.DisableCameraInput = false;
            InputControls.DisableInput = false;
            InputControls.ForceAllowCursor = false;
        }

        void OnGUI()
        {
            if (Globals.MenuIsOpen) return;

            if (currentShopData == null) return;

            if (!closeEnough) return;

            // Init style lazily here (not in Update, which can be skipped)
            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(GUI.skin.box);
                windowStyle.normal.background = darkBackground;
                windowStyle.normal.textColor = Color.white;
                windowStyle.fontSize = 16;
                windowStyle.alignment = TextAnchor.UpperCenter;
            }

            // Apply scaling matrix
            _originalMatrix = GUI.matrix;
            float scaleX = (float)Screen.width / _virtualRes.x;
            float scaleY = (float)Screen.height / _virtualRes.y;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1f));

            if (!showMenu)
            {
                // Draw floating "Talk" label above head
                Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.5f);
                if (screenPos.z > 0)
                {
                    float dist = Vector3.Distance(playerPos, transform.position);
                    // Scale label size by distance slightly
                    float size = Mathf.Clamp(2000 / dist, 50, 150);

                    // Revert matrix for world-to-screen points as they are in pixel coords
                    GUI.matrix = _originalMatrix;
                    GUI.Label(new Rect(screenPos.x - (size * .5f), Screen.height - screenPos.y, size, 50),
                        $"[{Plugin.CfgInteractKey.Value}] {currentShopData.shopName}");
                    // Restore scaling matrix
                    GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1f));
                }
            }
            else
            {
                try
                {
                    DrawShopInterface();
                }
                catch (Exception ex)
                {
                    Globals.Log($"[RoleplayV2] DrawShopInterface error: {ex}");
                }
            }

            // Restore original matrix
            GUI.matrix = _originalMatrix;
        }

        // UI State
        private int selectedTab = 0; // 0 = Buy, 1 = Sell

        void DrawShopInterface()
        {
            float width = 700;
            float height = 500;
            float x = (1920f - width) * .5f;
            float y = (1080f - height) * .5f;

            // Main Background
            GUI.Box(new Rect(x, y, width, height), "", windowStyle);

            // Header
            GUI.Label(new Rect(x, y + 10, width, 30), $"<b>{currentShopData.shopName}</b>", windowStyle);

            // Close Button
            if (GUI.Button(new Rect(x + width - 35, y + 5, 30, 30), "X")) CloseMenu();

            // Tabs
            if (GUI.Button(new Rect(x + 20, y + 45, 100, 30), "Buy")) selectedTab = 0;
            if (GUI.Button(new Rect(x + 130, y + 45, 100, 30), "Sell")) selectedTab = 1;

            // Currency Display
            int cash = (InventoryManager.Instance != null) ? InventoryManager.Instance.currency : 0;
            GUI.Label(new Rect(x + width - 200, y + 45, 180, 30), $"Cash: ${cash}",
                new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.MiddleRight, fontSize = 14, fontStyle = FontStyle.Bold });

            if (selectedTab == 0)
            {
                DrawBuyTab(x, y);
            }
            else
            {
                DrawSellTab(x, y);
            }
        }

        void DrawBuyTab(float x, float y)
        {
            float itemSize = 80;
            float padding = 20;
            int columns = 5;
            float totalItemWidth = columns * (itemSize + padding) - padding;
            float startX = (700 - totalItemWidth) * .5f;
            float startY = 10;

            var inventory = currentShopData.inventory;

            // Group by category
            var groupedInventory = inventory.GroupBy(i => 
            {
                if (string.IsNullOrEmpty(i.category)) return "Uncategorized";
                if (i.category.IndexOf("herb", StringComparison.OrdinalIgnoreCase) >= 0) return "Herb";
                return i.category;
            }).OrderBy(g => g.Key).ToList();

            float rowHeight = itemSize + 60 + padding;
            float calculatedHeight = startY;
            foreach (var group in groupedInventory) {
                calculatedHeight += 40; // Header height
                int groupRows = (group.Count() + columns - 1) / columns;
                calculatedHeight += groupRows * rowHeight;
            }
            float contentHeight = Mathf.Max(380, calculatedHeight);

            // Scroll view
            buyScrollPos = GUI.BeginScrollView(
                new Rect(x + 10, y + 90, 680, 390),
                buyScrollPos,
                new Rect(0, 0, 660, contentHeight));

            float currentY = startY;

            foreach (var group in groupedInventory)
            {
                // Draw category header
                GUIStyle headerStyle = new GUIStyle(windowStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 18, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(startX, currentY, 660, 30), group.Key, headerStyle);
                currentY += 40;

                var groupList = group.ToList();
                int groupRows = (groupList.Count + columns - 1) / columns;

                for (int i = 0; i < groupList.Count; i++)
                {
                    var item = groupList[i];

                    int row = i / columns;
                    int col = i % columns;

                    float itemX = startX + (col * (itemSize + padding));
                    float itemY = currentY + (row * rowHeight);

                    Rect iconRect = new Rect(itemX, itemY, itemSize, itemSize);

                    if (item.icon != null)
                    {
                        if (GUI.Button(iconRect, item.icon)) BuyItem(item);
                    }
                    else
                    {
                        if (GUI.Button(iconRect, item.id)) BuyItem(item);
                    }

                    GUIStyle centeredSmall = new GUIStyle(GUI.skin.label)
                        { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                    GUI.Label(new Rect(itemX, itemY + itemSize + 2, itemSize, 40), item.itemName, centeredSmall);
                    int stock = currentShopData.itemStock.ContainsKey(item.id) ? currentShopData.itemStock[item.id] : 0;
                    GUI.Label(new Rect(itemX, itemY + itemSize + 42, itemSize, 20), $"${item.price} ({stock} left)", centeredSmall);
                }

                currentY += groupRows * rowHeight;
            }

            GUI.EndScrollView();
        }

        void DrawSellTab(float x, float y)
        {
            if (InventoryManager.Instance == null) return;

            float itemSize = 80;
            float padding = 20;
            int columns = 5;
            float totalItemWidth = columns * (itemSize + padding) - padding;
            float startX = (700 - totalItemWidth) * .5f;
            float startY = 10;

            var playerInv = InventoryManager.Instance.playerInventory;

            // Build the sell list: faux items first, then player inventory
            // Count faux items to offset grid positions
            int fauxCount = 0;
            bool holdingPup = false;
            bool holdingMeat = false;
            bool holdingToy = false;
            Toy heldToy = null;
            int toySellPrice = 0;

            if (Globals.animalManager?.LocalPlayer != null)
            {
                var objInMouth = Globals.animalManager.LocalPlayer.State.ObjectInMouth;
                if (objInMouth != null)
                {
                    if (objInMouth.AsAnimal() != null)
                    {
                        holdingPup = true;
                        fauxCount++;
                    }
                    else if (objInMouth.AsMeatObject() != null)
                    {
                        holdingMeat = true;
                        fauxCount++;
                    }
                    else if (objInMouth.AsToy() != null)
                    {
                        heldToy = objInMouth.AsToy();
                        holdingToy = true;
                        toySellPrice = GetToySellPrice(heldToy);
                        fauxCount++;
                    }
                }
            }

            int totalItems = fauxCount + playerInv.GroupBy(i => i.id).Count();
            int rows = (totalItems + columns - 1) / columns;
            float contentHeight = Mathf.Max(380, rows * (itemSize + 60 + padding));

            // Scroll view
            sellScrollPos = GUI.BeginScrollView(
                new Rect(x + 10, y + 90, 680, 390),
                sellScrollPos,
                new Rect(0, 0, 660, contentHeight));

            int gridIndex = 0;

            // --- Faux: Sell Pup ---
            if (holdingPup)
            {
                int row = gridIndex / columns;
                int col = gridIndex % columns;
                float itemX = startX + (col * (itemSize + padding));
                float itemY = startY + (row * (itemSize + 60 + padding));
                Rect iconRect = new Rect(itemX, itemY, itemSize, itemSize);

                GUIStyle fauxStyle = new GUIStyle(GUI.skin.button);
                fauxStyle.normal.textColor = new Color(1f, 0.6f, 0.6f);
                fauxStyle.fontStyle = FontStyle.Bold;
                fauxStyle.fontSize = 11;

                if (GUI.Button(iconRect, "Sell\nPup", fauxStyle))
                {
                    SellHeldPup();
                }

                GUIStyle centeredSmall = new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                GUI.Label(new Rect(itemX, itemY + itemSize + 2, itemSize, 40), "Held Pup", centeredSmall);
                GUI.Label(new Rect(itemX, itemY + itemSize + 42, itemSize, 20), "Sell: $100", centeredSmall);
                gridIndex++;
            }

            // --- Faux: Sell Meat ---
            if (holdingMeat)
            {
                int row = gridIndex / columns;
                int col = gridIndex % columns;
                float itemX = startX + (col * (itemSize + padding));
                float itemY = startY + (row * (itemSize + 60 + padding));
                Rect iconRect = new Rect(itemX, itemY, itemSize, itemSize);

                GUIStyle fauxStyle = new GUIStyle(GUI.skin.button);
                fauxStyle.normal.textColor = new Color(0.8f, 1f, 0.6f);
                fauxStyle.fontStyle = FontStyle.Bold;
                fauxStyle.fontSize = 11;

                if (GUI.Button(iconRect, "Sell\nMeat", fauxStyle))
                {
                    SellHeldMeat();
                }

                GUIStyle centeredSmall = new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                GUI.Label(new Rect(itemX, itemY + itemSize + 2, itemSize, 40), "Held Meat", centeredSmall);
                GUI.Label(new Rect(itemX, itemY + itemSize + 42, itemSize, 20), "Sell: $25", centeredSmall);
                gridIndex++;
            }

            // --- Faux: Sell Toy ---
            if (holdingToy && heldToy != null)
            {
                int row = gridIndex / columns;
                int col = gridIndex % columns;
                float itemX = startX + (col * (itemSize + padding));
                float itemY = startY + (row * (itemSize + 60 + padding));
                Rect iconRect = new Rect(itemX, itemY, itemSize, itemSize);

                GUIStyle fauxStyle = new GUIStyle(GUI.skin.button);
                fauxStyle.normal.textColor = new Color(0.6f, 0.9f, 1f);
                fauxStyle.fontStyle = FontStyle.Bold;
                fauxStyle.fontSize = 11;

                if (GUI.Button(iconRect, "Sell\nToy", fauxStyle))
                {
                    SellHeldToy(toySellPrice);
                }

                // Get a display name from the toy's game object name
                string toyDisplayName = heldToy.gameObject.name.Replace("(Clone)", "").Trim();

                GUIStyle centeredSmall = new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                GUI.Label(new Rect(itemX, itemY + itemSize + 2, itemSize, 40), toyDisplayName, centeredSmall);
                GUI.Label(new Rect(itemX, itemY + itemSize + 42, itemSize, 20), $"Sell: ${toySellPrice}", centeredSmall);
                gridIndex++;
            }

            // --- Regular Inventory Items ---
            var groupedInv = playerInv.GroupBy(i => i.id).ToList();

            for (int i = 0; i < groupedInv.Count; i++)
            {
                var group = groupedInv[i];
                var firstItem = group.First();
                int itemCount = group.Count();
                
                int idx = gridIndex + i;

                int row = idx / columns;
                int col = idx % columns;

                float itemX = startX + (col * (itemSize + padding));
                float itemY = startY + (row * (itemSize + 60 + padding));

                Rect iconRect = new Rect(itemX, itemY, itemSize, itemSize);

                // Sell Price is 50%
                int sellPrice = Mathf.Max(1, Mathf.FloorToInt(firstItem.price * 0.5f));

                if (firstItem.icon != null)
                {
                    if (GUI.Button(iconRect, firstItem.icon))
                    {
                        SellItemGroup(group, sellPrice);
                        break;
                    }
                }
                else
                {
                    if (GUI.Button(iconRect, firstItem.id))
                    {
                        SellItemGroup(group, sellPrice);
                        break;
                    }
                }

                GUIStyle centeredSmall = new GUIStyle(GUI.skin.label)
                    { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                GUI.Label(new Rect(itemX, itemY + itemSize + 2, itemSize, 40), firstItem.itemName, centeredSmall);
                GUI.Label(new Rect(itemX, itemY + itemSize + 42, itemSize, 20), $"Sell: ${sellPrice}", centeredSmall);

                // Draw Stack Count
                if (itemCount > 1) {
                    GUIStyle countStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 14, fontStyle = FontStyle.Bold };
                    GUI.Label(new Rect(itemX, itemY, itemSize - 4, itemSize - 4), itemCount.ToString(), countStyle);
                }
            }

            GUI.EndScrollView();
        }

        void SellHeldPup()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var player = Globals.animalManager.LocalPlayer;
            var objInMouth = player.State.ObjectInMouth;
            if (objInMouth == null) return;

            var animal = objInMouth.AsAnimal();
            if (animal == null) return;

            // Kill the pup and clear from mouth
            animal.State.Health = 0;
            player.State.ObjectInMouth = null;

            // Move far away
            animal.transform.position = new Vector3(0, -1000, 0);

            // Give coins
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.AddCurrency(100);
            }
        }

        void SellHeldMeat()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var player = Globals.animalManager.LocalPlayer;
            var objInMouth = player.State.ObjectInMouth;
            if (objInMouth == null) return;

            var meat = objInMouth.AsMeatObject();
            if (meat == null) return;

            // Clear from mouth
            player.State.ObjectInMouth = null;

            // Move meat far away to despawn
            if (meat.gameObject != null)
            {
                meat.gameObject.transform.position = new Vector3(0, -1000, 0);
            }

            // Give coins
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.AddCurrency(25);
            }
        }

        void SellHeldToy(int sellPrice)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            var player = Globals.animalManager.LocalPlayer;
            var objInMouth = player.State.ObjectInMouth;
            if (objInMouth == null) return;

            var toy = objInMouth.AsToy();
            if (toy == null) return;

            // Clear from mouth
            player.State.ObjectInMouth = null;

            // Move to a random far-away position on the map
            Vector3 playerPos = player.Position;
            Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
            float distance = UnityEngine.Random.Range(500f, 800f);
            Vector3 farPos = new Vector3(
                playerPos.x + randomDir.x * distance,
                playerPos.y,
                playerPos.z + randomDir.y * distance
            );
            toy.gameObject.transform.position = farPos;

            // Give coins
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.AddCurrency(sellPrice);
            }
        }

        int GetToySellPrice(Toy toy)
        {
            if (Plugin._rpgDatabase != null)
            {
                // Clean the prefab name (remove "(Clone)" suffix Unity adds)
                string toyName = toy.gameObject.name.Replace("(Clone)", "").Trim();

                // Search database items for a matching ID (case-insensitive)
                foreach (var kvp in Plugin._rpgDatabase.allItems)
                {
                    if (string.Equals(kvp.Key, toyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Mathf.Max(1, Mathf.FloorToInt(kvp.Value.price * 0.5f));
                    }
                }
            }

            // Fallback prices based on type
            switch (toy.SpecificType)
            {
                case ToySpecificType.Antler: return 75;
                case ToySpecificType.Skull:  return 100;
                default: return 50;
            }
        }

        void BuyItem(RPGDatabase.ItemData item)
        {
            if (InventoryManager.Instance == null) return;
            
            int stock = currentShopData.itemStock.ContainsKey(item.id) ? currentShopData.itemStock[item.id] : 0;
            if (stock <= 0)
            {
                var nc = FindObjectOfType<NotificationControls>();
                if (nc != null)
                {
                    nc.QueueNotification(NotificationType.Corner, "Out of Stock", $"{item.itemName} is currently out of stock.", NotificationPriority.Low, 3f);
                }
                return;
            }

            if (InventoryManager.Instance.currency >= item.price)
            {
                InventoryManager.Instance.AddCurrency(-item.price);
                currentShopData.itemStock[item.id]--;
                
                // Clone so each purchase is an independent instance
                var clone = new RPGDatabase.ItemData
                {
                    id = item.id, itemName = item.itemName, icon = item.icon,
                    category = item.category, price = item.price,
                    modStrength = item.modStrength, modAgility = item.modAgility,
                    modStamina = item.modStamina, modHealth = item.modHealth,
                    foodValue = item.foodValue, healValue = item.healValue
                };
                InventoryManager.Instance.AddItem(clone);
            }
        }

        void SellItem(RPGDatabase.ItemData item, int price)
        {
            if (InventoryManager.Instance == null) return;

            InventoryManager.Instance.AddCurrency(price);
            InventoryManager.Instance.RemoveItem(item);
        }

        void SellItemGroup(IEnumerable<RPGDatabase.ItemData> items, int unitPrice)
        {
            if (InventoryManager.Instance == null) return;

            var itemsList = items.ToList();
            if (itemsList.Count == 0) return;

            if (Input.GetKey(Plugin.CfgSellOneKey.Value))
            {
                SellItem(itemsList.First(), unitPrice);
            }
            else
            {
                foreach(var item in itemsList) {
                    SellItem(item, unitPrice);
                }
            }
        }
    }
}