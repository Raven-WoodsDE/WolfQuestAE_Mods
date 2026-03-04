using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using SharedCommons;

namespace LocationHelper
{
    [BepInPlugin("com.rw.locationhelper", "Location Helper", "1.4.0")]
    public class Plugin : BaseUnityPlugin
    {
        private string labelText;
        private Rect rect;
        private bool renderLabel;

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));

            rect = new Rect(Screen.width * .035f, Screen.height * .2f, 300f, 120f);

            Globals.Log("[LocationHelper] Initialised.");
        }

        private void Update()
        {
            if (Globals.MenuIsOpen)
            {
                renderLabel = false;
                return;
            }

            if (Globals.animalManager == null) return;

            renderLabel = true;

            labelText =
                "Position:" +
                $"{(int)Globals.animalManager.LocalPlayer.Position.x}, " +
                $"{(int)Globals.animalManager.LocalPlayer.Position.y}, " +
                $"{(int)Globals.animalManager.LocalPlayer.Position.z} \n" +
                $"Scene:{SceneManager.GetActiveScene().name}";
        }

        void OnGUI()
        {
            if (!renderLabel) return;

            rect.x = Screen.width * .035f;
            rect.y = Screen.height * .2f;
            GUI.color = Color.black;
            GUI.Label(rect, labelText);

            rect.x -= 2;
            GUI.color = Color.white;
            GUI.Label(rect, labelText);
        }
    }
}
