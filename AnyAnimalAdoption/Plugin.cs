using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace AnyAnimalAdoption
{
    [BepInPlugin("com.rw.anyanimaladoption", "AnyAnimalAdoption", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<KeyCode> cfgAdoptHotkey;
        public static ConfigEntry<float> cfgMaxAdoptDistance;
        public static ConfigEntry<float> cfgFollowDistance;
        public static ConfigEntry<float> cfgFollowSpeed;
        public static ConfigEntry<int> cfgGroundLayer;

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));

            cfgAdoptHotkey = Config.Bind("General", "Adopt Hotkey", KeyCode.O, "Hotkey to adopt the animal you are looking at.");
            cfgMaxAdoptDistance = Config.Bind("General", "Max Adopt Distance", 15f, "Maximum distance to adopt an animal.");
            cfgFollowDistance = Config.Bind("General", "Follow Distance", 6f, "Distance the adopted animal will try to maintain from the player.");
            cfgFollowSpeed = Config.Bind("General", "Follow Speed", 8f, "Speed at which the adopted animal will move to catch up.");
            cfgGroundLayer = Config.Bind("General", "GroundLayer", 12, "Layer index to cast rays against for grounding.");
            
            Globals.Log("[AnyAnimalAdoption] Initialised.");
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;

            if (Input.GetKeyDown(cfgAdoptHotkey.Value))
            {
                TryAdoptAnimal();
            }
        }

        private void TryAdoptAnimal()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;
            
            Animal playerAnimal = Globals.animalManager.LocalPlayer;
            if (playerAnimal == null) return;

            Animal bestTarget = null;
            float bestDist = cfgMaxAdoptDistance.Value;
            Vector3 pos2 = playerAnimal.Position;

            foreach (Animal animal in Globals.animalManager.LivingAnimals)
            {
                if (animal == null || animal == playerAnimal || animal.IsPlayer || animal.State.DeadOrDying) continue;
                
                Vector3 pos1 = animal.Position;
                float dist = Vector3.Distance(pos1, pos2);
                if (dist < bestDist)
                {
                    Vector3 directionToAnimal = (pos1 - pos2).normalized;
                    Vector3 playerForward = playerAnimal.Forward;
                    float dot = Vector3.Dot(playerForward, directionToAnimal);

                    if (dot > 0.5f) // Roughly in front
                    {
                        bestDist = dist;
                        bestTarget = animal;
                    }
                }
            }

            if (bestTarget != null)
            {
                Adopt(bestTarget, playerAnimal);
            }
            else
            {
                Globals.ShowMessage("Adoption", "No animal in sight to adopt.");
            }
        }

        private void Adopt(Animal animal, Animal playerAnimal)
        {
            SceneAssetContainer sac = UnityEngine.Object.FindObjectOfType<SceneAssetContainer>();
            if (sac != null)
            {
                Animal prefab = sac.GetPrefab<Animal>(animal.PrefabId);
                if (prefab != null)
                {
                    // Create unmoving dummy copy
                    Animal newAnimal = UnityEngine.Object.Instantiate(prefab, animal.Position, animal.Rotation);

                    // Add CustomFollowAI
                    CustomFollowAI customAI = newAnimal.gameObject.AddComponent<CustomFollowAI>();
                    customAI.target = playerAnimal;

                    // Teleport and kill the original animal 
                    animal.transform.position += Vector3.down * 500f;
                    animal.State.Health = 0f;
                    animal.gameObject.SetActive(false);

                    Globals.ShowMessage("Adoption", $"Successfully adopted 1 {animal.Species.name}!");
                    Globals.Log($"[AnyAnimalAdoption] Spawned unmoving prefab of {animal.name} and attached CustomFollowAI.");
                    return;
                }
            }

            Globals.ShowMessage("Adoption", "Failed to access SceneAssetContainer or Prefab.");
        }
    }
}
