using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using UnityEngine.SceneManagement;
using SharedCommons;

namespace Superwolf
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        internal static ManualLogSource Log;
        private Harmony harmony;

        // GUI state
        private bool showGUI = false;
        private Rect windowRect = new Rect(20, 20, 450, 780);
        private Vector2 scrollPosition = Vector2.zero;

        // Time scale state
        public static bool FreezeGameWhileMenuOpen = true;

        // Flight mode state - static so Harmony patches can access
        public static bool FlightModeEnabled = false;
        private float flightSpeed = 20f;

        public static ConfigEntry<KeyCode> CfgToggleKey;

        // Animation speed fix
        public static bool FixAnimationSpeed = true;
        public static float MaxAnimationSpeedMultiplier = 2f;

        // Camera zoom limits
        public static bool UnlimitedCameraZoom = false;
        public static float CustomMaxCameraDistance = 100f;

        // God mode toggles
        public static bool UnlimitedHealth = false;
        public static bool UnlimitedFood = false;
        public static bool UnlimitedEnergy = false;
        public static bool UnlimitedWakefulness = false;

        // Locked values - static so Harmony patches can access
        public static int LockedStrength;
        public static int LockedHealth;
        public static int LockedAgility;
        public static int LockedStamina;
        public static float LockedBodySize = 1f;

        // Cached references
        private NormalCameraMover normalCameraMover;
        private SceneToSceneData sceneToSceneData;
        private float originalMaxCameraDistance = 0f;

        // Ability values (for sliders)
        private int strengthValue;
        private int healthValue;
        private int agilityValue;
        private int staminaValue;
        private float bodySizeValue = 1f;

        // GUI styles
        private GUIStyle windowStyle;
        private GUIStyle headerStyle;
        private GUIStyle labelStyle;
        private GUIStyle sliderLabelStyle;
        private GUIStyle boxStyle;
        private GUIStyle toggleStyle;
        private Texture2D darkBackground;
        private Texture2D darkerBackground;
        private bool stylesInitialized = false;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // Config
            CfgToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F8, "Key to toggle the Superwolf menu");

            Globals.Log($"Plugin {PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} is loaded!");
        }

        private void Update()
        {
            if (Globals.MenuIsOpen)
            {
                showGUI = false;
            }
            else
            {
                if (Input.GetKeyDown(CfgToggleKey.Value))
                {
                    if (!showGUI) RefreshReferences();
                    showGUI = !showGUI;

                    InputControls.ForceAllowCursor = showGUI;
                    InputControls.DisableCameraInput = showGUI;
                    InputControls.DisableInput = showGUI;
                }
            }

            // Block all functionality in multiplayer
            if (IsMultiplayer()) return;

            // Handle flight mode (skip if game is frozen)
            if (FlightModeEnabled && Globals.animalManager?.LocalPlayer != null && Time.timeScale > 0)
            {
                HandleFlightMode();
            }

            // Continuously enforce locked values (skip if frozen)
            if (Time.timeScale > 0)
            {
                EnforceLocks();
                EnforceGodMode();
            }
        }

        private void LateUpdate()
        {
            // Block all functionality in multiplayer
            if (IsMultiplayer()) return;

            // Ensure position stays where we set it in flight mode
            if (FlightModeEnabled && Globals.animalManager?.LocalPlayer != null && Time.timeScale > 0)
            {
                var rb = Globals.animalManager.LocalPlayer.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            EnforceLocks();
            ApplyBodySize();
        }

        /// <summary>
        /// Check if we're in multiplayer mode
        /// </summary>
        private bool IsMultiplayer()
        {
            if (sceneToSceneData == null)
            {
                sceneToSceneData = SceneToSceneData.Instance;
            }

            return sceneToSceneData != null && sceneToSceneData.IsMultiplayer;
        }

        private void EnforceLocks()
        {
            if (Globals.animalManager?.LocalPlayer?.WolfDef == null) return;

            // Enforce ability locks
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.strength = LockedStrength;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.health = LockedHealth;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.agility = LockedAgility;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.stamina = LockedStamina;

            // Enforce body size lock
            if (Globals.animalManager.LocalPlayer.Physical != null)
                Globals.animalManager.LocalPlayer.Physical.SizeScale = LockedBodySize;
        }

        private void EnforceGodMode()
        {
            if (Globals.animalManager?.LocalPlayer?.State == null) return;

            if (UnlimitedHealth)
                Globals.animalManager.LocalPlayer.State.Health = Globals.animalManager.LocalPlayer.State.MaxHealth;

            if (UnlimitedFood)
                Globals.animalManager.LocalPlayer.State.Food =
                    Globals.animalManager.LocalPlayer.State.MaxFoodWithoutRegurgitant;

            if (UnlimitedEnergy)
                Globals.animalManager.LocalPlayer.State.Energy = Globals.animalManager.LocalPlayer.State.MaxEnergy;

            if (UnlimitedWakefulness)
                Globals.animalManager.LocalPlayer.State.Wakefulness = 1f;
        }

        private void RefreshReferences()
        {
            normalCameraMover = FindObjectOfType<NormalCameraMover>();
            if (normalCameraMover != null && originalMaxCameraDistance == 0)
            {
                originalMaxCameraDistance = normalCameraMover.maxDistance;
                CustomMaxCameraDistance = originalMaxCameraDistance;
            }
        }

        private void HandleFlightMode()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            // Get full camera direction for rotation (including pitch)
            Vector3 cameraForward = Camera.main.transform.forward;

            // Make wolf face camera direction with full pitch
            if (cameraForward != Vector3.zero && Globals.animalManager.LocalPlayer.Physical != null)
            {
                Quaternion targetRotation = Quaternion.LookRotation(cameraForward, Vector3.up);
                Globals.animalManager.LocalPlayer.Physical.Rotation = targetRotation;
            }

            // Use Physical.Position for proper movement
            Vector3 currentPos = Globals.animalManager.LocalPlayer.Physical != null
                ? Globals.animalManager.LocalPlayer.Physical.Position
                : Globals.animalManager.LocalPlayer.transform.position;

            // Basic WASD flight controls - relative to camera
            Vector3 moveDirection = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) moveDirection += Camera.main.transform.forward;
            if (Input.GetKey(KeyCode.S)) moveDirection -= Camera.main.transform.forward;
            if (Input.GetKey(KeyCode.A)) moveDirection -= Camera.main.transform.right;
            if (Input.GetKey(KeyCode.D)) moveDirection += Camera.main.transform.right;
            if (Input.GetKey(KeyCode.Space)) moveDirection += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) moveDirection -= Vector3.up;

            // Speed modifier
            float currentSpeed = flightSpeed;
            if (Input.GetKey(KeyCode.LeftShift)) currentSpeed *= 3f;

            if (moveDirection != Vector3.zero)
            {
                Vector3 newPosition = currentPos + moveDirection.normalized * currentSpeed * Time.deltaTime;

                // Set position through Physical if available
                Globals.animalManager.LocalPlayer.Physical.Position = newPosition;
            }
        }

        private void EnableFlightMode()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            FlightModeEnabled = true;

            // Make rigidbody kinematic
            var rb = Globals.animalManager.LocalPlayer.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            Log.LogInfo("Flight mode enabled");
        }

        private void DisableFlightMode()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            FlightModeEnabled = false;

            // Restore rigidbody
            var rb = Globals.animalManager.LocalPlayer.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }

            Log.LogInfo("Flight mode disabled");
        }

        private Texture2D MakeTex(int width, int height, Color color)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = color;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            // Create dark background textures
            darkBackground = MakeTex(2, 2, new Color(0.12f, 0.12f, 0.15f, 0.95f));
            darkerBackground = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 0.98f));

            // Window style with dark background
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = darkBackground;
            windowStyle.onNormal.background = darkBackground;
            windowStyle.fontSize = 14;
            windowStyle.normal.textColor = new Color(0.95f, 0.85f, 0.5f);
            windowStyle.padding = new RectOffset(10, 10, 25, 10);

            // Header style
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 15;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            headerStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
            headerStyle.margin = new RectOffset(0, 0, 8, 8);

            // Label style
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            // Slider label style
            sliderLabelStyle = new GUIStyle(GUI.skin.label);
            sliderLabelStyle.fontSize = 12;
            sliderLabelStyle.alignment = TextAnchor.MiddleRight;
            sliderLabelStyle.fixedWidth = 60;
            sliderLabelStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);

            // Box style for sections
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = darkerBackground;
            boxStyle.padding = new RectOffset(8, 8, 8, 8);
            boxStyle.margin = new RectOffset(0, 0, 5, 5);

            // Toggle style
            toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            toggleStyle.onNormal.textColor = new Color(0.5f, 1f, 0.5f);

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!showGUI) return;

            InitStyles();

            windowRect = GUILayout.Window(12346, windowRect, DrawWindow, "🐺 Wolf Editor v1.6", windowStyle);
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.Space(5);

            // Dark box for content
            GUILayout.BeginVertical(boxStyle);

            // Freeze game toggle
            FreezeGameWhileMenuOpen =
                GUILayout.Toggle(FreezeGameWhileMenuOpen, " ⏸️ Freeze Game While Menu Open", toggleStyle);

            // Refresh button
            GUI.backgroundColor = new Color(0.3f, 0.4f, 0.5f);
            if (GUILayout.Button("🔄 Refresh References", GUILayout.Height(28)))
            {
                RefreshReferences();
            }

            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);

            // Block in multiplayer
            if (IsMultiplayer()) return;

            if (Globals.animalManager?.LocalPlayer?.WolfDef == null)
            {
                GUILayout.Label("⚠️ No player wolf found. Enter the game world first.", labelStyle);
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            // Wolf name
            GUILayout.Label($"🐺 {Globals.animalManager.LocalPlayer.WolfDef.NickNameOrBirthName}", headerStyle);

            GUILayout.EndVertical();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            // ==================== GOD MODE ====================
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("══ GOD MODE ══", headerStyle);

            UnlimitedHealth = GUILayout.Toggle(UnlimitedHealth, "Unlimited Health", toggleStyle);
            UnlimitedFood = GUILayout.Toggle(UnlimitedFood, "Unlimited Food", toggleStyle);
            UnlimitedEnergy = GUILayout.Toggle(UnlimitedEnergy, "Unlimited Energy", toggleStyle);
            UnlimitedWakefulness = GUILayout.Toggle(UnlimitedWakefulness, "Unlimited Wakefulness", toggleStyle);

            GUILayout.EndVertical();

            // ==================== INNATE ABILITIES ====================
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("══ INNATE ABILITIES ══", headerStyle);

            // Lock toggle
            LockedStrength = strengthValue;
            LockedHealth = healthValue;
            LockedAgility = agilityValue;
            LockedStamina = staminaValue;

            GUILayout.Space(5);

            // Strength
            GUILayout.BeginHorizontal();
            GUILayout.Label("Strength:", labelStyle, GUILayout.Width(80));
            strengthValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(strengthValue, -10, 999, GUILayout.Width(180)));
            GUILayout.Label(strengthValue.ToString("+#;-#;0"), sliderLabelStyle);
            GUILayout.EndHorizontal();

            // Health
            GUILayout.BeginHorizontal();
            GUILayout.Label("Health:", labelStyle, GUILayout.Width(80));
            healthValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(healthValue, -10, 999, GUILayout.Width(180)));
            GUILayout.Label(healthValue.ToString("+#;-#;0"), sliderLabelStyle);
            GUILayout.EndHorizontal();

            // Agility
            GUILayout.BeginHorizontal();
            GUILayout.Label("Agility:", labelStyle, GUILayout.Width(80));
            agilityValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(agilityValue, -10, 999, GUILayout.Width(180)));
            GUILayout.Label(agilityValue.ToString("+#;-#;0"), sliderLabelStyle);
            GUILayout.EndHorizontal();

            // Stamina
            GUILayout.BeginHorizontal();
            GUILayout.Label("Stamina:", labelStyle, GUILayout.Width(80));
            staminaValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(staminaValue, -10, 999, GUILayout.Width(180)));
            GUILayout.Label(staminaValue.ToString("+#;-#;0"), sliderLabelStyle);
            GUILayout.EndHorizontal();

            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();

            // ==================== BODY SIZE ====================
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("══ BODY SIZE ══", headerStyle);

            // Animation speed fix toggle
            FixAnimationSpeed = GUILayout.Toggle(FixAnimationSpeed, " Cap Animation Speed", toggleStyle);

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", labelStyle, GUILayout.Width(80));
            // Use logarithmic scale for better control: 0.01 to 100
            float logMin = Mathf.Log10(0.01f);
            float logMax = Mathf.Log10(100f);
            float logValue = Mathf.Log10(Mathf.Max(0.01f, bodySizeValue));
            logValue = GUILayout.HorizontalSlider(logValue, logMin, logMax, GUILayout.Width(180));
            bodySizeValue = Mathf.Pow(10f, logValue);
            GUILayout.Label(bodySizeValue.ToString("F2") + "x", sliderLabelStyle);
            GUILayout.EndHorizontal();

            if (FixAnimationSpeed)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Max Anim:", labelStyle, GUILayout.Width(80));
                MaxAnimationSpeedMultiplier =
                    GUILayout.HorizontalSlider(MaxAnimationSpeedMultiplier, 0.5f, 5f, GUILayout.Width(180));
                GUILayout.Label(MaxAnimationSpeedMultiplier.ToString("F1") + "x", sliderLabelStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            // ==================== CAMERA ====================
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("══ CAMERA ══", headerStyle);

            bool newUnlimitedZoom = GUILayout.Toggle(UnlimitedCameraZoom, " Extended Camera Zoom", toggleStyle);
            if (newUnlimitedZoom != UnlimitedCameraZoom)
            {
                UnlimitedCameraZoom = newUnlimitedZoom;
                ApplyCameraZoom();
            }

            if (UnlimitedCameraZoom && normalCameraMover != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Max Dist:", labelStyle, GUILayout.Width(80));
                float newMaxDist = GUILayout.HorizontalSlider(CustomMaxCameraDistance, originalMaxCameraDistance, 500f,
                    GUILayout.Width(180));
                if (Mathf.Abs(newMaxDist - CustomMaxCameraDistance) > 0.1f)
                {
                    CustomMaxCameraDistance = newMaxDist;
                    ApplyCameraZoom();
                }

                GUILayout.Label(CustomMaxCameraDistance.ToString("F0") + "m", sliderLabelStyle);
                GUILayout.EndHorizontal();

                GUILayout.Label($"Original: {originalMaxCameraDistance:F0}m", labelStyle);
            }

            GUILayout.EndVertical();

            // ==================== FLIGHT MODE ====================
            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("══ FLIGHT MODE ══", headerStyle);

            GUI.backgroundColor = FlightModeEnabled ? new Color(0.7f, 0.2f, 0.2f) : new Color(0.2f, 0.5f, 0.7f);

            if (GUILayout.Button(FlightModeEnabled ? "Disable" : "Enable", GUILayout.Height(38)))
            {
                if (FlightModeEnabled)
                {
                    DisableFlightMode();
                }
                else
                {
                    EnableFlightMode();
                }
            }

            GUI.backgroundColor = Color.white;

            if (FlightModeEnabled)
            {
                GUILayout.Space(5);
                GUILayout.Label("Wolf tilts with camera (full 3D rotation)", labelStyle);
                GUILayout.Label("WASD: move | Space/Ctrl: up/down", labelStyle);
                GUILayout.Label("Shift: 3x speed boost", labelStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Speed:", labelStyle, GUILayout.Width(80));
                flightSpeed = GUILayout.HorizontalSlider(flightSpeed, 5f, 200f, GUILayout.Width(180));
                GUILayout.Label(flightSpeed.ToString("F0"), sliderLabelStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.EndScrollView();

            GUILayout.Space(5);
            GUILayout.Label($"Press {CfgToggleKey.Value} to toggle this window", labelStyle);

            GUI.DragWindow();
        }

        private void ApplyAbilities()
        {
            if (Globals.animalManager?.LocalPlayer?.WolfDef == null) return;

            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.strength = strengthValue;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.health = healthValue;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.agility = agilityValue;
            Globals.animalManager.LocalPlayer.WolfDef.innateAbilities.stamina = staminaValue;

            LockedStrength = strengthValue;
            LockedHealth = healthValue;
            LockedAgility = agilityValue;
            LockedStamina = staminaValue;
        }

        private void ApplyBodySize()
        {
            if (Globals.animalManager?.LocalPlayer?.WolfDef == null) return;

            // DIRECTLY set the Physical.SizeScale to bypass blendshape limits
            if (Globals.animalManager.LocalPlayer.Physical != null)
            {
                Globals.animalManager.LocalPlayer.Physical.SizeScale = bodySizeValue;

                // Update locked value if locking
                LockedBodySize = bodySizeValue;
            }
        }

        private void ApplyCameraZoom()
        {
            if (normalCameraMover == null) return;

            if (UnlimitedCameraZoom)
            {
                normalCameraMover.maxDistance = CustomMaxCameraDistance;
            }
            else
            {
                normalCameraMover.maxDistance = originalMaxCameraDistance;
            }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.rw.superwolf";
        public const string PLUGIN_NAME = "Superwolf";
        public const string PLUGIN_VERSION = "1.6.0";
    }

    // ==================== HARMONY PATCHES ====================

    /// <summary>
    /// Patch to bypass the InnateAbilitiesCheatProof clamping
    /// </summary>
    [HarmonyPatch(typeof(WolfDefinition))]
    [HarmonyPatch("InnateAbilitiesCheatProof", MethodType.Getter)]
    public static class InnateAbilitiesCheatProofPatch
    {
        static bool Prefix(WolfDefinition __instance, ref AbilityBlock __result)
        {
            // Return the raw innate abilities without clamping
            __result = __instance.innateAbilities;
            return false; // Skip original method
        }
    }

    /// <summary>
    /// Patch WolfCosmetics.UpdateBodySize to preserve our locked scale
    /// </summary>
    [HarmonyPatch(typeof(WolfCosmetics))]
    [HarmonyPatch("UpdateBodySize")]
    public static class UpdateBodySizePatch
    {
        static void Postfix(WolfCosmetics __instance)
        {
            var animal = __instance.GetComponentInParent<Animal>();
            if (animal != null && animal.IsLocalPlayer && animal.Physical != null)
            {
                animal.Physical.SizeScale = Plugin.LockedBodySize;
            }
        }
    }

    /// <summary>
    /// Patch AnimalAnimator to cap animation speed when wolf is large
    /// </summary>
    [HarmonyPatch(typeof(AnimalAnimator))]
    [HarmonyPatch("MainSpeed", MethodType.Setter)]
    public static class AnimalAnimatorMainSpeedPatch
    {
        static void Prefix(AnimalAnimator __instance, ref float value)
        {
            if (!Plugin.FixAnimationSpeed) return;

            var animal = __instance.GetComponentInParent<Animal>();
            if (animal != null && animal.IsLocalPlayer)
            {
                // Cap the animation speed to prevent ridiculous running animations
                value = Mathf.Clamp(value, -Plugin.MaxAnimationSpeedMultiplier, Plugin.MaxAnimationSpeedMultiplier);
            }
        }
    }

    /// <summary>
    /// Patch PhysicalMover.MovePlayerTo to skip grounding when flight mode is active
    /// </summary>
    [HarmonyPatch(typeof(PhysicalMover))]
    [HarmonyPatch("MovePlayerTo")]
    public static class MovePlayerToPatch
    {
        static bool Prefix()
        {
            if (Plugin.FlightModeEnabled)
            {
                return false; // Don't run original
            }

            return true;
        }
    }

    /// <summary>
    /// Patch PhysicalMover.MoveNPCTo as backup
    /// </summary>
    [HarmonyPatch(typeof(PhysicalMover))]
    [HarmonyPatch("MoveNPCTo")]
    public static class MoveNPCToPatch
    {
        static bool Prefix(PhysicalMover __instance)
        {
            if (Plugin.FlightModeEnabled)
            {
                var animal = __instance.GetComponentInParent<Animal>();
                if (animal != null && animal.IsLocalPlayer)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Patch AnimalMover.MoveGrounded to skip grounding in flight mode
    /// </summary>
    [HarmonyPatch(typeof(AnimalMover))]
    [HarmonyPatch("MoveGrounded")]
    public static class AnimalMoverMoveGroundedPatch
    {
        static bool Prefix(AnimalMover __instance)
        {
            if (Plugin.FlightModeEnabled)
            {
                var animal = __instance.GetComponentInParent<Animal>();
                if (animal != null && animal.IsLocalPlayer)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Patch AnimalMover.PerformBasicMovement to skip movement in flight mode
    /// </summary>
    [HarmonyPatch(typeof(AnimalMover))]
    [HarmonyPatch("PerformBasicMovement")]
    public static class AnimalMoverPerformBasicMovementPatch
    {
        static bool Prefix(AnimalMover __instance)
        {
            if (Plugin.FlightModeEnabled)
            {
                var animal = __instance.GetComponentInParent<Animal>();
                if (animal != null && animal.IsLocalPlayer)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
