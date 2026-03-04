using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using SharedCommons;

namespace DenExpansion
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        internal static ManualLogSource Log;
        private Harmony harmony;

        // ==================== CONFIG ====================
        public static ConfigEntry<KeyCode> CfgInteractKey;
        public static ConfigEntry<bool> CfgAutoNamePups;
        public static ConfigEntry<bool> CfgSkipNamingMission;
        public static ConfigEntry<bool> CfgEnableDenExpansion;

        // ==================== PUP AUTONAMER ====================
        public static List<string> FemaleNames = new List<string>
        {
            "Luna", "Stella", "Aurora", "Willow", "Shadow", "Dakota", "Sierra", "Athena",
            "Maya", "Nova", "Iris", "Sage", "Ember", "Ivy", "Hazel", "Sky", "Storm"
        };

        public static List<string> MaleNames = new List<string>
        {
            "Shadow", "Thunder", "Storm", "Wolf", "Hunter", "Blaze", "Ghost", "Fang",
            "Arrow", "Scout", "Ranger", "Dakota", "Bear", "Hawk", "River", "Stone"
        };

        // ==================== GUI STATE ====================
        private bool showDenMenu;
        private int activeTab; // 0 = Den, 1 = Pack

        // Den window
        private Rect windowRect = new Rect(
            (Screen.width * .5f) - 220,
            (Screen.height * .5f) - 200,
            500, 480);
        private bool windowPositionedForEditor;

        // Pack editor window (appears to the right when a member is selected)
        private Rect editorRect = new Rect(0, 0, 400, 580);
        private bool editorPositioned;

        // Selected pack member for the editor
        private Animal selectedMember;
        private string tempBio = "";
        private Vector2 bioScroll;

        // Scroll positions
        private Vector2 denScroll;
        private Vector2 packScroll;

        // ==================== DEN STORAGE ====================
        public static Dictionary<string, DenStorageData> DenStorage = new Dictionary<string, DenStorageData>();
        private SceneToSceneData sceneToSceneData;
        private string currentSaveFilePath;
        private bool denDataLoaded;

        // Current / nearest den
        private HomeSite currentDen;
        private HomeSite nearestDen;

        // ==================== GUI STYLES ====================
        private bool stylesInitialized;
        private GUIStyle headerStyle;
        private GUIStyle sectionStyle;
        private GUIStyle titleBarStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle kickButtonStyle;
        private GUIStyle tabStyle;
        private GUIStyle activeTabStyle;
        private GUIStyle promptStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle windowStyle;
        private Texture2D bgTex;

        // ================================================================
        //                           LIFECYCLE
        // ================================================================

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            CfgInteractKey = Config.Bind("Keybinds", "InteractKey", KeyCode.F, "Key to interact with dens");
            CfgAutoNamePups = Config.Bind("Pup Autonamer", "Auto-Name Pups", true, "Automatically name newborn pups");
            CfgSkipNamingMission = Config.Bind("Pup Autonamer", "Skip Naming Mission", true, "Skip the pup naming screen");
            CfgEnableDenExpansion = Config.Bind("General", "Enable Den Expansion", true, "Enable the Den Management and expansion features");

            EnsureFoldersExist();
            LoadNameLists();

            Globals.Log("[DenExpansion] Initialised.");
        }

        private void EnsureFoldersExist()
        {
            string assetRoot = Path.Combine(Paths.PluginPath, "denexpansion_assets");
            if (!Directory.Exists(assetRoot))
                Directory.CreateDirectory(assetRoot);
        }

        private void LoadNameLists()
        {
            string assetRoot = Path.Combine(Paths.PluginPath, "denexpansion_assets");
            
            string femPath = Path.Combine(assetRoot, "names_f.txt");
            if (File.Exists(femPath))
            {
                FemaleNames = File.ReadAllLines(femPath).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                Log.LogInfo($"Loaded {FemaleNames.Count} female names");
            }

            string malePath = Path.Combine(assetRoot, "names_m.txt");
            if (File.Exists(malePath))
            {
                MaleNames = File.ReadAllLines(malePath).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                Log.LogInfo($"Loaded {MaleNames.Count} male names");
            }
        }

        private void Update()
        {
            if (sceneToSceneData == null)
                sceneToSceneData = FindObjectOfType<SceneToSceneData>();

            if (Globals.MenuIsOpen)
            {
                showDenMenu = false;
                return;
            }

            // Load den data when save file is detected
            if (!denDataLoaded && sceneToSceneData != null && !string.IsNullOrEmpty(sceneToSceneData.saveName))
            {
                if (currentSaveFilePath != sceneToSceneData.saveName)
                {
                    currentSaveFilePath = sceneToSceneData.saveName;
                    LoadDenStorage();
                    denDataLoaded = true;
                }
            }

            if (!CfgEnableDenExpansion.Value)
            {
                showDenMenu = false;
                nearestDen = null;
                return;
            }

            CheckNearbyDen();

            if (Input.GetKeyDown(CfgInteractKey.Value))
            {
                if (!showDenMenu)
                {
                    if (nearestDen != null)
                    {
                        if (IsDenClaimed(nearestDen))
                        {
                            currentDen = nearestDen;
                            showDenMenu = true;
                            selectedMember = null;
                            editorPositioned = false;
                        }
                        else
                        {
                            ClaimDen(nearestDen);
                        }
                    }
                }
                else
                {
                    showDenMenu = false;
                }

                InputControls.ForceAllowCursor = showDenMenu;
                InputControls.DisableCameraInput = showDenMenu;
                InputControls.DisableInput = showDenMenu;
            }
        }

        // ================================================================
        //                           OnGUI
        // ================================================================

        private void OnGUI()
        {
            if (Globals.MenuIsOpen) return;
            if (!CfgEnableDenExpansion.Value) return;
            if (nearestDen == null) return;

            InitStyles();

            if (!showDenMenu)
            {
                DrawInteractionPrompt();
                return;
            }

            if (currentDen == null) return;

            bool showEditor = activeTab == 1 && selectedMember != null && selectedMember.WolfDef != null;

            // When the editor opens, shift the main window left so the pair is centered
            if (showEditor && !windowPositionedForEditor)
            {
                float totalWidth = windowRect.width + 10 + editorRect.width;
                windowRect.x = (Screen.width - totalWidth) * 0.5f;
                windowRect.y = Mathf.Max(40f, (Screen.height - windowRect.height) * 0.5f);
                windowPositionedForEditor = true;
            }
            else if (!showEditor && windowPositionedForEditor)
            {
                // Switching back to Den tab — re-center the main window
                windowRect.x = (Screen.width - windowRect.width) * 0.5f;
                windowRect.y = Mathf.Max(40f, (Screen.height - windowRect.height) * 0.5f);
                windowPositionedForEditor = false;
            }

            // Clamp window positions
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);

            // Main window
            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            windowRect = GUI.Window(54321, windowRect, DrawMainWindow, "", windowStyle);

            // Pack editor (only when a member is selected on the Pack tab)
            if (showEditor)
            {
                if (!editorPositioned)
                {
                    editorRect.x = windowRect.x + windowRect.width + 10;
                    editorRect.y = Mathf.Max(40f, (Screen.height - editorRect.height) * 0.5f);
                    editorPositioned = true;
                }

                editorRect.x = Mathf.Clamp(editorRect.x, 0, Screen.width - editorRect.width);
                editorRect.y = Mathf.Clamp(editorRect.y, 0, Screen.height - editorRect.height);

                editorRect = GUI.Window(54322, editorRect, DrawEditorWindow, "", windowStyle);
            }
        }

        // ================================================================
        //                       MAIN TABBED WINDOW
        // ================================================================

        private void DrawMainWindow(int id)
        {
            DrawTitleBar("Den Management");
            GUILayout.Space(5f);

            // --- Tab bar ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Den", activeTab == 0 ? activeTabStyle : tabStyle, GUILayout.Height(28)))
            {
                activeTab = 0;
                selectedMember = null;
            }
            if (GUILayout.Button("Pack", activeTab == 1 ? activeTabStyle : tabStyle, GUILayout.Height(28)))
            {
                if (activeTab != 1)
                {
                    activeTab = 1;
                    // Auto-open the first player in the editor
                    var packData = Globals.animalManager.LocalPlayer?.Pack?.PlayerPackData;
                    if (packData?.PlayerWolves != null && packData.PlayerWolves.Count > 0)
                    {
                        selectedMember = packData.PlayerWolves[0];
                        tempBio = selectedMember.WolfDef?.biography ?? "";
                        editorPositioned = false;
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            if (activeTab == 0)
            {
                denScroll = GUILayout.BeginScrollView(denScroll);
                DrawDenTab();
                GUILayout.EndScrollView();
            }
            else
            {
                packScroll = GUILayout.BeginScrollView(packScroll);
                DrawPackTab();
                GUILayout.EndScrollView();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        // ================================================================
        //                          DEN TAB
        // ================================================================

        private void DrawDenTab()
        {
            var denStorage = GetOrCreateDenStorage(currentDen);

            // --- Stored Food ---
            GUILayout.Label("=== Stored Food ===", sectionStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Amount: {Mathf.Round(denStorage.StoredMeatFoodValue * 10f) / 10f} / 3000", labelStyle);
            GUILayout.FlexibleSpace();

            if (Globals.animalManager.LocalPlayer.State.ObjectInMouth != null)
            {
                if (GUILayout.Button("Add Meat", buttonStyle, GUILayout.Height(20), GUILayout.Width(100)))
                    AddItemToDen();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            // --- Misc Actions ---
            GUILayout.Label("=== Actions ===", sectionStyle);

            if (GUILayout.Button("Fill from nearby Carcasses", buttonStyle, GUILayout.Height(22)))
                ExtractFromCarcasses();

            GUILayout.Space(10);
            if (GUILayout.Button("Fetch Carcasses", buttonStyle, GUILayout.Height(22)))
                FetchCarcasses();

            GUILayout.Space(10);
            if (GUILayout.Button("Get Random Toy", buttonStyle, GUILayout.Height(22)))
                GetRandomToy();

            GUILayout.Space(10);
            if (GUILayout.Button("Send Pups to Den", buttonStyle, GUILayout.Height(22)))
                FetchAllPups();

            GUILayout.Space(10);
            if (GUILayout.Button("Retrieve stray packmembers", buttonStyle, GUILayout.Height(22)))
                FetchAllPackMembers();

            GUILayout.Space(60);

            if (GUILayout.Button("Abandon Den", buttonStyle, GUILayout.Height(44)))
            {
                Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AssignHomeSite(null);
                InputControls.ForceAllowCursor = false;
                InputControls.DisableCameraInput = false;
                InputControls.DisableInput = false;
                currentDen = null;
                nearestDen = null;
                showDenMenu = false;
            }
        }

        // ================================================================
        //                          PACK TAB
        // ================================================================

        private void DrawPackTab()
        {
            if (Globals.animalManager.LocalPlayer.Pack == null)
            {
                GUILayout.Label("No pack.", labelStyle);
                return;
            }

            var packData = Globals.animalManager.LocalPlayer.Pack.PlayerPackData;

            GUILayout.Label($"Pack: {Globals.animalManager.LocalPlayer.Pack.NameForDisplay}, {packData.Members.Count()} Members", sectionStyle);
            GUILayout.Space(5);

            // --- Players ---
            if (packData.PlayerWolves != null && packData.PlayerWolves.Count > 0)
            {
                GUILayout.Label("- Players -", sectionStyle);
                foreach (var member in packData.PlayerWolves)
                    DrawMemberRow(member, true);
            }

            // --- Mate ---
            if (packData.Mate != null)
            {
                GUILayout.Space(10);
                GUILayout.Label("- Mate -", sectionStyle);
                DrawMemberRow(packData.Mate, false);
            }
            else
            {
                GUILayout.Label("None", sectionStyle);
            }

            // --- Adult Offspring ---
            if (packData.AdultOffsprings != null && packData.AdultOffsprings.Count > 0)
            {
                GUILayout.Space(10);
                bool hasDrawnAdults = false;
                GUILayout.Label("- Adult Members -", sectionStyle);
                foreach (var member in packData.AdultOffsprings)
                {
                    if (member.WolfDef.lifeStage >= WolfLifeStage.Adult)
                    {
                        DrawMemberRow(member, false);
                        hasDrawnAdults = true;
                    }
                }
                if(!hasDrawnAdults) GUILayout.Label("None", sectionStyle);

                GUILayout.Space(10);
                bool hasDrawnYearlings = false;
                GUILayout.Label("- Yearlings -", sectionStyle);
                foreach (var member in packData.AdultOffsprings)
                {
                    if (member.WolfDef.lifeStage == WolfLifeStage.Yearling)
                    {
                        DrawMemberRow(member, false);
                        hasDrawnYearlings = true;
                    }
                }
                if (!hasDrawnYearlings) GUILayout.Label("None", sectionStyle);
            }

            // --- Pups ---
            if (packData.Pups != null && packData.Pups.Count > 0)
            {
                GUILayout.Space(10);
                bool hasDrawnYoungHunters = false;
                GUILayout.Label("- Young Hunters -", sectionStyle);
                foreach (var pup in packData.Pups)
                {
                    if (pup.WolfDef.lifeStage == WolfLifeStage.YoungHunter)
                    {
                        DrawMemberRow(pup, false);
                        hasDrawnYoungHunters = true;
                    }
                }
                if (!hasDrawnYoungHunters) GUILayout.Label("None", sectionStyle);

                GUILayout.Space(10);
                GUILayout.Label("- Pups -", sectionStyle);
                bool hasDrawnPups = false;
                foreach (var pup in packData.Pups)
                {
                    if (pup.WolfDef.lifeStage < WolfLifeStage.YoungHunter)
                    {
                        DrawMemberRow(pup, false);
                        hasDrawnPups = true;
                    }
                }
                if (!hasDrawnPups)
                {
                    GUILayout.Label("None", sectionStyle);
                }
            }
            else
            {
                GUILayout.Label("None", sectionStyle);
            }
        }

        private void DrawMemberRow(Animal member, bool isPlayer)
        {
            if (member == null || member.WolfDef == null) return;

            // --- Stats ---
            string sex = member.WolfDef.sex == Sex.Male ? "M" : "F";
            string name = member.WolfDef.NickNameOrBirthName;
            int hpPct = (member.State != null) ? Mathf.RoundToInt((member.State.Health / member.State.MaxHealth) * 100f) : 0;
            int foodPct = (member.State != null) ? Mathf.RoundToInt((member.State.Food / member.State.MaxFoodWithRegurgitant) * 100f) : 0;

            GUILayout.BeginHorizontal();

            // Fixed width for the clickable area (300).
            float buttonWidth = 300;

            Rect rowRect = GUILayoutUtility.GetRect(buttonWidth, 24);
            GUIStyle style = (selectedMember == member) ? selectedButtonStyle : buttonStyle;
            
            if (GUI.Button(rowRect, "", style))
            {
                selectedMember = member;
                tempBio = member.WolfDef.biography ?? "";
                editorPositioned = false;
            }

            // Overlay aligned text
            float x = rowRect.x + 5;
            GUI.Label(new Rect(x, rowRect.y, 110, 24), $"{name} ({sex})", labelStyle);
            x += 115;
            GUI.Label(new Rect(x, rowRect.y, 100, 24), $"Health: {hpPct}%", labelStyle);
            x += 105;
            GUI.Label(new Rect(x, rowRect.y, 80, 24), $"Food: {foodPct}%", labelStyle);

            GUILayout.Space(5);

            // Feed button (always width 50)
            if (GUILayout.Button("Feed", buttonStyle, GUILayout.Width(50), GUILayout.Height(24)))
                FeedSingleMember(member);

            GUILayout.Space(5);

            // Kick button spot (fixed width 50)
            bool canKick = !isPlayer && member.Pack.PlayerPackData.Mate != member && member.WolfDef.lifeStage >= WolfLifeStage.Yearling;
            if (canKick)
            {
                if (GUILayout.Button("Kick", buttonStyle, GUILayout.Width(50), GUILayout.Height(24)))
                    KickMember(member);
            }
            else
            {
                // Preserve the space so the row doesn't collapse or the name button doesn't stretch
                GUILayout.Space(50);
            }

            GUILayout.EndHorizontal();
        }

        // ================================================================
        //                    PACK EDITOR WINDOW
        // ================================================================

        private void DrawEditorWindow(int id)
        {
            DrawTitleBar("Wolf Editor");
            DrawSelectedMemberInfo();
            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        private void DrawSelectedMemberInfo()
        {
            if (selectedMember == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(5);
            GUILayout.Label("<b>" + selectedMember.WolfDef.GUIFormattedName + "</b>", headerStyle);

            WolfDefinition def = selectedMember.WolfDef;

            // --- Stats (Innate) ---
            GUILayout.Label("- Stats (Innate) -", sectionStyle);

            DrawStatSlider("Strength", ref def.innateAbilities.strength, -2f, 999f, () => selectedMember.UpdateBodyDynamics());
            DrawStatSlider("Health", ref def.innateAbilities.health, -2f, 999f, null);
            DrawStatSlider("Agility", ref def.innateAbilities.agility, -2f, 999f, null);
            DrawStatSlider("Stamina", ref def.innateAbilities.stamina, -2f, 999f, null);

            GUILayout.Space(5f);

            // --- Personality ---
            GUILayout.Label("- Personality -", sectionStyle);

            DrawFloatSlider("Boldness", ref def.cautiousToBold, 0f, 1f);
            DrawFloatSlider("Social", ref def.lonerToSocial, 0f, 1f);
            DrawFloatSlider("Energetic", ref def.lazyToEnergetic, 0f, 1f);

            GUILayout.Space(5f);

            // --- Pelt ---
            GUILayout.Label("- Pelt -", sectionStyle);

            int maxPackages = 20;
            int maxCoats = 30;

            WolfCustomizationSettings settings = Traverse.Create(selectedMember.WolfCosmetics)
                .Field("wolfCustomizationSettings").GetValue<WolfCustomizationSettings>();
            if (settings != null && settings.coatPackages != null)
            {
                maxPackages = settings.coatPackages.Length - 1;
                if (def.coatPackageIndex >= 0 && def.coatPackageIndex < settings.coatPackages.Length)
                {
                    if (settings.coatPackages[def.coatPackageIndex].coats != null)
                        maxCoats = settings.coatPackages[def.coatPackageIndex].coats.Length - 1;
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Package: {def.coatPackageIndex} (Max: {maxPackages})", labelStyle, GUILayout.Width(140));
            int newPkg = Mathf.RoundToInt(GUILayout.HorizontalSlider(def.coatPackageIndex, 0f, maxPackages));
            if (newPkg != def.coatPackageIndex)
            {
                def.coatPackageIndex = newPkg;
                def.coatIndex = 0;
                selectedMember.WolfCosmetics.ApplyCoatCustomization(def);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Coat: {def.coatIndex} (Max: {maxCoats})", labelStyle, GUILayout.Width(140));
            int newCoat = Mathf.RoundToInt(GUILayout.HorizontalSlider(def.coatIndex, 0f, maxCoats));
            if (newCoat != def.coatIndex)
            {
                def.coatIndex = newCoat;
                selectedMember.WolfCosmetics.ApplyCoatCustomization(def);
            }
            GUILayout.EndHorizontal();

            // Tints
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Gray Tint: {def.grayTint:F2}", labelStyle, GUILayout.Width(140));
            float newGray = GUILayout.HorizontalSlider(def.grayTint, 0f, 1f);
            if (Mathf.Abs(newGray - def.grayTint) > 0.001f)
            {
                def.grayTint = newGray;
                selectedMember.WolfCosmetics.ApplyCoatTintCustomization(def);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Brown Tint: {def.brownTint:F2}", labelStyle, GUILayout.Width(140));
            float newBrown = GUILayout.HorizontalSlider(def.brownTint, 0f, 1f);
            if (Mathf.Abs(newBrown - def.brownTint) > 0.001f)
            {
                def.brownTint = newBrown;
                selectedMember.WolfCosmetics.ApplyCoatTintCustomization(def);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);

            // --- Biography ---
            GUILayout.Label("- Biography / Extra Data -", sectionStyle);
            bioScroll = GUILayout.BeginScrollView(bioScroll, GUILayout.Height(80));
            string newBio = GUILayout.TextArea(tempBio, textAreaStyle, GUILayout.ExpandHeight(true));
            if (newBio != tempBio)
            {
                tempBio = newBio;
                def.biography = tempBio;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(10f);

            GUILayout.EndVertical();
        }

        // ================================================================
        //                       HELPER GUI DRAWERS
        // ================================================================

        private void DrawTitleBar(string title)
        {
            GUILayout.BeginHorizontal(titleBarStyle, GUILayout.Height(25));
            GUILayout.Label(title, titleBarStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawStatSlider(string name, ref int value, float min, float max, Action onChanged)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {value}", labelStyle, GUILayout.Width(100));

            if (GUILayout.Button("-", buttonStyle, GUILayout.Width(20)))
            {
                value = Mathf.RoundToInt(Mathf.Max(min, value - 1));
                onChanged?.Invoke();
            }

            int newVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(80)));

            if (GUILayout.Button("+", buttonStyle, GUILayout.Width(20)))
            {
                newVal = Mathf.RoundToInt(Mathf.Min(max, value + 1));
            }

            if (newVal != value)
            {
                value = newVal;
                onChanged?.Invoke();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawFloatSlider(string name, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {value:F2}", labelStyle, GUILayout.Width(100));
            float newVal = GUILayout.HorizontalSlider(value, min, max);
            if (Mathf.Abs(newVal - value) > 0.001f)
                value = newVal;
            GUILayout.EndHorizontal();
        }

        private void DrawInteractionPrompt()
        {
            if (Globals.MenuIsOpen) return;

            string prompt = IsDenClaimed(nearestDen)
                ? $"[{CfgInteractKey.Value}] Open Den"
                : $"[{CfgInteractKey.Value}] Claim Den";

            float w = 300, h = 60;
            Rect r = new Rect(Screen.width * .5f - w * .5f, Screen.height - 150, w, h);

            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, w, h), prompt, promptStyle);
            GUI.color = Color.white;
            GUI.Label(r, prompt, promptStyle);
        }

        // ================================================================
        //                       DEN LOGIC
        // ================================================================

        private void CheckNearbyDen()
        {
            nearestDen = null;
            if (Globals.animalManager.LocalPlayer == null || Globals.homeSiteControls == null) return;

            if ((Globals.animalManager.LocalPlayer.Position - Globals.homeSiteControls.NearestDen.VisualPosition)
                .sqrMagnitude < 6f * 6f)
            {
                nearestDen = Globals.homeSiteControls.NearestDen;
            }
        }

        private bool IsDenClaimed(HomeSite den)
        {
            return Globals.animalManager.LocalPlayer.Pack != null &&
                   Globals.animalManager.LocalPlayer.Pack.HomeSite == den;
        }

        private string GetDenId(HomeSite den)
        {
            return den?.idForSave ?? den?.GetInstanceID().ToString() ?? "unknown";
        }

        private DenStorageData GetOrCreateDenStorage(HomeSite den)
        {
            string denId = GetDenId(den);
            if (!DenStorage.ContainsKey(denId))
                DenStorage[denId] = new DenStorageData();
            return DenStorage[denId];
        }

        private void ClaimDen(HomeSite den)
        {
            if (Globals.animalManager.LocalPlayer.Pack == null || den == null) return;

            if (Globals.animalManager.LocalPlayer.Pack.HomeSite != null)
                Log.LogInfo($"Abandoning current den: {GetDenId(Globals.animalManager.LocalPlayer.Pack.HomeSite)}");

            Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AssignHomeSite(den);
            currentDen = den;
        }

        private void AddItemToDen()
        {
            if (Globals.animalManager.LocalPlayer == null || currentDen == null) return;

            var denStorage = GetOrCreateDenStorage(currentDen);
            var objectInMouth = Globals.animalManager.LocalPlayer.State.ObjectInMouth;
            if (objectInMouth == null) return;

            var meatObject = objectInMouth.AsMeatObject();
            if (meatObject != null)
            {
                denStorage.StoredMeatFoodValue += meatObject.Food;
                Globals.animalManager.LocalPlayer.State.ObjectInMouth = null;
                Globals.meatManager.DespawnMeatObject(meatObject);
                SaveDenStorage();
            }
        }

        // ================================================================
        //                       FEEDING
        // ================================================================

        private void FeedSingleMember(Animal animal)
        {
            try
            {
                if (animal?.State == null || currentDen == null) return;

                var denStorage = GetOrCreateDenStorage(currentDen);
                if (denStorage.StoredMeatFoodValue <= 0) return;

                float needed = animal.State.MaxFoodWithRegurgitant - animal.State.Food;
                if (needed <= 0) return;

                float toFeed = Mathf.Min(denStorage.StoredMeatFoodValue, needed);
                animal.State.Food += toFeed;
                denStorage.StoredMeatFoodValue -= toFeed;
                SaveDenStorage();
            }
            catch (Exception ex)
            {
                Log.LogError($"FeedSingleMember exception: {ex.Message}");
            }
        }

        // ================================================================
        //                       PACK ACTIONS
        // ================================================================

        private void KickMember(Animal member)
        {
            if (member == null) return;
            try
            {
                if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData.AdultOffsprings.Contains(member))
                {
                    if (!Globals.animalManager.LocalPlayer.Pack.PlayerPackData.WolvesToDisperse.Contains(member))
                    {
                        member.PersistentWolf.WolfState.Role = WolfRole.RankAndFile;
                        Globals.animalManager.LocalPlayer.Pack.PlayerPackData.WolvesToDisperse.Add(member);
                    }

                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.ConfirmWolfDispersal(member, true, true);
                }

                selectedMember = null;
            }
            catch (Exception ex)
            {
                Globals.Log("Kick failed: " + ex.Message);
            }
        }

        // ================================================================
        //                       DEN MISC ACTIONS
        // ================================================================

        private void FetchAllPups()
        {
            if (currentDen == null) return;
            if (Globals.animalManager?.LocalPlayer?.Pack?.PlayerPackData?.Pups == null) return;

            foreach (Animal pup in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Pups)
                pup.TeleportIntoDen(currentDen);
        }

        private void FetchAllPackMembers()
        {
            if (currentDen == null) return;
            if (Globals.animalManager?.LocalPlayer?.Pack?.PlayerPackData?.Members == null) return;

            foreach (Animal member in Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Members)
            {
                if (member == Globals.animalManager.LocalPlayer) continue;
                if (member.WolfDef.lifeStage < WolfLifeStage.Yearling) continue;
                if ((member.Position - currentDen.VisualPosition).sqrMagnitude < 100f) continue;

                member.TeleportIntoDen(currentDen);
            }
        }

        private void ExtractFromCarcasses()
        {
            if (currentDen == null) return;
            var denStorage = GetOrCreateDenStorage(currentDen);

            float capacity = 3000f;
            if (denStorage.StoredMeatFoodValue >= capacity)
            {
                denStorage.StoredMeatFoodValue = capacity;
            }

            float spaceLeft = capacity - denStorage.StoredMeatFoodValue;
            bool addedMeat = false;

            if (Globals.meatManager != null && Globals.meatManager.MeatObjects != null)
            {
                foreach (MeatObject meatObj in Globals.meatManager.MeatObjects)
                {
                    if (spaceLeft <= 0) break;

                    Carcass carcass = meatObj as Carcass;
                    if (carcass == null) continue;

                    float distSqr = (carcass.Position - currentDen.VisualPosition).sqrMagnitude;
                    if (distSqr <= 400f)
                    {
                        if (carcass.FoodUpdater != null && carcass.FoodUpdater.Food > 0)
                        {
                            float taken = carcass.FoodUpdater.Consume(spaceLeft);
                            if (taken > 0)
                            {
                                denStorage.StoredMeatFoodValue += taken;
                                spaceLeft -= taken;
                                addedMeat = true;
                            }
                        }
                    }
                }
            }

            if (addedMeat) SaveDenStorage();
        }

        private void FetchCarcasses()
        {
            Carcass[] all = FindObjectsOfType<Carcass>();
            foreach (Carcass carcass in all)
            {
                Vector3 nPos = currentDen.VisualPosition;
                nPos += Rnd(5f, 10f) * Vector3.right;
                nPos += Rnd(-5f, 5f) * Vector3.back;
                nPos.y = 1500f;

                if (Physics.Raycast(nPos, Vector3.down, out RaycastHit info, 1800f, -1))
                    carcass.Teleport(info.point, carcass.Rotation);
            }
        }

        private void GetRandomToy()
        {
            Vector3 nPos = Globals.animalManager.LocalPlayer.Position + (Vector3.up * .15f);
            Toy[] toys = FindObjectsOfType<Toy>();
            if (toys.Length == 0) return;
            int index = (int)Rnd(0, toys.Length - 1);
            toys[index].Teleport(nPos, toys[index].Rotation);
        }

        // ================================================================
        //                       SAVE / LOAD
        // ================================================================

        private string GetDenStoragePath()
        {
            if (sceneToSceneData == null || string.IsNullOrEmpty(sceneToSceneData.saveName))
                return null;
            return sceneToSceneData.saveName + ".dendata";
        }

        public static void SaveDenStorage()
        {
            try
            {
                if (Instance == null) return;
                string path = Instance.GetDenStoragePath();
                if (string.IsNullOrEmpty(path)) return;

                var lines = new List<string>();
                foreach (var kvp in DenStorage)
                    lines.Add($"{kvp.Key}|{kvp.Value.StoredMeatFoodValue}");

                File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to save den storage: {ex.Message}");
            }
        }

        public static void LoadDenStorage()
        {
            try
            {
                if (Instance == null) return;
                string path = Instance.GetDenStoragePath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                DenStorage.Clear();
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;

                    float meatValue = 0f;
                    float.TryParse(parts[1], out meatValue);
                    DenStorage[parts[0]] = new DenStorageData { StoredMeatFoodValue = meatValue };
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to load den storage: {ex.Message}");
            }
        }

        // ================================================================
        //                       GUI STYLES
        // ================================================================

        private void InitStyles()
        {
            if (stylesInitialized) return;

            bgTex = MakeTex(2, 2, new Color(0.12f, 0.12f, 0.15f, 0.95f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = bgTex;
            windowStyle.onNormal.background = bgTex;
            windowStyle.fontSize = 14;
            windowStyle.normal.textColor = new Color(0.95f, 0.85f, 0.5f);
            windowStyle.padding = new RectOffset(10, 10, 25, 10);

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                normal = { textColor = new Color(0.95f, 0.85f, 0.5f) }
            };

            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            titleBarStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.85f, 0.5f), background = MakeTex(2, 2, new Color(0.2f, 0.2f, 0.25f, 0.95f)) }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(2, 2, 2, 2)
            };

            selectedButtonStyle = new GUIStyle(buttonStyle);
            selectedButtonStyle.normal.textColor = Color.yellow;
            selectedButtonStyle.normal.background = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.3f));

            kickButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };

            activeTabStyle = new GUIStyle(tabStyle)
            {
                fontStyle = FontStyle.Bold
            };
            activeTabStyle.normal.textColor = Color.yellow;
            activeTabStyle.normal.background = MakeTex(1, 1, new Color(0.25f, 0.25f, 0.3f));

            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true
            };

            stylesInitialized = true;
        }

        private Texture2D MakeTex(int w, int h, Color col)
        {
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        // ==================== UTILITY ====================

        private float Rnd(float min, float max) => UnityEngine.Random.Range(min, max);
    }

    // ==================== DATA CLASSES ====================

    [Serializable]
    public class DenStorageData
    {
        public float StoredMeatFoodValue = 0f;
    }

    public static class WolfLifeStageExtensions
    {
        public static bool IsPup(this WolfLifeStage stage)
        {
            return stage == WolfLifeStage.SpringPup ||
                   stage == WolfLifeStage.SummerPup ||
                   stage == WolfLifeStage.LateSummerPup ||
                   stage == WolfLifeStage.UnbornPup;
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.rw.denexpansion";
        public const string PLUGIN_NAME = "DenExpansion";
        public const string PLUGIN_VERSION = "1.1.0";
    }
}