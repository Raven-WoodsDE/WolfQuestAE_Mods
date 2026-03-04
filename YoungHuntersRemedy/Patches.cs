using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;

namespace YoungHuntersRemedy
{
    [HarmonyPatch(typeof(HealthUpdater))]
    public static class Patches
    {
        [HarmonyPatch("ApplyXpBasedDamage")]
        [HarmonyPrefix]
        public static bool ApplyXpBasedDamagePrefix(HealthUpdater __instance, ref int __result, int damage, DamageType damageType, Animal attacker, HealthUpdaterSetup ___setup, FudgingManager ___fudgingManager, Animal ___self)
        {
            if (!Plugin.EnableDamageXpBypass.Value) return true;

            // Only intervene if it's a Player Pup, specifically a Young Hunter.
            if (___self.IsWolf && ___self.IsPlayerPup && ___self.WolfDef.lifeStage == WolfLifeStage.YoungHunter)
            {
                if (___setup.huntingDamageByXp.Length != 0 && (damageType == DamageType.Blunt || damageType == DamageType.Struggle) && ___self.Senses.GetAnimalsByDistance(AnimalRelationship.DangerousPrey).Contains(attacker))
                {
                    // We completely bypass the fudging multipliers and XP based weakness array.
                    // Just return the base damage.
                    __result = damage;
                    return false; // Skip original method
                }
            }
            return true; // Run original method for everyone else
        }

        [HarmonyPatch("ApplyDifficultyToDamage")]
        [HarmonyPrefix]
        public static bool ApplyDifficultyToDamagePrefix(HealthUpdater __instance, ref int __result, int damage, Animal attacker, DifficultyManager ___difficultyManager, Animal ___self)
        {
            if (!Plugin.EnableDamageDifficultyOverride.Value) return true;

            if (___self.IsWolf && ___self.IsPlayerPup && ___self.WolfDef.lifeStage == WolfLifeStage.YoungHunter)
            {
                // IsPlayerPup normally forces DamageMultiplierNPCToPlayerPup for Young Hunters. 
                // We'll override that and give them the flat NPC to NPC modifier, similar to adults.
                if (!___self.IsPlayer && attacker != null && !attacker.IsPlayer)
                {
                    __result = Mathf.CeilToInt((float)damage * ___difficultyManager.DamageMultiplierNPCToNPC);
                    return false; // Skip original method
                }
            }
            return true; // Run original method
        }

        [HarmonyPatch("ApplyAdultWolfCriticalHits")]
        [HarmonyPrefix]
        public static bool ApplyAdultWolfCriticalHitsPrefix(HealthUpdater __instance, ref int damage, DamageType damageType, Animal attacker, HealthUpdaterSetup ___setup, FudgingManager ___fudgingManager, DifficultyManager ___difficultyManager, Animal ___self, AnimalState ___state)
        {
            if (!Plugin.EnableAdultCriticalHits.Value) return true;

            // The original method excludes Young Hunters with `lifeStage > WolfLifeStage.YoungHunter`.
            // We want to manually execute the adult critical hit logic FOR Young Hunters, then let the original run (since it'll ignore them anyway) or just skip it.
            if (___difficultyManager.PackmateCriticalHitsEnabled && ___self.IsWolf && !___self.IsPlayer && ___self.WolfDef.lifeStage == WolfLifeStage.YoungHunter && attacker != null)
            {
                bool flag = damageType != DamageType.Struggle && (___self.Senses.GetAnimalsByDistance(AnimalRelationship.RivalToFight).Contains(attacker) || ___self.Senses.GetAnimalsByDistance(AnimalRelationship.RivalToAvoid).Contains(attacker));
                bool flag2 = damageType == DamageType.Blunt && ___self.Senses.GetAnimalsByDistance(AnimalRelationship.DangerousPrey).Contains(attacker);
                if (flag || flag2)
                {
                    CriticalHitsSetup criticalHitsSetup;
                    if (___self.IsInPlayerPack)
                    {
                        criticalHitsSetup = (flag ? ___setup.adultCriticalHitsFighting : ___setup.adultCriticalHitsHunting);
                    }
                    else
                    {
                        criticalHitsSetup = ___setup.criticalHitsAgainstRivals;
                    }
                    
                    float normalDamagePerHit = criticalHitsSetup.normalDamagePerCriticalHit;
                    if (normalDamagePerHit <= 0f) normalDamagePerHit = 1f; // Safety
                    
                    float num = (float)damage / normalDamagePerHit;
                    if (___self.IsInPlayerPack)
                    {
                        num *= ___fudgingManager.AdultFudgingNeed;
                    }
                    
                    ___state.DebugHealthLossWithCriticalPotentialThisYear += (float)damage;
                    
                    if (num > 0f && UnityEngine.Random.value < num)
                    {
                        int num2 = damage;
                        damage = Mathf.RoundToInt((float)(damage + UnityEngine.Random.Range(criticalHitsSetup.minExtraDamage, criticalHitsSetup.maxExtraDamage + 1)));
                        Debug.Log($"Young Hunters Remedy: Applied adult critical hit to {___self.WolfDef.GUIFormattedName}, damage before: {num2}, damage after: {damage}");
                    }
                }
            }
            // Let the original method run. It checks `lifeStage > WolfLifeStage.YoungHunter`, so it will peacefully do nothing for Young Hunters.
            return true; 
        }
    }
}
