using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using XFurStudio;
using SharedCommons;

namespace Customizer
{
    /// <summary>
    /// Harmony patches for custom skins and eye colors.
    /// Uses WolfDef biography field for customization.
    /// </summary>
    [HarmonyPatch(typeof(WolfCosmetics))]
    public static class SkinsPatches
    {
        /// <summary>
        /// Postfix patch for SetEye - Applies custom eye color from biography.
        /// Biography format: eyes=r,g,b (RGB values 0-255)
        /// </summary>
        [HarmonyPatch("SetEye")]
        [HarmonyPostfix]
        public static void SetEye_Postfix(WolfDefinition wolfDefinition, WolfCosmetics __instance)
        {
            if (wolfDefinition == null || string.IsNullOrEmpty(wolfDefinition.biography))
                return;

            try
            {
                var data = Plugin.ParseBiography(wolfDefinition.biography);

                if (!data.HasCustomEyes)
                    return;

                // Get private fields via Traverse
                var lodRenderers = Traverse.Create(__instance)
                    .Field("lodRenderers").GetValue<List<SkinnedMeshRenderer>>();
                var lod0Renderer = Traverse.Create(__instance)
                    .Field("lod0Renderer").GetValue<SkinnedMeshRenderer>();
                int eyeIndex = Traverse.Create(__instance)
                    .Field("eyeIndexInWolfMeshRenderer").GetValue<int>();

                // Apply to all LOD renderers
                if (lodRenderers != null)
                {
                    foreach (var r in lodRenderers)
                    {
                        if (r != null && r.materials.Length > eyeIndex)
                        {
                            r.materials[eyeIndex].SetColor("_EyeColor", data.EyeColor);
                        }
                    }
                }

                // Apply to LOD0 renderer
                if (lod0Renderer != null && lod0Renderer.materials.Length > eyeIndex)
                {
                    lod0Renderer.materials[eyeIndex].SetColor("_EyeColor", data.EyeColor);
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Error applying custom eyes: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix patch for SetCoat - Applies custom skin texture from biography.
        /// Biography format: skin=filename.png (file in BepInEx/plugins/Skins/)
        /// </summary>
        [HarmonyPatch("SetCoat")]
        [HarmonyPostfix]
        public static void SetCoat_Postfix(WolfDefinition wolfDefinition, WolfCosmetics __instance)
        {
            if (__instance == null || wolfDefinition == null)
                return;

            if (string.IsNullOrEmpty(wolfDefinition.biography))
                return;

            try
            {
                var data = Plugin.ParseBiography(wolfDefinition.biography);

                if (!data.HasCustomSkin)
                    return;

                // Ensure .png extension
                string filename = data.SkinFile;
                if (!filename.EndsWith(".png"))
                    filename += ".png";

                // Get private fields via Traverse
                var lodRenderers = Traverse.Create(__instance)
                    .Field("lodRenderers").GetValue<List<SkinnedMeshRenderer>>();
                var lod0Renderer = Traverse.Create(__instance)
                    .Field("lod0Renderer").GetValue<SkinnedMeshRenderer>();
                int coatIndex = Traverse.Create(__instance)
                    .Field("coatIndexInWolfMeshRenderer").GetValue<int>();
                string texKeyword = Traverse.Create(__instance)
                    .Field("noFurShaderTextureKeyword").GetValue<string>();

                // Build the path to the skin file
                string path = Path.Combine(BepInEx.Paths.PluginPath, Plugin.AssetRoot, "Skins", filename);

                if (!File.Exists(path))
                {
                    Globals.Log($"Custom skin not found: {path}");
                    return;
                }

                // Load texture from file
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);

                // Apply to all LOD renderers
                if (lodRenderers != null)
                {
                    foreach (var r in lodRenderers)
                    {
                        ApplyTexture(r, coatIndex, texKeyword, tex);
                    }
                }

                // Apply to LOD0 renderer
                if (lod0Renderer != null)
                {
                    ApplyTexture(lod0Renderer, coatIndex, texKeyword, tex);
                }

                // Apply to fur system (XFur)
                var xFurSystem = Traverse.Create(__instance)
                    .Field("xFurSystem").GetValue<XFur_System>();
                if (xFurSystem != null && xFurSystem.FurProfiles.Length > coatIndex)
                {
                    xFurSystem.FurProfiles[coatIndex].furmatFurColorMap = tex;
                    xFurSystem.ApplyFurProperties(coatIndex, true);
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Error applying custom skin: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix patch for UpdateBodyDynamics - Applies custom size from biography.
        /// Biography format: size=1.5
        /// </summary>
        [HarmonyPatch("UpdateBodyDynamics")]
        [HarmonyPostfix]
        public static void UpdateBodyDynamics_Postfix(WolfDefinition wolfDefinition, WolfCosmetics __instance)
        {
            if (__instance == null || wolfDefinition == null)
                return;

            if (string.IsNullOrEmpty(wolfDefinition.biography))
                return;

            // Access private self field
            var self = Traverse.Create(__instance).Field("self").GetValue<Animal>();
            if (self == null) return;

            try
            {
                var data = Plugin.ParseBiography(wolfDefinition.biography);

                if (data.HasSize && data.Size.HasValue)
                {
                    self.Physical.SizeScale = data.Size.Value;
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Error applying custom size: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix patch for ApplyWolfCustomizations - Applies custom permanent injury from biography.
        /// Biography format: injury=RearRight
        /// </summary>
        [HarmonyPatch("ApplyWolfCustomizations")]
        [HarmonyPostfix]
        public static void ApplyWolfCustomizations_Postfix(WolfDefinition wolfDefinition, WolfCosmetics __instance)
        {
            if (__instance == null || wolfDefinition == null)
                return;

            if (string.IsNullOrEmpty(wolfDefinition.biography))
                return;

            // Access private self field
            var self = Traverse.Create(__instance).Field("self").GetValue<Animal>();
            if (self == null || self.WolfState == null) return;

            try
            {
                var data = Plugin.ParseBiography(wolfDefinition.biography);

                if (data.HasInjury)
                {
                    if (Enum.TryParse(data.Injury, true, out AttackSegment segment))
                    {
                        var injuryCalc = UnityEngine.Object.FindObjectOfType<InjuryCalculator>();
                        if (injuryCalc != null)
                        {
                            // Try to find a major injury for this segment
                            // We use Bite as a generic damage type that usually causes injuries
                            int injuryIndex = injuryCalc.PickInjury(InjurySeverity.Major, segment, DamageType.Bite);

                            if (injuryIndex != -1)
                            {
                                self.WolfState.MajorInjuryIndex = injuryIndex;
                                self.WolfState.MajorInjuryRecoveryLeft = float.PositiveInfinity;
                                Globals.Log(
                                    $"Applied permanent major injury to {segment} (Index: {injuryIndex})");
                            }
                            else
                            {
                                Globals.Log($"No major injury found for segment: {segment}");
                            }
                        }
                        else
                        {
                            Globals.Log("InjuryCalculator not found!");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Error applying custom injury: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper to apply texture to a skinned mesh renderer.
        /// </summary>
        private static void ApplyTexture(SkinnedMeshRenderer renderer, int matIndex, string texKeyword, Texture2D tex)
        {
            if (renderer == null || renderer.materials.Length <= matIndex)
                return;

            renderer.materials[matIndex].SetTexture(texKeyword, tex);
            renderer.materials[matIndex].SetColor("_Color", Color.white);
        }
    }
}
