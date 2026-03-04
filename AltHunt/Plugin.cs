using BepInEx;
using HarmonyLib;
using System.IO;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace AltHunt
{
    [BepInPlugin("com.rw.althunt", "AltHunt", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static AssetBundle assetBundle;
        public static GameObject prefabHuntNormal, prefabHuntHuman;
        public static AudioClip sndBite, sndStart, sndFail;
        public static AudioClip sndDeer, sndElk, sndMoose, sndBison;

        private const float sqrAttackRange = 16f;

        private Vector3 pos1, pos2;
        private GameObject activeHunt;

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));

            LoadAssets();

            Globals.Log("[AltHunt] Initialised.");
        }

        private void LoadAssets()
        {
            try
            {
                string path = Path.Combine(Paths.PluginPath, "althunt_assets");

                assetBundle = AssetBundle.LoadFromFile(path);
                prefabHuntNormal = assetBundle.LoadAsset<GameObject>("Alt-Hunt");
                prefabHuntHuman = assetBundle.LoadAsset<GameObject>("Alt-HuntHuman");
                sndBite = assetBundle.LoadAsset<AudioClip>("bite");
                sndStart = assetBundle.LoadAsset<AudioClip>("start");
                sndFail = assetBundle.LoadAsset<AudioClip>("failed");
                sndDeer = assetBundle.LoadAsset<AudioClip>("deer");
                sndElk = assetBundle.LoadAsset<AudioClip>("elk");
                sndMoose = assetBundle.LoadAsset<AudioClip>("moose");
                sndBison = assetBundle.LoadAsset<AudioClip>("bison");
            }
            catch (System.Exception error)
            {
                Globals.Log(error.Message);
            }
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;

            if (Globals.inputControls.AttackHeld || Globals.inputControls.AttackPressed)
            {
                if (activeHunt != null) return;

                pos2 = Globals.animalManager.LocalPlayer.Position;

                foreach (Animal animal in Globals.animalManager.LivingAnimals)
                {
                    if (animal == null) continue;
                    if (animal.IsPlayer) continue;
                    if (animal.IsWolf && !IsStrayPup(animal)) continue;

                    pos1 = animal.Position;
                    if ((pos1 - pos2).sqrMagnitude <= sqrAttackRange)
                    {
                        if (!ValidSpecies(animal.Species.name))
                            continue;

                        Vector3 directionToAnimal = (pos1 - pos2).normalized;
                        Vector3 playerForward = Globals.animalManager.LocalPlayer.Physical.MovingPartTransform.forward;
                        float dot = Vector3.Dot(playerForward, directionToAnimal);

                        if (dot > 0.5f)
                        {
                            activeHunt = Instantiate(prefabHuntNormal);
                            activeHunt.AddComponent<HuntManager>().Init(Globals.animalManager.LocalPlayer, animal);
                            break;
                        }
                    }
                }
            }
        }

        public static bool IsStrayPup(Animal animal)
        {
            if (animal == null) return false;
            if (animal.WolfDef == null) return false;
            return animal.WolfDef.originPackName == "Stray";
        }

        public static bool ValidSpecies(string species)
        {
            string n = species.ToLower();

            switch (n)
            {
                case "bear": return false;
                case "grizzly": return false;
                case "hare": return false;
                case "cougar": return false;
                case "lynx": return false;
                case "feral dogs": return false;
                case "wolverine": return false;
            }

            return true;
        }
    }
}
