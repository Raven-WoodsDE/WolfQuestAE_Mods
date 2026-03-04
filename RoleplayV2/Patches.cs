using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using System.Reflection;
using SharedCommons;

namespace WQRPG
{
    public static class Patches
    {
        // --- 1. XP PATCH ---
        // Hook into when an animal dies (or takes damage leading to death)
        // Assuming 'Animal.TakeDamage' returns bool indicating death, or we check health after.
        // Or finding a 'Death' method. 'Kill' is often used.
        // We'll try Postfix on TakeDamage to see if it died.

        // --- 1. XP PATCH ---
        // Hook into HealthUpdater.TakeDamage to detect when an animal dies.
        // We use Prefix to capture health before damage, and Postfix to check if it died.

        [HarmonyPatch(typeof(WolfQuestEp3.HealthUpdater), "TakeDamage")]
        public static class HealthUpdater_TakeDamage_Patch
        {
            // Capture health before damage
            public static void Prefix(WolfQuestEp3.HealthUpdater __instance, WolfQuestEp3.AnimalState ___state,
                out float __state)
            {
                __state = ___state != null ? ___state.Health : 0f;
            }

            public static void Postfix(WolfQuestEp3.HealthUpdater __instance, WolfQuestEp3.AnimalState ___state,
                WolfQuestEp3.Animal ___self, WolfQuestEp3.Animal attacker, float __state)
            {
                // __state is health BEFORE damage
                // ___state.Health is health AFTER damage

                if (___state != null && ___self != null && attacker != null)
                {
                    // Check if it died explicitly from this hit (Health was > 0, now <= 0)
                    if (__state > 0 && ___state.Health <= 0)
                    {
                        // Check if killer is player or packmate
                        if (attacker.IsPlayer || (Globals.animalManager?.LocalPlayer != null &&
                                                  attacker.Pack == Globals.animalManager?.LocalPlayer.Pack))
                        {
                            // Calculate XP based on animal type/strength
                            int xpGain = CalculateXP(___self);

                            // Add to Player
                            if (InventoryManager.Instance != null)
                            {
                                InventoryManager.Instance.AddXP(xpGain);

                                // Add Currency (1 coin per 10 XP, min 1)
                                int coins = Mathf.Max(1, xpGain / 10);
                                InventoryManager.Instance.AddCurrency(coins);

                                // Roll loot drops for this species
                                ProcessLootDrop(___self);

                                // Add to Kill Counters (Only track if we have a valid species name)
                                if (___self.Species != null && !string.IsNullOrEmpty(___self.Species.name))
                                {
                                    string speciesName = ___self.Species.name;
                                    InventoryManager.Instance.AddKill(speciesName);
                                }
                            }
                        }
                    }
                }
            }
        }

        // PATCH: Animal.get_Abilities
        // This patch integrates the RPG stats directly into the game's core ability system.
        // It overwrites the calculated abilities with the Total Stats from InventoryManager.
        [HarmonyPatch(typeof(WolfQuestEp3.Animal), "get_Abilities")]
        public static class Animal_GetAbilities_Patch
        {
            public static void Postfix(WolfQuestEp3.Animal __instance, ref WolfQuestEp3.AbilityBlock __result)
            {
                if (InventoryManager.Instance == null) return;

                // Only apply to the local player's animal to avoid affecting NPCs
                if (__instance.IsLocalPlayer)
                {
                    // Overwrite the result with our RPG stats
                    // The RPG stats (Base + Equipment) become the "Effective" stats for the game logic.
                    __result.strength = InventoryManager.Instance.GetTotalStat("Strength");
                    __result.agility = InventoryManager.Instance.GetTotalStat("Agility");
                    __result.health = InventoryManager.Instance.GetTotalStat("Health");
                    __result.stamina = InventoryManager.Instance.GetTotalStat("Stamina");
                }
                // Also apply BASE stats to Packmates (synched with player level)
                else if (Globals.animalManager?.LocalPlayer != null &&
                         __instance.Pack == Globals.animalManager?.LocalPlayer.Pack)
                {
                    __result.strength = InventoryManager.Instance.baseStrength;
                    __result.agility = InventoryManager.Instance.baseAgility;
                    __result.health = InventoryManager.Instance.baseHealth;
                    __result.stamina = InventoryManager.Instance.baseStamina;
                }
            }
        }

        // PATCH: WolfDefinition.get_InnateAbilitiesCheatProof
        // Prevents the game from clamping stats to the [-3, 2] range, allowing our higher RPG stats to work if read from here.
        [HarmonyPatch(typeof(WolfQuestEp3.WolfDefinition), "get_InnateAbilitiesCheatProof")]
        public static class WolfDefinition_InnateAbilitiesCheatProof_Patch
        {
            static FieldInfo innateAbilitiesField =
                AccessTools.Field(typeof(WolfQuestEp3.WolfDefinition), "innateAbilities");

            public static bool Prefix(WolfQuestEp3.WolfDefinition __instance, ref WolfQuestEp3.AbilityBlock __result)
            {
                if (innateAbilitiesField != null)
                {
                    __result = (WolfQuestEp3.AbilityBlock)innateAbilitiesField.GetValue(__instance);
                    return false; // Skip the original method (which contains the clamp)
                }

                return true; // Fallback if reflection fails
            }
        }

        private static int CalculateXP(Animal target)
        {
            // Use MaxHealth as base XP
            if (target != null)
            {
                return Mathf.CeilToInt(target.State.MaxHealth);
            }

            return 10; // Fallback
        }

        private static void ProcessLootDrop(Animal target)
        {
            if (Plugin._rpgDatabase == null || target == null || target.Species == null)
                return;

            string speciesName = target.Species.name;

            if (!Plugin._rpgDatabase.lootTable.ContainsKey(speciesName))
                return;

            var entries = Plugin._rpgDatabase.lootTable[speciesName];

            foreach (var entry in entries)
            {
                // Roll drop chance
                if (Random.value > entry.dropChance)
                    continue;

                // Determine amount
                int amount = Random.Range(entry.minAmount, entry.maxAmount + 1);
                if (amount <= 0) continue;

                // Find the item
                if (!Plugin._rpgDatabase.allItems.ContainsKey(entry.itemId))
                    continue;

                var template = Plugin._rpgDatabase.allItems[entry.itemId];

                // Add to inventory (once per amount) - clone each so they're independent
                for (int i = 0; i < amount; i++)
                {
                    var clone = new RPGDatabase.ItemData
                    {
                        id = template.id, itemName = template.itemName, icon = template.icon,
                        category = template.category, price = template.price,
                        modStrength = template.modStrength, modAgility = template.modAgility,
                        modStamina = template.modStamina, modHealth = template.modHealth,
                        foodValue = template.foodValue, healValue = template.healValue
                    };
                    InventoryManager.Instance.AddItem(clone);
                }

                // Notify player
                Globals.ShowMessage("Loot Drop", $"+{amount} {template.itemName}");

                Globals.Log($"[RoleplayV2] Loot: {amount}x {template.itemName} from {speciesName}");
            }
        }

        // --- 4. SAVE/LOAD TRIGGERS ---

        // PATCH: WolfSaveManager.SaveWolf
        // Trigger RPG Data Save when the game saves the wolf.
        [HarmonyPatch(typeof(WolfQuestEp3.WolfSaveManager), "SaveWolf")]
        public static class WolfSaveManager_SaveWolf_Patch
        {
            public static void Postfix(WolfQuestEp3.WolfDefinition __result)
            {
                if (InventoryManager.Instance != null && __result != null)
                {
                    InventoryManager.Instance.SaveData();
                }
            }
        }

        // --- 5. DISABLE HUD TOGGLE ---

        // PATCH: GameplaySettingsControls.UpdateControlsAlways
        // Prevents the game from checking for the "Toggle HUD" input, effectively disabling the keybind.
        [HarmonyPatch(typeof(WolfQuestEp3.GameplaySettingsControls), "UpdateControlsAlways")]
        public static class GameplaySettingsControls_UpdateControlsAlways_Patch
        {
            public static bool Prefix()
            {
                return false; // Skip original method (Disables HUD toggle input)
            }
        }
        // --- 6. FIX UI SCALING ---

        // PATCH: MovableBackgroundBar.UpdatePosition
        // Clamps the ratio to 1.0f to prevent the background bar from growing infinitely when stats > default.
        [HarmonyPatch(typeof(WolfQuestEp3.MovableBackgroundBar), "UpdatePosition")]
        public static class MovableBackgroundBar_UpdatePosition_Patch
        {
            public static void Prefix(ref float unclampedRatio)
            {
                unclampedRatio = Mathf.Min(unclampedRatio, 1.0f);
            }
        }

        // PATCH: MovableFillBar.UpdatePosition
        // Clamps the ratio to 1.0f to prevent the fill bar from exceeding the background bar.
        [HarmonyPatch(typeof(WolfQuestEp3.MovableFillBar), "UpdatePosition")]
        public static class MovableFillBar_UpdatePosition_Patch
        {
            public static void Prefix(ref float unclampedRatio)
            {
                unclampedRatio = Mathf.Min(unclampedRatio, 1.0f);
            }
        }
    }
}
