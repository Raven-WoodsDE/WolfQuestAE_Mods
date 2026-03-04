using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using UnityEngine;
using WolfQuestEp3;
using UnityEngine.SceneManagement;
using SharedCommons;

namespace Customizer
{
    /// <summary>
    /// Transform Explorer / Outfitter Module
    /// Provides a GUI for exploring wolf bone hierarchy and attaching custom AssetBundle objects.
    /// </summary>
    public class Outfitter : MonoBehaviour
    {
        // GUI State
        private bool _showWindow = false;
        private Rect _windowRect;
        private bool _windowRectInitialized = false;
        private Vector2 _scrollPosition = Vector2.zero;
        private Vector2 _mainScrollPosition = Vector2.zero;
        private Vector2 _attachmentsScrollPosition = Vector2.zero;

        // Selected transform
        private Transform _selectedRoot;
        private Transform _selectedChild;
        private string _searchQuery = "";
        private List<Transform> _searchResults = new List<Transform>();

        // Hierarchy state (expanded nodes)
        private HashSet<Transform> _expandedNodes = new HashSet<Transform>();

        // AssetBundle 
        private string _assetBundlePath = "";
        private string _assetName = "";
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();

        // Attachment offsets (for current/new attachment)
        private Vector3 _positionOffset = Vector3.zero;
        private Vector3 _rotationOffset = Vector3.zero;
        private Vector3 _scaleOffset = Vector3.one;

        // Multiple attachments tracking
        private List<Containers> _activeAttachments = new List<Containers>();
        private int _selectedAttachmentIndex = -1;

        // Outfit system
        private string _outfitName = "MyOutfit";
        private List<string> _savedOutfitNames = new List<string>();

        // Gizmos
        private bool _showGizmos = true;
        private Material _gizmoMaterial;

        private string OutfitsFolder => Path.Combine(Paths.PluginPath, Plugin.AssetRoot, "Outfits");

        private void Start()
        {
            RefreshOutfitList();
        }

        private void Update()
        {
            if (Globals.MenuIsOpen)
            {
                _showWindow = false;
            }
            else
            {
                if (Input.GetKeyDown(Plugin.Instance.ToggleKey))
                {
                    _showWindow = !_showWindow;

                    if (_showWindow)
                    {
                        if (_selectedRoot == null)
                            TrySelectPlayerWolf();
                    }

                    InputControls.ForceAllowCursor = _showWindow;
                    InputControls.DisableCameraInput = _showWindow;
                    InputControls.DisableInput = _showWindow;
                }
            }
        }

        private void TrySelectPlayerWolf()
        {
            if (Globals.animalManager?.LocalPlayer != null)
            {
                _selectedRoot = Globals.animalManager.LocalPlayer.transform;
            }
        }

        /// <summary>
        /// Loads an outfit by name from the Outfits folder.
        /// Can be called externally (e.g., from biography parsing).
        /// </summary>
        public void LoadOutfitByName(string outfitName, Transform wolfRoot)
        {
            _selectedRoot = wolfRoot;
            LoadOutfit(outfitName);
        }

        private void OnGUI()
        {
            if (!_showWindow) return;

            // Initialize window rect based on screen resolution (once)
            if (!_windowRectInitialized)
            {
                float windowWidth = Mathf.Min(700, Screen.width * 0.8f);
                float windowHeight = Mathf.Min(750, Screen.height * 0.85f);
                float windowX = (Screen.width - windowWidth) * .5f;
                float windowY = (Screen.height - windowHeight) * .5f;
                _windowRect = new Rect(windowX, windowY, windowWidth, windowHeight);
                _windowRectInitialized = true;
            }

            // Clamp window to screen bounds
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);

            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
            _windowRect = GUI.Window(9999, _windowRect, DrawWindow, "Outfitter");
        }

        private void DrawWindow(int windowId)
        {
            _mainScrollPosition = GUILayout.BeginScrollView(_mainScrollPosition);

            GUILayout.BeginVertical();

            // === Target Selection (simplified) ===
            DrawTargetSelection();

            GUILayout.Space(5);

            // === Search Bar ===
            DrawSearchBar();

            GUILayout.Space(5);

            // === Main Content Area ===
            float panelHeight = Mathf.Min(300, _windowRect.height * 0.35f);

            // Hierarchy panel (full width)
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Bone Hierarchy", EditorLabelStyle());
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(panelHeight));

            if (_searchResults.Count > 0)
            {
                foreach (var result in _searchResults)
                {
                    if (GUILayout.Button(GetFullPath(result),
                            result == _selectedChild ? SelectedButtonStyle() : GUI.skin.button))
                    {
                        _selectedChild = result;
                    }
                }
            }
            else if (_selectedRoot != null)
            {
                DrawHierarchyNode(_selectedRoot, 0);
            }
            else
            {
                GUILayout.Label("Press 'Select Player' first", WarningLabelStyle());
            }

            GUILayout.EndScrollView();

            // Show selected bone info inline
            if (_selectedChild != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Selected Bone: ", EditorLabelStyle(), GUILayout.Width(100));
                GUILayout.Label($"{_selectedChild.name}", InfoLabelStyle());
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy Path", GUILayout.Width(80)))
                {
                    string path = GetFullPath(_selectedChild);
                    GUIUtility.systemCopyBuffer = path;
                    Globals.Log($"Copied: {path}");
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.Space(5);

            // === AssetBundle & Attachment Section ===
            DrawAssetBundleSection();

            GUILayout.Space(10);

            // === Active Attachments ===
            DrawActiveAttachments();

            GUILayout.Space(10);

            // === Outfit System ===
            DrawOutfitSection();

            GUILayout.EndVertical();

            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawTargetSelection()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Select Player Wolf", GUILayout.Width(130)))
            {
                TrySelectPlayerWolf();
            }

            if (GUILayout.Button("Log Bones", GUILayout.Width(80)))
            {
                if (_selectedRoot != null)
                {
                    LogAllChildren(_selectedRoot, "");
                }
            }

            // Gizmo toggle
            _showGizmos = GUILayout.Toggle(_showGizmos, "Gizmos", GUILayout.Width(70));

            GUILayout.FlexibleSpace();

            if (_selectedRoot != null)
            {
                GUILayout.Label($"Target: {_selectedRoot.name}", InfoLabelStyle());
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSearchBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(50));

            string newQuery = GUILayout.TextField(_searchQuery, GUILayout.Width(500));
            if (newQuery != _searchQuery)
            {
                _searchQuery = newQuery;
                UpdateSearchResults();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                _searchQuery = "";
                _searchResults.Clear();
            }

            GUILayout.EndHorizontal();
        }

        private void UpdateSearchResults()
        {
            _searchResults.Clear();

            if (string.IsNullOrEmpty(_searchQuery) || _selectedRoot == null)
                return;

            SearchRecursive(_selectedRoot, _searchQuery.ToLowerInvariant());
        }

        private void SearchRecursive(Transform parent, string query)
        {
            if (parent.name.ToLowerInvariant().Contains(query))
            {
                _searchResults.Add(parent);
            }

            foreach (Transform child in parent)
            {
                SearchRecursive(child, query);
            }
        }

        private void DrawHierarchyNode(Transform node, int depth)
        {
            bool hasChildren = node.childCount > 0;
            bool isExpanded = _expandedNodes.Contains(node);
            bool isSelected = node == _selectedChild;

            GUILayout.BeginHorizontal();

            GUILayout.Space(depth * 15);

            if (hasChildren)
            {
                if (GUILayout.Button(isExpanded ? "▼" : "▶", GUILayout.Width(20)))
                {
                    if (isExpanded)
                        _expandedNodes.Remove(node);
                    else
                        _expandedNodes.Add(node);
                }
            }
            else
            {
                GUILayout.Space(24);
            }

            var style = isSelected ? SelectedButtonStyle() : GUI.skin.button;
            string label = hasChildren ? $"{node.name} ({node.childCount})" : node.name;

            if (GUILayout.Button(label, style))
            {
                _selectedChild = node;
            }

            GUILayout.EndHorizontal();

            if (hasChildren && isExpanded)
            {
                foreach (Transform child in node)
                {
                    DrawHierarchyNode(child, depth + 1);
                }
            }
        }

        private void DrawAssetBundleSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Spawn Attachment", EditorLabelStyle());

            // Bundle path
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bundle:", GUILayout.Width(60));
            _assetBundlePath = GUILayout.TextField(_assetBundlePath);
            if (GUILayout.Button("Load", GUILayout.Width(50)))
            {
                LoadAssetBundle(_assetBundlePath);
            }

            GUILayout.EndHorizontal();

            // Asset name
            GUILayout.BeginHorizontal();
            GUILayout.Label("Asset:", GUILayout.Width(60));
            _assetName = GUILayout.TextField(_assetName);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Position offset (R=X, G=Y, B=Z)
            GUILayout.Label("Position Offset: (R=X, G=Y, B=Z)", EditorLabelStyle());
            GUILayout.BeginHorizontal();
            GUI.color = new Color(1f, 0.5f, 0.5f); // Red for X
            GUILayout.Label("X:", GUILayout.Width(20));
            GUI.color = Color.white;
            _positionOffset.x = DrawFloatSlider(_positionOffset.x, -1f, 1f);
            GUI.color = new Color(0.5f, 1f, 0.5f); // Green for Y
            GUILayout.Label("Y:", GUILayout.Width(20));
            GUI.color = Color.white;
            _positionOffset.y = DrawFloatSlider(_positionOffset.y, -1f, 1f);
            GUI.color = new Color(0.5f, 0.5f, 1f); // Blue for Z
            GUILayout.Label("Z:", GUILayout.Width(20));
            GUI.color = Color.white;
            _positionOffset.z = DrawFloatSlider(_positionOffset.z, -1f, 1f);
            GUILayout.EndHorizontal();

            // Rotation offset
            GUILayout.Label("Rotation Offset:", EditorLabelStyle());
            GUILayout.BeginHorizontal();
            GUI.color = new Color(1f, 0.5f, 0.5f);
            GUILayout.Label("X:", GUILayout.Width(20));
            GUI.color = Color.white;
            _rotationOffset.x = DrawFloatSlider(_rotationOffset.x, -180f, 180f);
            GUI.color = new Color(0.5f, 1f, 0.5f);
            GUILayout.Label("Y:", GUILayout.Width(20));
            GUI.color = Color.white;
            _rotationOffset.y = DrawFloatSlider(_rotationOffset.y, -180f, 180f);
            GUI.color = new Color(0.5f, 0.5f, 1f);
            GUILayout.Label("Z:", GUILayout.Width(20));
            GUI.color = Color.white;
            _rotationOffset.z = DrawFloatSlider(_rotationOffset.z, -180f, 180f);
            GUILayout.EndHorizontal();

            // Scale
            GUILayout.Label("Scale:", EditorLabelStyle());
            GUILayout.BeginHorizontal();
            GUI.color = new Color(1f, 0.5f, 0.5f);
            GUILayout.Label("X:", GUILayout.Width(20));
            GUI.color = Color.white;
            _scaleOffset.x = DrawFloatSlider(_scaleOffset.x, 0.01f, 5f);
            GUI.color = new Color(0.5f, 1f, 0.5f);
            GUILayout.Label("Y:", GUILayout.Width(20));
            GUI.color = Color.white;
            _scaleOffset.y = DrawFloatSlider(_scaleOffset.y, 0.01f, 5f);
            GUI.color = new Color(0.5f, 0.5f, 1f);
            GUILayout.Label("Z:", GUILayout.Width(20));
            GUI.color = Color.white;
            _scaleOffset.z = DrawFloatSlider(_scaleOffset.z, 0.01f, 5f);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Spawn button
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn & Attach to Selected Bone"))
            {
                SpawnAndAttach();
            }

            if (GUILayout.Button("Reset Offsets"))
            {
                _positionOffset = Vector3.zero;
                _rotationOffset = Vector3.zero;
                _scaleOffset = Vector3.one;
            }

            GUILayout.EndHorizontal();

            // Show loaded bundles
            if (_loadedBundles.Count > 0)
            {
                GUILayout.Label($"Loaded Bundles: {_loadedBundles.Count}", InfoLabelStyle());
            }

            GUILayout.EndVertical();
        }

        private float DrawFloatSlider(float value, float min, float max)
        {
            float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(80));
            string textValue = GUILayout.TextField(newValue.ToString("F3"), GUILayout.Width(60));
            if (float.TryParse(textValue, out float parsed))
            {
                newValue = Mathf.Clamp(parsed, min, max);
            }

            return newValue;
        }

        private void DrawActiveAttachments()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Active Attachments ({_activeAttachments.Count})", EditorLabelStyle());

            _attachmentsScrollPosition = GUILayout.BeginScrollView(_attachmentsScrollPosition, GUILayout.Height(120));

            for (int i = 0; i < _activeAttachments.Count; i++)
            {
                var att = _activeAttachments[i];
                bool isSelected = i == _selectedAttachmentIndex;

                GUILayout.BeginHorizontal();

                if (GUILayout.Button(isSelected ? "►" : " ", GUILayout.Width(20)))
                {
                    _selectedAttachmentIndex = i;
                    // Load this attachment's offsets into the sliders
                    _positionOffset = att.PositionOffset;
                    _rotationOffset = att.RotationOffset;
                    _scaleOffset = att.Scale;
                }

                string boneName = att.TargetBone != null ? att.TargetBone.name : "???";
                GUILayout.Label($"{att.AssetName} → {boneName}", GUILayout.Width(350));

                if (GUILayout.Button("Update", GUILayout.Width(60)))
                {
                    UpdateAttachmentTransform(att);
                }

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    RemoveAttachment(i);
                    i--;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Offsets to Selected"))
            {
                if (_selectedAttachmentIndex >= 0 && _selectedAttachmentIndex < _activeAttachments.Count)
                {
                    var att = _activeAttachments[_selectedAttachmentIndex];
                    att.PositionOffset = _positionOffset;
                    att.RotationOffset = _rotationOffset;
                    att.Scale = _scaleOffset;
                    UpdateAttachmentTransform(att);
                }
            }

            if (GUILayout.Button("Remove All"))
            {
                RemoveAllAttachments();
            }

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawOutfitSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Outfit System", EditorLabelStyle());

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(50));
            _outfitName = GUILayout.TextField(_outfitName, GUILayout.Width(200));

            if (GUILayout.Button("Save Outfit"))
            {
                SaveOutfit(_outfitName);
            }

            if (GUILayout.Button("Refresh List"))
            {
                RefreshOutfitList();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label("Saved Outfits:", EditorLabelStyle());

            if (_savedOutfitNames.Count == 0)
            {
                GUILayout.Label("No saved outfits found", WarningLabelStyle());
            }
            else
            {
                foreach (var outfitName in _savedOutfitNames)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(outfitName, GUILayout.Width(250));

                    if (GUILayout.Button("Load", GUILayout.Width(60)))
                    {
                        LoadOutfit(outfitName);
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(60)))
                    {
                        DeleteOutfit(outfitName);
                    }

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
        }

        private void LoadAssetBundle(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Globals.Log("Please enter an AssetBundle path");
                return;
            }

            string fullPath = path;
            if (!Path.IsPathRooted(path))
            {
                fullPath = Path.Combine(Paths.PluginPath, Plugin.AssetRoot, "AssetBundles", path);
            }

            if (_loadedBundles.ContainsKey(fullPath))
            {
                Globals.Log($"Bundle already loaded: {fullPath}");
                return;
            }

            if (!File.Exists(fullPath))
            {
                Globals.Log($"AssetBundle not found: {fullPath}");
                return;
            }

            try
            {
                var bundle = AssetBundle.LoadFromFile(fullPath);
                _loadedBundles[fullPath] = bundle;
                Globals.Log($"Loaded AssetBundle: {fullPath}");

                string[] assetNames = bundle.GetAllAssetNames();
                Globals.Log($"Available assets ({assetNames.Length}):");
                foreach (var name in assetNames)
                {
                    Globals.Log($"  - {name}");
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Failed to load AssetBundle: {ex.Message}");
            }
        }

        private AssetBundle GetOrLoadBundle(string path)
        {
            string fullPath = path;
            if (!Path.IsPathRooted(path))
            {
                fullPath = Path.Combine(Paths.PluginPath, Plugin.AssetRoot, "AssetBundles", path);
            }

            if (_loadedBundles.TryGetValue(fullPath, out var bundle))
            {
                return bundle;
            }

            LoadAssetBundle(path);
            _loadedBundles.TryGetValue(fullPath, out bundle);
            return bundle;
        }

        private void SpawnAndAttach()
        {
            if (string.IsNullOrEmpty(_assetBundlePath))
            {
                Globals.Log("Please enter an AssetBundle path");
                return;
            }

            if (_selectedChild == null)
            {
                Globals.Log("No target transform selected");
                return;
            }

            if (string.IsNullOrEmpty(_assetName))
            {
                Globals.Log("Please enter an asset name");
                return;
            }

            var bundle = GetOrLoadBundle(_assetBundlePath);
            if (bundle == null)
            {
                Globals.Log("Failed to load bundle");
                return;
            }

            try
            {
                GameObject prefab = bundle.LoadAsset<GameObject>(_assetName);
                if (prefab == null)
                {
                    Globals.Log($"Asset '{_assetName}' not found in bundle");
                    return;
                }

                // Create attachment data
                var attachment = new Containers
                {
                    BonePath = GetRelativePath(_selectedRoot, _selectedChild),
                    BundlePath = _assetBundlePath,
                    AssetName = _assetName,
                    PositionOffset = _positionOffset,
                    RotationOffset = _rotationOffset,
                    Scale = _scaleOffset,
                    TargetBone = _selectedChild
                };

                // Instantiate and attach
                var instance = Instantiate(prefab);
                instance.name = $"Attachment_{prefab.name}_{_activeAttachments.Count}";
                attachment.Instance = instance;

                // Apply transform
                instance.transform.SetParent(_selectedChild);
                UpdateAttachmentTransform(attachment);

                _activeAttachments.Add(attachment);
                _selectedAttachmentIndex = _activeAttachments.Count - 1;

                Globals.Log($"Spawned '{prefab.name}' and attached to '{_selectedChild.name}'");
            }
            catch (Exception ex)
            {
                Globals.Log($"Failed to spawn asset: {ex.Message}");
            }
        }

        private void UpdateAttachmentTransform(Containers att)
        {
            if (att.Instance == null) return;

            att.Instance.transform.localPosition = att.PositionOffset;
            att.Instance.transform.localRotation = Quaternion.Euler(att.RotationOffset);
            att.Instance.transform.localScale = att.Scale;
        }

        private void RemoveAttachment(int index)
        {
            if (index < 0 || index >= _activeAttachments.Count) return;

            var att = _activeAttachments[index];
            if (att.Instance != null)
            {
                Destroy(att.Instance);
            }

            _activeAttachments.RemoveAt(index);

            if (_selectedAttachmentIndex >= _activeAttachments.Count)
            {
                _selectedAttachmentIndex = _activeAttachments.Count - 1;
            }
        }

        private void RemoveAllAttachments()
        {
            foreach (var att in _activeAttachments)
            {
                if (att.Instance != null)
                {
                    Destroy(att.Instance);
                }
            }

            _activeAttachments.Clear();
            _selectedAttachmentIndex = -1;
        }

        private string GetRelativePath(Transform root, Transform target)
        {
            StringBuilder sb = new StringBuilder();
            Transform current = target;

            while (current != null && current != root)
            {
                if (sb.Length > 0)
                    sb.Insert(0, "/");
                sb.Insert(0, current.name);
                current = current.parent;
            }

            return sb.ToString();
        }

        private Transform FindChildByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;

            Globals.Log($"FindChildByPath: Looking for '{path}' starting from '{root.name}'");

            string[] parts = path.Split('/');
            Transform current = root;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                current = FindDirectChild(current, part);
                if (current == null)
                {
                    // Try recursive search as fallback
                    current = FindDeepChild(root, part);
                    if (current == null)
                    {
                        Globals.Log($"FindChildByPath: Could not find '{part}' in hierarchy");
                        return null;
                    }
                }
            }

            Globals.Log($"FindChildByPath: Found '{current.name}'");
            return current;
        }

        private Transform FindDirectChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        /// <summary>
        /// Recursively search for a child with the given name anywhere in the hierarchy
        /// </summary>
        private Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                var result = FindDeepChild(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RefreshOutfitList()
        {
            _savedOutfitNames.Clear();

            if (Directory.Exists(OutfitsFolder))
            {
                var files = Directory.GetFiles(OutfitsFolder, "*.txt");
                foreach (var file in files)
                {
                    _savedOutfitNames.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }

        private void SaveOutfit(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Globals.Log("Please enter an outfit name");
                return;
            }

            if (_activeAttachments.Count == 0)
            {
                Globals.Log("No attachments to save!");
                return;
            }

            // Simple text format: one line per attachment
            // BonePath|BundlePath|AssetName|PosX,PosY,PosZ|RotX,RotY,RotZ|ScaleX,ScaleY,ScaleZ
            var lines = new List<string>();

            foreach (var att in _activeAttachments)
            {
                string posStr = $"{att.PositionOffset.x},{att.PositionOffset.y},{att.PositionOffset.z}";
                string rotStr = $"{att.RotationOffset.x},{att.RotationOffset.y},{att.RotationOffset.z}";
                string scaleStr = $"{att.Scale.x},{att.Scale.y},{att.Scale.z}";

                string line = $"{att.BonePath}|{att.BundlePath}|{att.AssetName}|{posStr}|{rotStr}|{scaleStr}";
                lines.Add(line);
                Globals.Log($"Saving attachment: {line}");
            }

            string filePath = Path.Combine(OutfitsFolder, $"{name}.txt");
            File.WriteAllLines(filePath, lines.ToArray());

            Globals.Log($"Saved outfit '{name}' with {lines.Count} attachments to {filePath}");
            RefreshOutfitList();
        }

        private void LoadOutfit(string name)
        {
            string filePath = Path.Combine(OutfitsFolder, $"{name}.txt");

            if (!File.Exists(filePath))
            {
                Globals.Log($"Outfit file not found: {filePath}");
                return;
            }

            if (_selectedRoot == null)
            {
                TrySelectPlayerWolf();
                if (_selectedRoot == null)
                {
                    Globals.Log("No target selected. Please select a wolf first.");
                    return;
                }
            }

            // Remove existing attachments
            RemoveAllAttachments();

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                Globals.Log($"Loading outfit '{name}' with {lines.Length} lines");

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 6)
                    {
                        Globals.Log($"Invalid line format: {line}");
                        continue;
                    }

                    string bonePath = parts[0];
                    string bundlePath = parts[1];
                    string assetName = parts[2];
                    Vector3 posOffset = ParseVector3(parts[3]);
                    Vector3 rotOffset = ParseVector3(parts[4]);
                    Vector3 scale = ParseVector3(parts[5]);

                    Globals.Log(
                        $"Loading attachment: Asset='{assetName}', Bundle='{bundlePath}', Bone='{bonePath}'");

                    // Find the target bone
                    Transform targetBone = FindChildByPath(_selectedRoot, bonePath);
                    if (targetBone == null)
                    {
                        Globals.Log($"Bone not found: {bonePath}");
                        continue;
                    }

                    // Load bundle
                    var bundle = GetOrLoadBundle(bundlePath);
                    if (bundle == null)
                    {
                        Globals.Log($"Failed to load bundle: {bundlePath}");
                        continue;
                    }

                    // Load and spawn prefab
                    GameObject prefab = bundle.LoadAsset<GameObject>(assetName);
                    if (prefab == null)
                    {
                        Globals.Log($"Asset not found: {assetName}");
                        continue;
                    }

                    var instance = Instantiate(prefab);
                    instance.name = $"Attachment_{prefab.name}_{_activeAttachments.Count}";
                    instance.transform.SetParent(targetBone);

                    var attachment = new Containers
                    {
                        BonePath = bonePath,
                        BundlePath = bundlePath,
                        AssetName = assetName,
                        PositionOffset = posOffset,
                        RotationOffset = rotOffset,
                        Scale = scale,
                        Instance = instance,
                        TargetBone = targetBone
                    };

                    UpdateAttachmentTransform(attachment);
                    _activeAttachments.Add(attachment);
                    Globals.Log($"Successfully loaded attachment: {assetName} -> {targetBone.name}");
                }

                Globals.Log($"Loaded outfit '{name}' with {_activeAttachments.Count} attachments");
            }
            catch (Exception ex)
            {
                Globals.Log($"Failed to load outfit: {ex.Message}");
            }
        }

        private Vector3 ParseVector3(string str)
        {
            try
            {
                string[] parts = str.Split(',');
                return new Vector3(
                    float.Parse(parts[0]),
                    float.Parse(parts[1]),
                    float.Parse(parts[2])
                );
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private void DeleteOutfit(string name)
        {
            string filePath = Path.Combine(OutfitsFolder, $"{name}.txt");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Globals.Log($"Deleted outfit: {name}");
                RefreshOutfitList();
            }
        }

        private void ExpandAllRecursive(Transform node)
        {
            _expandedNodes.Add(node);
            foreach (Transform child in node)
            {
                ExpandAllRecursive(child);
            }
        }

        private string GetFullPath(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            Transform current = t;

            while (current != null)
            {
                if (sb.Length > 0)
                    sb.Insert(0, "/");
                sb.Insert(0, current.name);
                current = current.parent;
            }

            return sb.ToString();
        }

        private void LogAllChildren(Transform parent, string indent)
        {
            Globals.Log($"{indent}{parent.name}");
            foreach (Transform child in parent)
            {
                LogAllChildren(child, indent + "  ");
            }
        }

        // GUI Styles
        private GUIStyle EditorLabelStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(0.8f, 0.9f, 1f);
            return style;
        }

        private GUIStyle InfoLabelStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = new Color(0.6f, 0.8f, 0.6f);
            return style;
        }

        private GUIStyle WarningLabelStyle()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = new Color(1f, 0.8f, 0.4f);
            style.fontStyle = FontStyle.Italic;
            return style;
        }

        private GUIStyle SelectedButtonStyle()
        {
            var style = new GUIStyle(GUI.skin.button);
            style.normal.textColor = Color.yellow;
            style.fontStyle = FontStyle.Bold;
            return style;
        }

        private void OnRenderObject()
        {
            if (!_showGizmos || !_showWindow) return;
            if (_selectedChild == null) return;

            // Create material if needed
            if (_gizmoMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                _gizmoMaterial = new Material(shader);
                _gizmoMaterial.hideFlags = HideFlags.HideAndDontSave;
                _gizmoMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _gizmoMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _gizmoMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _gizmoMaterial.SetInt("_ZWrite", 0);
                _gizmoMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            _gizmoMaterial.SetPass(0);

            GL.PushMatrix();

            Vector3 pos = _selectedChild.position;
            float axisLength = 0.2f;

            GL.Begin(GL.LINES);

            // X axis - Red (Right)
            GL.Color(Color.red);
            GL.Vertex(pos);
            GL.Vertex(pos + _selectedChild.right * axisLength);

            // Y axis - Green (Up)
            GL.Color(Color.green);
            GL.Vertex(pos);
            GL.Vertex(pos + _selectedChild.up * axisLength);

            // Z axis - Blue (Forward)
            GL.Color(Color.blue);
            GL.Vertex(pos);
            GL.Vertex(pos + _selectedChild.forward * axisLength);

            GL.End();

            GL.PopMatrix();
        }
    }
}
