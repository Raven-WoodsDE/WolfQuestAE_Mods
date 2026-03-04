using BepInEx;
using HarmonyLib;
using WolfQuestEp3;
using SharedCommons;

namespace GameKeys
{
    [BepInPlugin("com.rw.gamekeys", "GameKeys", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(Patch1));
            Harmony.CreateAndPatchAll(typeof(Patch2));

            Globals.Log("[GameKeys] Initialized.");
        }
    }

    [HarmonyPatch(typeof(GameVariantManager))]
    public static class Patch1
    {
        [HarmonyPatch("IsSteamVersion", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_IsSteamVersion(ref bool __result)
        {
            __result = false;
            return false;
        }

        [HarmonyPatch("IsItchIoVersion", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_IsItchIoVersion(ref bool __result)
        {
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DlcManager))]
    public static class Patch2
    {
        [HarmonyPatch("IsCoatUnlocked")]
        [HarmonyPrefix]
        public static bool Prefix_IsCoatUnlocked(ref bool __result)
        {
            __result = true;
            return false;
        }

        [HarmonyPatch("WasDlcOwned")]
        [HarmonyPrefix]
        public static bool Prefix_WasDlcOwned(ref bool __result)
        {
            __result = true;
            return false;
        }

        [HarmonyPatch("IsDlcOwned")]
        [HarmonyPrefix]
        public static bool Prefix_IsDlcOwned(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
}