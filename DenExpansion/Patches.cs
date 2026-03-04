using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace DenExpansion
{
    // ==================== PACKMANAGER PATCH ====================
    // Bypasses the cheat-proof clamping on innate abilities so the
    // pack editor sliders can set values beyond normal limits.

    [HarmonyPatch(typeof(WolfDefinition))]
    public static class InnateAbilitiesCheatProofPatch
    {
        [HarmonyPatch("InnateAbilitiesCheatProof", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix(WolfDefinition __instance, ref AbilityBlock __result)
        {
            __result = __instance.innateAbilities;
            return false; // skip original
        }
    }

    // ==================== DEN EXPANSION PATCHES ====================

    // Allow rendezvous site mission to complete when all pups reach 30 lbs
    // OR the player already has a home site.
    [HarmonyPatch(typeof(FindRendezvousSiteMission))]
    [HarmonyPatch("IsCompleted", MethodType.Getter)]
    public static class FindRendezvousSiteMissionPatch
    {
        static bool Prefix(FindRendezvousSiteMission __instance, ref bool __result)
        {
            if (!Plugin.CfgEnableDenExpansion.Value) return true;

            var field = typeof(FindRendezvousSiteMission).GetField("playerWolfPack",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null) return true;

            var playerWolfPack = field.GetValue(__instance) as PlayerPackHandler;
            if (playerWolfPack == null) return true;

            // Check if all pups have reached 30 lbs
            bool allPupsReady = true;
            foreach (var pup in playerWolfPack.Pups)
            {
                if (pup?.WolfDef != null && pup.WolfDef.weightInPounds < 30f)
                {
                    allPupsReady = false;
                    break;
                }
            }

            // Complete if pups at 30 lbs, has rendezvous site, or has home
            __result = allPupsReady
                    || playerWolfPack.HasRendezvousSite
                    || (Globals.animalManager.LocalPlayer.Pack != null
                        && Globals.animalManager.LocalPlayer.Pack.HasHome);

            return false; // skip original
        }
    }

    // Starving time mission completes when smallest pup weighs >= 60 lbs.
    [HarmonyPatch(typeof(StarvingTimeMission))]
    [HarmonyPatch("IsCompleted", MethodType.Getter)]
    public static class StarvingTimeMissionPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!Plugin.CfgEnableDenExpansion.Value) return true;

            __result = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.SmallestPup.WolfDef.weightInPounds >= 60;
            return false;
        }
    }

    // ==================== PUP AUTONAMER PATCH ====================

    [HarmonyPatch(typeof(NewbornPupsControls))]
    [HarmonyPatch("StartNamingPups")]
    public static class StartNamingPupsPatch
    {
        private static string GetRandomName(Sex sex, HashSet<string> existingNames)
        {
            var nameList = sex == Sex.Female ? Plugin.FemaleNames : Plugin.MaleNames;
            var shuffled = nameList.OrderBy(x => UnityEngine.Random.value).ToList();

            foreach (var name in shuffled)
            {
                if (!existingNames.Contains(name))
                    return name;
            }

            string baseName = shuffled.FirstOrDefault() ?? "Wolf";
            int counter = 2;
            while (existingNames.Contains($"{baseName} {counter}"))
                counter++;

            return $"{baseName} {counter}";
        }

        static bool Prefix(NewbornPupsControls __instance)
        {
            if (Plugin.CfgAutoNamePups.Value)
            {
                if (Globals.animalManager?.LocalPlayer?.Pack != null &&
                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups != null)
                {
                    // Collect existing names
                    HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Members)
                    {
                        if (member?.WolfDef != null && !string.IsNullOrEmpty(member.WolfDef.nickName))
                            existingNames.Add(member.WolfDef.nickName);
                    }

                    foreach (var pup in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups)
                    {
                        if (pup?.WolfDef != null && !string.IsNullOrEmpty(pup.WolfDef.nickName))
                            existingNames.Add(pup.WolfDef.nickName);
                    }

                    // Auto-name unnamed pups
                    foreach (var pup in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups)
                    {
                        if (pup?.WolfDef != null && string.IsNullOrEmpty(pup.WolfDef.nickName))
                        {
                            string newName = GetRandomName(pup.WolfDef.sex, existingNames);
                            pup.ChangeWolfNickname(newName);
                            existingNames.Add(newName);
                        }
                    }
                }
            }

            // Skip naming screen if configured
            if (Plugin.CfgSkipNamingMission.Value)
            {
                __instance.PupsNamed();
                return false;
            }

            return true;
        }
    }
}

