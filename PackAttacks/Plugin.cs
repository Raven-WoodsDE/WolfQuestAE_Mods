using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;

namespace PackAttacks
{
    [BepInPlugin("com.rw.packattacks", "PackAttacks", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            Harmony.CreateAndPatchAll(typeof(Plugin), null);
            Logger.LogInfo("PackAttacks Plugin Initialized!");
        }

        // Allow multiple attackers to grab the same point by always pretending segments are unoccupied
        [HarmonyPatch(typeof(TargetingCalculator), "GetBitePointsSegmentOccupier")]
        [HarmonyPostfix]
        private static void GetBitePointsSegmentOccupier_Postfix(ref Animal __result)
        {
            // By forcefully returning null, the game logic will think the bite point segment is free,
            // allowing unlimited wolves to latch onto the same segment simultaneously.
            __result = null;
        }

        // Deal double damage when hitting HeadLeft or HeadRight
        [HarmonyPatch(typeof(HealthUpdater), "TakeDamage")]
        [HarmonyPrefix]
        private static void TakeDamage_Prefix(ref int damage, AttackSegment attackSegment, Animal attacker)
        {
            // Only amplify damage if we actually have an attacker and are hitting the head segments
            if (attacker != null && damage > 0)
            {
                if (attackSegment == AttackSegment.HeadLeft || attackSegment == AttackSegment.HeadRight)
                {
                    damage *= 2;
                }
            }
        }
    }
}
