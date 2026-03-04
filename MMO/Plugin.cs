using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using WolfQuestEp3;
using Photon.Realtime;
using System;
using SharedCommons;

namespace MMO
{
    [BepInPlugin("com.rw.mmo", "MMO", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        // Config entries
        public static ConfigEntry<int> MaxPlayersConfig;
        public static ConfigEntry<bool> EnableCustomMaxPlayers;
        public static ConfigEntry<string> DefaultRoomName;
        public static ConfigEntry<bool> PublicRoom;

        private void Awake()
        {
            // Bind configuration options
            EnableCustomMaxPlayers = Config.Bind("Room Settings",
                "EnableCustomMaxPlayers",
                false,
                "Enable custom max player limit for multiplayer rooms");

            MaxPlayersConfig = Config.Bind("Room Settings",
                "MaxPlayers",
                16,
                new ConfigDescription(
                    "Maximum number of players allowed in your room (2-255)",
                    new AcceptableValueRange<int>(2, 255)));

            DefaultRoomName = Config.Bind("Room Settings",
                "DefaultRoomName",
                "",
                "Default room name to use (leave empty for random)");

            // Apply Harmony patches
            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        // Patch the NwRoomInfo.CreateRoomOptions method
        [HarmonyPatch(typeof(NwRoomInfo), "CreateRoomOptions")]
        [HarmonyPostfix]
        static void ModifyRoomOptions(ref RoomOptions __result)
        {
            if (EnableCustomMaxPlayers.Value)
            {
                __result.MaxPlayers = (byte)MaxPlayersConfig.Value;
            }
        }

        // Patch NwRoomInfo constructor to modify max players
        [HarmonyPatch(typeof(NwRoomInfo), MethodType.Constructor,
            new Type[] { typeof(string), typeof(SceneUtilities.SceneIdentity),
            typeof(GameConfiguration), typeof(int), typeof(ChatType),
            typeof(System.Collections.Generic.List<string>), typeof(int),
            typeof(int), typeof(int) })]
        [HarmonyPrefix]
        static void ModifyRoomInfoConstructor(ref int maxPlayers, ref string roomName)
        {
            if (EnableCustomMaxPlayers.Value)
            {
                maxPlayers = MaxPlayersConfig.Value;
            }

            if (!string.IsNullOrEmpty(DefaultRoomName.Value))
            {
                roomName = DefaultRoomName.Value;
            }
        }

        // Optional: Patch MultiplayerLoader to change default
        [HarmonyPatch(typeof(MultiplayerLoader), "OnAvailableRoomsChanged")]
        [HarmonyPrefix]
        static void ModifyLoaderMaxPlayers(MultiplayerLoader __instance)
        {
            if (EnableCustomMaxPlayers.Value)
            {
                __instance.maxPlayers = MaxPlayersConfig.Value;
            }
        }
    }
}
