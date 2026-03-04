using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace YoungHuntersRemedy
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> EnableDamageXpBypass { get; private set; }
        public static ConfigEntry<bool> EnableDamageDifficultyOverride { get; private set; }
        public static ConfigEntry<bool> EnableAdultCriticalHits { get; private set; }

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            EnableDamageXpBypass = Config.Bind("General", "EnableDamageXpBypass", true, "Bypass the XP based damage weakness array for Young Hunters, so they take normal damage from dangerous prey.");
            EnableDamageDifficultyOverride = Config.Bind("General", "EnableDamageDifficultyOverride", true, "Override the increased damage multiplier that pups receive. Young Hunters will use the adult NPCToNPC multiplier.");
            EnableAdultCriticalHits = Config.Bind("General", "EnableAdultCriticalHits", true, "Allow Young Hunters to deal adult critical hits similarly to adults.");

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll(typeof(Patches));

            Logger.LogInfo("Patches for Young Hunters Remedy applied successfully.");
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.kouga.younghuntersremedy";
        public const string PLUGIN_NAME = "Young Hunters Remedy";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
