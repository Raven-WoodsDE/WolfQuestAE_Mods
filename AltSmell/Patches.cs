using HarmonyLib;
using WolfQuestEp3;
using SharedCommons;

public class Patches
{
    [HarmonyPatch(typeof(CameraControls), "SwitchToScentCamera")]
    [HarmonyPrefix]
    public static bool Prefix_SwitchToScentCamera()
    {
        return false;
    }

    [HarmonyPatch(typeof(InputControls), "get_ScentCameraPressed")]
    [HarmonyPrefix]
    public static bool Prefix_ScentCameraPressed(ref bool __result)
    {
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(ScentParticleRenderer), "AddParticle")]
    [HarmonyPrefix]
    public static bool Prefix_AddParticle(ref int __result)
    {
        __result = -1;
        return false;
    }

    [HarmonyPatch(typeof(ScentParticleRenderer), "UpdateRendering")]
    [HarmonyPrefix]
    public static bool Prefix_ScentUpdateRendering()
    {
        return false;
    }

    [HarmonyPatch(typeof(ScentParticleRenderer), "AddToCamera")]
    [HarmonyPrefix]
    public static bool Prefix_ScentAddToCamera()
    {
        return false;
    }

    [HarmonyPatch(typeof(TerritoryMarkingRenderer), "MarkingCreated")]
    [HarmonyPrefix]
    public static bool Prefix_MarkingCreated()
    {
        return false;
    }

    [HarmonyPatch(typeof(TerritoryMarkingRenderer), "UpdateRendering")]
    [HarmonyPrefix]
    public static bool Prefix_TerritoryUpdateRendering()
    {
        return false;
    }

    [HarmonyPatch(typeof(TerritoryMarkingRenderer), "AddToCamera")]
    [HarmonyPrefix]
    public static bool Prefix_TerritoryAddToCamera()
    {
        return false;
    }
}