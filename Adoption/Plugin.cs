using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace Adoption
{
    [BepInPlugin("com.rw.adoption", "Adoption", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private
            List<Animal> strayPups = new List<Animal>(20); // Initialized list

        private
            Texture2D pupIcon; // Icon for stray pups

        private const float
            MinSpawnInterval = 60f, // minimum seconds delay between spawn checks
            MaxSpawnInterval = 600f, // maximum seconds delay between spawn checks
            MinDistanceFromDen = 300f,
            MaxDistanceFromDen = 500f,
            MaxRenderDistanceIcon = 300f,
            MaxAdoptDistanceFromDen = 10f,
            iconSize = 24f;

        private float
            alpha,
            updateTimer,
            currentSpawnInterval,
            straySpawnTimer;

        private Rect
            rect;

        private Vector3
            worldPos,
            screenPos;

        public static ConfigEntry<bool> cfgShowIcons;
        public static ConfigEntry<bool> cfgAdoptPupsOnPickup;

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(HealthUpdater_TakeDamage_Patch));

            alpha = 0;
            rect = new Rect(0, 0, iconSize, iconSize);
            worldPos = Vector3.zero;
            screenPos = Vector3.zero;

            currentSpawnInterval = UnityEngine.Random.Range(MinSpawnInterval, MaxSpawnInterval);

            cfgShowIcons = Config.Bind<bool>("Adoption", "Show Icons", true, "");
            cfgAdoptPupsOnPickup = Config.Bind<bool>("Adoption", "Adopt Pups On Pickup", true, "");

            if (cfgShowIcons.Value) LoadAssets();

            Globals.Log("[Adoption] Initialised.");
        }

        private void LoadAssets()
        {
            string filePath = Path.Combine(Paths.PluginPath, "adoption_assets", "icon.png");

            if (File.Exists(filePath))
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                pupIcon = new Texture2D(2, 2);
                pupIcon.LoadImage(fileData);
            }
            else
            {
                Globals.Log($"[Adoption] Icon not found at: {filePath}");
            }
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;

            try
            {
                updateTimer += Time.deltaTime;
                if (updateTimer >= 1f)
                {
                    updateTimer = 0f;

                    if (Globals.animalManager?.LocalPlayer != null)
                    {
                        Animal carriedAnimal = Globals.animalManager.LocalPlayer.State.AnimalInMouth;
                        if (carriedAnimal != null && carriedAnimal.IsWolf)
                        {
                            if (carriedAnimal.State.DeadOrDying == false)
                            {
                                if (cfgAdoptPupsOnPickup.Value)
                                {
                                    TryAdoptCarriedPup();
                                }
                                else
                                {
                                    Animal pPlayer = Globals.animalManager.LocalPlayer;
                                    if (pPlayer.Pack.homeSite != null)
                                    {
                                        Vector3 dist = pPlayer.Pack.homeSite.VisualPosition - pPlayer.Position;
                                        if (dist.sqrMagnitude < MaxAdoptDistanceFromDen * MaxAdoptDistanceFromDen)
                                        {
                                            TryAdoptCarriedPup();
                                            return;
                                        }
                                    }
                                }

                                // Damage pup in water (5% Health/sec)
                                if (carriedAnimal.Physical != null)
                                {
                                    bool playerSwimming = Globals.animalManager.LocalPlayer.Physical.Swimming;
                                    bool playerInWater = Globals.animalManager.LocalPlayer.Physical.ForePawsInDeepWater;
                                    bool pupSwimming = carriedAnimal.Physical.Swimming;
                                    bool pupInWater = carriedAnimal.Physical.ForePawsInDeepWater;
                                    if (playerSwimming || pupSwimming || playerInWater || pupInWater)
                                    {
                                        carriedAnimal.TakeDamage(
                                            35,
                                            false, false, false,
                                            DamageType.Drown,
                                            AttackSegment.HeadLeft, null);

                                        // Show notification with 30% chance to avoid spam
                                        if (UnityEngine.Random.value < 0.5f)
                                        {
                                            Globals.ShowMessage("Pack Info", "The pup you carry is drowing!", 1f);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                straySpawnTimer += Time.deltaTime;
                if (straySpawnTimer >= currentSpawnInterval)
                {
                    straySpawnTimer = 0f;
                    if (UnityEngine.Random.value > .75f)
                    {
                        TrySpawnStrayPup();
                    }
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"[Adoption] Update error: {ex.Message}");
            }
        }

        void OnGUI()
        {
            if (pupIcon == null) return;
            if (Globals.MenuIsOpen) return;

            foreach (Animal animal in Globals.animalManager.LivingAnimals)
            {
                if (animal == null) continue;
                if (animal.IsWolf == false) continue;
                if (animal.State == null) continue;
                if (animal.State.Health <= 0) continue;
                if (animal.WolfDef == null) continue;
                if (animal.WolfDef.originPackName != "Stray") continue;

                worldPos = animal.Position + Vector3.up * .25f;
                screenPos = Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0f && screenPos.z <= MaxRenderDistanceIcon)
                {
                    // Fade out with distance
                    alpha = 1.0f - (screenPos.z / MaxRenderDistanceIcon);

                    // Draw black outline
                    GUI.color = Color.black * alpha;
                    rect.x = screenPos.x - ((4 + iconSize) * .5f);
                    rect.y = Screen.height - screenPos.y - ((4 + iconSize) * .5f);
                    rect.width = rect.height = iconSize + 4;
                    GUI.DrawTexture(rect, pupIcon);

                    // Draw actual color
                    GUI.color = Color.white * alpha;
                    rect.x = screenPos.x - (iconSize * .5f);
                    rect.y = Screen.height - screenPos.y - (iconSize * .5f);
                    rect.width = rect.height = iconSize;
                    GUI.DrawTexture(rect, pupIcon);
                }
            }
        }

        public static bool IsStrayPup(Animal animal)
        {
            if (animal == null) return false;
            if (animal.IsWolf == false) return false;
            if (animal.State == null) return false;
            if (animal.State.Health <= 0) return false;
            if (animal.WolfDef == null) return false;
            if (animal.WolfDef.originPackName != "Stray") return false;
            return true;
        }

        private void TryAdoptCarriedPup()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            Animal carriedPup = Globals.animalManager.LocalPlayer.State.AnimalInMouth;
            if (carriedPup == null) return;

            if (IsStrayPup(carriedPup))
            {
                carriedPup.WolfDef.raisedByPlayer = true;
                carriedPup.WolfDef.isPlayerDescendant = false;
                carriedPup.WolfDef.originPackName = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.ActualName;

                try
                {
                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AddMember(carriedPup, true);
                }
                catch
                {
                }

                Globals.ShowMessage("Pack Info", "You adopted a stray pup!");
            }
        }

        private bool CanSpawn()
        {
            if (!Globals.persistentWolfCalculator)
            {
                Globals.Log("No persistentWolfCalculator");
                return false;
            }

            if (!Globals.animalManager?.LocalPlayer?.Pack?.PlayerPackData)
            {
                Globals.Log("No player pack data");
                return false;
            }

            PlayerPackHandler pph =
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData;

            if (pph.PupsLifeStage != WolfLifeStage.SpringPup)
            {
                Globals.Log("Not in spring pup stage");
                return false;
            }

            if (!pph.HomeSite || !pph.MainFlock)
            {
                Globals.Log("No home site or main flock");
                return false;
            }

            return true;
        }

        public void TrySpawnStrayPup()
        {
            if (!CanSpawn())
                return;

            try
            {
                Globals.Log("[Adoption] Preparing stray pup");
                PersistentWolf wolf =
                    Globals.persistentWolfCalculator.GetRandomWolf(WolfLifeStage.SpringPup, false, null);
                wolf.WolfDef.birthName = "Stray " + (wolf.WolfDef.sex == Sex.Male ? "(M)" : "(F)");
                wolf.WolfDef.nickName = wolf.WolfDef.birthName;
                wolf.WolfDef.originPackName = "Stray"; // Tag as stray
                wolf.WolfDef.isPlayerDescendant = false;
                wolf.WolfDef.raisedByPlayer = false;
                wolf.WolfDef.weightInPounds = UnityEngine.Random.Range(5f, 8f);

                Globals.Log("[Adoption] Finding spawn position");
                // Calculate spawn position based on PLAYER position
                Vector3 playerPos = Globals.animalManager.LocalPlayer.Position;
                Vector3 spawnPos = Vector3.zero;

                // Try to find a valid spawn position
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    float angle = UnityEngine.Random.Range(0f, 360f);
                    float distance = UnityEngine.Random.Range(MinDistanceFromDen, MaxDistanceFromDen);
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                    Vector3 testPos = playerPos + offset;

                    // Raycast from high above to find ground
                    RaycastHit hit;
                    if (Physics.Raycast(testPos + Vector3.up * 500f, Vector3.down, out hit, 1000f))
                    {
                        spawnPos = hit.point + Vector3.up * 0.5f;
                        break;
                    }
                }

                Globals.Log("[Adoption] Creating stray pup");
                // Ensure stray is not in player pack immediately after spawn
                Animal animal =
                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.MainFlock.ForceSpawnWolf(wolf, spawnPos);
                try
                {
                    if (animal.IsInPlayerPack)
                        Globals.animalManager.LocalPlayer.Pack.PlayerPackData.RemoveMember(animal, false);
                }
                catch
                {
                }

                Globals.Log("[Adoption] Setting stray pup stats");
                animal.State.Food = animal.State.MaxFoodWithoutRegurgitant * .25f;
                animal.State.Wakefulness = 0.1f;
                animal.State.SicknessState = SicknessState.RegularSickness;
                animal.State.Health = animal.State.MaxHealth * 0.3f;
                animal.State.SicknessHealthLossPerHour = 2f;

                Globals.Log("[Adoption] Teleporting stray pup");
                // Teleport to ensure proper grounding
                animal.Teleport(spawnPos, Quaternion.identity, GroundingMode.GroundUpwardOrDownwardFully, false);

                Globals.Log("[Adoption] Sending notification");
                string notificationMessage = "";
                switch (UnityEngine.Random.Range(1, 4))
                {
                    case 1: notificationMessage = "You feel like something changed in your territory.."; break;
                    case 2: notificationMessage = "A sickly odor wafts through the air.."; break;
                    case 3: notificationMessage = "A strange scent like old milk and despair is in the air."; break;
                    case 4: notificationMessage = "A strange scent wafts through the air.."; break;
                }

                Globals.Log("[Adoption] Showing notification");
                Globals.ShowMessage("Info", notificationMessage, 6f);

                Globals.Log("[Adoption] Adding stray pup to list");
                strayPups.Add(animal);

                currentSpawnInterval = UnityEngine.Random.Range(MinSpawnInterval, MaxSpawnInterval);
            }
            catch (Exception ex)
            {
                Globals.Log($"[Adoption] TrySpawnStrayPup error: {ex.Message}\n{ex.StackTrace}");
            }
        }


        [HarmonyPatch(typeof(WolfQuestEp3.DropYoungAction), "GetSpecialBlockReason")]
        [HarmonyPostfix]
        private static void GetSpecialBlockReason_Postfix(ref WolfQuestEp3.ActionBlockReason __result)
        {
            if (__result == WolfQuestEp3.ActionBlockReason.CantDropHere)
                __result = WolfQuestEp3.ActionBlockReason.None;
        }
    }

    [HarmonyPatch(typeof(HealthUpdater), "TakeDamage")]
    public static class HealthUpdater_TakeDamage_Patch
    {
        static bool Prefix(HealthUpdater __instance, Animal attacker)
        {
            // Randomly fail to trigger the patch (80% chance)
            if (UnityEngine.Random.value > 0.05f) 
            {
                Globals.Log("[Adoption] Bad Luck...");
                return true;
            }
            else
            {
                Globals.Log("[Adoption] Good Throw!");
            }

            // Something wrong with the attacker?
            if (attacker == null || (!attacker.IsPlayer && !attacker.IsInPlayerPack)) 
                return true;

            // Something wrong with the target?
            Animal target = Traverse.Create(__instance).Field("self").GetValue<Animal>();
            if (target == null) return true;
            if (target.IsPlayer) return true;
            if (!target.IsWolf) return true;
            if (target.WolfDef.lifeStage == WolfLifeStage.Adult) return true;
            if (target.Pack == attacker.Pack) return true;

            // Something wrong with the target's state?
            AnimalState state = target.State;
            if (state.DeadOrDying) return true;
            if (state.HealthRatio > 0.2f) return true;

            Globals.notificationControls.QueueNotification(
                NotificationType.YesAndNoButtons,
                "Surrender",
                $"{target.WolfDef.GUIFormattedName} is defeated and begs to join your pack.\nAccept them?",
                NotificationPriority.Important,
                60f,
                (response) =>
                {
                    if (response == NotificationResponse.Yes)
                    {
                        target.State.Health = target.State.MaxHealth * .3f;
                        AdoptWolf(target);
                    }
                    else
                    {
                        target.TakeDamage(9999, false, false, false, DamageType.Bite, AttackSegment.MidRight, attacker);
                    }
                }
            );

            return false;
        }

        private static void AdoptWolf(Animal wolf)
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            wolf.WolfDef.raisedByPlayer = true;
            wolf.WolfDef.originPackName = Globals.animalManager.LocalPlayer.Pack.PlayerPackData.ActualName;

            try
            {
                // Transfer from old flock to player's main flock
                Globals.animalManager.LocalPlayer.Pack.MainFlock.TransferWolfFrom(wolf.Flock, wolf);
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AddMember(wolf, true);
                
                Globals.ShowMessage("Pack Info", $"{wolf.WolfDef.GUIFormattedName} joined your pack!");
            }
            catch (Exception ex)
            {
                Globals.Log($"[Adoption] AdoptWolf error: {ex.Message}");
            }
        }
    }
}
