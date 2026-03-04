using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using SharedCommons;

namespace PackSwitcher
{
    [BepInPlugin("com.rw.packswitcher", "PackSwitcher", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        // Config
        public static ConfigEntry<KeyCode> CfgMenuToggleKey;

        // State
        private bool _showMenu = false;
        private Rect _windowRect = new Rect(50f, 50f, 700f, 600f);
        private Vector2 _scrollPosition = Vector2.zero;
        private bool _stylesInitialized = false;
        private bool _isScanning = false;
        private string _statusMessage = "";

        // Data
        private List<Flock> _allFlocks = new List<Flock>();

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _flockButtonStyle;
        private GUIStyle _joinButtonStyle;
        private GUIStyle _statusStyle;

        private void Awake()
        {
            CfgMenuToggleKey = Config.Bind("Keybinds", "MenuToggleKey", KeyCode.Keypad5,
                "Key to toggle Flock Joiner menu");

            // Apply Patches
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(Patch));

            Globals.Log("[PackSwitcher] Initialised.");
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;

            if (Input.GetKeyDown(CfgMenuToggleKey.Value))
            {
                _showMenu = !_showMenu;
                if (_showMenu) ScanAllFlocks();

                InputControls.ForceAllowCursor = _showMenu;
                InputControls.DisableCameraInput = _showMenu;
                InputControls.DisableInput = _showMenu;
            }
        }

        private void OnGUI()
        {
            if (!_showMenu) return;

            if (!_stylesInitialized)
            {
                InitializeStyles();
                _stylesInitialized = true;
            }

            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            _windowRect = GUI.Window(54321, _windowRect, DrawWindow, "🐺 Flock Joiner v2.0");
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.Space(10f);
            GUILayout.Label("All Flocks in World", _headerStyle);
            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Close", GUILayout.Width(80f)))
            {
                _showMenu = false;
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);

                if (GUILayout.Button("Scan Flocks", GUILayout.Width(120f)))
                {
                    ScanAllFlocks();
                }

                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
                GUILayout.EndHorizontal();

                // Separator Line
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(2f));

                if (!string.IsNullOrEmpty(_statusMessage))
                {
                    GUILayout.Label(_statusMessage, _statusStyle);
                    GUILayout.Space(5f);
                }

                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(450f));

                try
                {
                    GUILayout.Label($"<b>{_allFlocks.Count} Flocks Found:</b>", _flockButtonStyle);
                    GUILayout.Space(5f);

                    foreach (Flock flock in _allFlocks)
                    {
                        DrawFlockEntry(flock);
                    }
                }
                catch (Exception ex)
                {
                    // Catch GUI errors gracefully
                    Globals.Log($"GUI Error: {ex.Message}");
                }

                GUILayout.EndScrollView();
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 30f));
            }
        }

        private void DrawFlockEntry(Flock flock)
        {
            if (flock?.Pack == null) return;

            Pack pack = flock.Pack;
            bool isPlayerPack = pack.IsPlayerPack;

            GUILayout.BeginHorizontal("box");
            GUILayout.BeginVertical(GUILayout.Width(400f));

            GUILayout.Label($"<b>{flock.name}</b>", _flockButtonStyle);
            GUILayout.Label($"Pack: {pack.properName} ({pack.PackType})", _statusStyle);
            GUILayout.Label($"Living Wolves: {flock.LivingMembers.Count}", _statusStyle);

            if (Globals.animalManager?.LocalPlayer?.Pack != null)
            {
                float distance = Vector3.Distance(Globals.animalManager.LocalPlayer.Position, flock.Position);
                GUILayout.Label($"Distance: {distance:F0}m", _statusStyle);
            }

            if (isPlayerPack)
            {
                GUILayout.Label("<color=green>✅ Your current pack</color>", _statusStyle);
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            if (!isPlayerPack)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);

                if (GUILayout.Button("TELEPORT TO", _joinButtonStyle, GUILayout.Width(120f), GUILayout.Height(50f)))
                {
                    Globals.animalManager.LocalPlayer.Teleport(flock.Position, Quaternion.identity,
                        GroundingMode.GroundUpwardOrDownwardFully, false);
                }

                if (GUILayout.Button("JOIN FLOCK", _joinButtonStyle, GUILayout.Width(120f), GUILayout.Height(50f)))
                {
                    JoinFirstWolfInFlock(flock);
                }

                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            }
            else
            {
                GUI.backgroundColor = Color.gray;
                GUILayout.Button("YOUR PACK", _joinButtonStyle, GUILayout.Width(120f), GUILayout.Height(50f));
                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
        }

        private void JoinFirstWolfInFlock(Flock targetFlock)
        {
            if (Globals.animalManager.LocalPlayer == null) return;

            try
            {
                // Find first valid animal in target flock
                Animal targetAnimal = targetFlock.LivingMembers.FirstOrDefault(a => a != null && a.WolfDef != null);

                if (targetAnimal == null) return;

                // Move local player to new flock
                targetAnimal.Flock.TransferWolfFrom(Globals.animalManager.LocalPlayer.Flock,
                    Globals.animalManager.LocalPlayer);
                targetAnimal.Pack.AddPersistentWolf(Globals.animalManager.LocalPlayer.PersistentWolf, null);

                // Kick old members (or cleanup duplicates?) logic preserved from source
                // Copy list to avoid InvalidOperationException from modifying collection during iteration
                var membersToKick = new List<Animal>(Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Members);
                foreach (Animal member in membersToKick)
                {
                    if (member != Globals.animalManager.LocalPlayer)
                    {
                        KickMember(member);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Failed: " + ex.Message;
                Globals.Log($"Flock join failed: {ex}");
            }

            ScanAllFlocks();
        }

        private void KickMember(Animal member)
        {
            if (member == null) return;

            Globals.Log($"Attempting to kick {member.name}...");
            try
            {
                if (Globals.animalManager.LocalPlayer.Pack.PlayerPackData.Members.Contains(member))
                {
                    if (!Globals.animalManager.LocalPlayer.Pack.PlayerPackData.WolvesToDisperse.Contains(member))
                    {
                        member.PersistentWolf.WolfState.Role = WolfRole.RankAndFile;
                        Globals.animalManager.LocalPlayer.Pack.PlayerPackData.WolvesToDisperse.Add(member);
                    }

                    Globals.animalManager.LocalPlayer.Pack.PlayerPackData.ConfirmWolfDispersal(member, true, true);
                }
                else
                {
                    // Fallback removal
                    if (Globals.animalManager.LocalPlayer.Pack != null && member.PersistentWolf != null)
                    {
                        Globals.animalManager.LocalPlayer.Pack.RemovePersistentWolf(member.PersistentWolf);
                    }
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Kick failed: {ex.Message}");
            }
        }

        private void ScanAllFlocks()
        {
            if (Globals.animalManager?.LocalPlayer == null) return;

            _isScanning = true;
            _allFlocks.Clear();
            _statusMessage = "🔍 Scanning all flocks...";

            FlockManager flockManager = FindObjectOfType<FlockManager>();
            if (flockManager != null)
            {
                _allFlocks.AddRange(flockManager.Flocks);
            }

            PackManager packManager = FindObjectOfType<PackManager>();
            if (packManager != null)
            {
                foreach (Pack pack in packManager.Packs)
                {
                    if (pack.MainFlock != null && !_allFlocks.Contains(pack.MainFlock))
                    {
                        _allFlocks.Add(pack.MainFlock);
                    }

                    foreach (Flock subFlock in pack.Flocks)
                    {
                        if (!_allFlocks.Contains(subFlock))
                        {
                            _allFlocks.Add(subFlock);
                        }
                    }
                }
            }

            // Cleanup nulls
            _allFlocks.RemoveAll(f => f == null || f.Pack == null);
            _isScanning = false;
        }

        private void InitializeStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.yellow }
            };

            _flockButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                richText = true
            };

            _joinButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.gray },
                wordWrap = true
            };
        }
    }
}