using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using WolfQuestEp3;
using SharedCommons;

namespace Mapeditor
{
    [BepInPlugin("com.rw.mapeditor", "Mapeditor", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        // Configuration
        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<float> _moveSpeed;
        private ConfigEntry<float> _rotateSpeed;
        private ConfigEntry<float> _scaleSpeed;
        private ConfigEntry<float> _defaultObstacleRadius;
        private ConfigEntry<bool> _enableNavObstacles;

        // Window State
        private bool _showWindow = false;
        private Rect _windowRect;
        private bool _windowRectInitialized = false;

        // Scroll Positions
        private Vector2 _mainScrollPosition = Vector2.zero;
        private Vector2 _objectListScrollPosition = Vector2.zero;
        private Vector2 _sceneObjectsScrollPosition = Vector2.zero;
        private Vector2 _layoutListScrollPosition = Vector2.zero;

        // Tabs
        private int _currentTab = 0;
        private readonly string[] _tabNames = { "Spawn", "Objects", "Scene Objects", "Layouts", "Meat", "Props" };

        // Asset Management
        private string _bundlePath = "";
        private string _assetName = "";
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        private List<string> _availableAssets = new List<string>();
        private int _selectedAssetIndex = -1;

        // Spawning State
        private Vector3 _spawnPosition = Vector3.zero;
        private Vector3 _spawnRotation = Vector3.zero;
        private Vector3 _spawnScale = Vector3.one;
        private int _spawnLayer = 0;
        private float _spawnObstacleRadius = 2f;
        private List<Containers> _spawnedObjects = new List<Containers>();
        private int _selectedObjectIndex = -1;

        // Scene Objects / Selection / Deletion
        private List<DeletedSceneObject> _deletedSceneObjects = new List<DeletedSceneObject>();
        private GameObject _selectedSceneObject = null;
        private string _sceneSearchQuery = "";
        private List<GameObject> _sceneSearchResults = new List<GameObject>();
        private string _layoutName = "MyLayout";
        private List<string> _savedLayoutNames = new List<string>();
        private string _currentSceneAcronym = "";
        private bool _selectionModeActive = false;
        private bool _showGizmos = true;
        private Material _gizmoMaterial;

        // Free Cam
        private bool _freeCamEnabled = false;
        private Camera _mainCamera;
        private Transform _originalCameraParent;
        private Vector3 _originalCameraLocalPos;
        private Quaternion _originalCameraLocalRot;
        private float _freeCamMoveSpeed = 50f;
        private float _freeCamFastMultiplier = 3f;
        private float _freeCamLookSensitivity = 2f;
        private float _freeCamYaw = 0f;
        private float _freeCamPitch = 0f;

        // References
        private SceneAssetContainer _sceneAssets;

        public static Plugin Instance { get; private set; }

        // Properties
        private Containers SelectedObject =>
            (_selectedObjectIndex >= 0 && _selectedObjectIndex < _spawnedObjects.Count)
                ? _spawnedObjects[_selectedObjectIndex]
                : null;

        public KeyCode ToggleKey => _toggleKey.Value;
        public float MoveSpeed => _moveSpeed.Value;
        public float RotateSpeed => _rotateSpeed.Value;
        public float ScaleSpeed => _scaleSpeed.Value;
        public float DefaultObstacleRadius => _defaultObstacleRadius.Value;
        public bool EnableNavObstacles => _enableNavObstacles.Value;
        public static string AssetRoot => Path.Combine(Paths.PluginPath, "mapeditor_assets");
        public static string LayoutsFolder => Path.Combine(AssetRoot, "MapLayouts");
        public static string BundlesFolder => Path.Combine(AssetRoot, "MapBundles");

        private void Awake()
        {
            Instance = this;
            _toggleKey = Config.Bind("General", "ToggleKey", KeyCode.F7, "Key to toggle the Map Editor window");
            _moveSpeed = Config.Bind("Transform", "MoveSpeed", 0.1f,
                new ConfigDescription("Speed for moving objects", new AcceptableValueRange<float>(0.01f, 1f)));
            _rotateSpeed = Config.Bind("Transform", "RotateSpeed", 5f,
                new ConfigDescription("Speed for rotating objects (degrees)",
                    new AcceptableValueRange<float>(1f, 45f)));
            _scaleSpeed = Config.Bind("Transform", "ScaleSpeed", 0.05f,
                new ConfigDescription("Speed for scaling objects", new AcceptableValueRange<float>(0.01f, 0.5f)));
            _defaultObstacleRadius = Config.Bind("Navigation", "DefaultObstacleRadius", 2f,
                new ConfigDescription("Default radius for AI navigation obstacles (meters)",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            _enableNavObstacles = Config.Bind("Navigation", "EnableNavigationObstacles", true,
                "When enabled, spawned objects will be visible to AI pathfinding");

            EnsureFoldersExist();

            Harmony.CreateAndPatchAll(typeof(Plugin));
            Harmony.CreateAndPatchAll(typeof(Patch));
            Harmony.CreateAndPatchAll(typeof(Containers));
            Harmony.CreateAndPatchAll(typeof(Patch.CollectExtraPointObstacles_Patch));

            Globals.Log("[Mapeditor] Initialised");
        }

        private void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            UpdateCurrentScene();
            RefreshLayoutList();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UpdateCurrentScene();
            RefreshLayoutList();
        }

        private void UpdateCurrentScene()
        {
            SceneUtilities.SceneIdentity currentSceneIdentity = SceneUtilities.GetCurrentSceneIdentity();
            _currentSceneAcronym = SceneUtilities.GetSceneAcronym(currentSceneIdentity);
        }

        private void Update()
        {
            if (Globals.MenuIsOpen)
            {
                _showWindow = false;
                _selectionModeActive = false;
                if (_freeCamEnabled) DisableFreeCam();
            }
            else
            {
                if (Input.GetKeyDown(ToggleKey))
                {
                    _showWindow = !_showWindow;
                    if (!_showWindow)
                    {
                        _selectionModeActive = false;
                        if (_freeCamEnabled) DisableFreeCam();
                    }

                    InputControls.ForceAllowCursor = _showWindow;
                    InputControls.DisableCameraInput = _showWindow;
                    InputControls.DisableInput = _showWindow;
                }
            }

            if (_selectionModeActive && Input.GetMouseButtonDown(0) && !IsMouseOverWindow())
            {
                TrySelectObjectUnderMouse();
            }

            if (_freeCamEnabled && _mainCamera != null)
            {
                UpdateFreeCam();
            }
        }

        private void UpdateFreeCam()
        {
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            float speed = _freeCamMoveSpeed;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= _freeCamFastMultiplier;

            Vector3 moveVector = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) moveVector += _mainCamera.transform.forward;
            if (Input.GetKey(KeyCode.S)) moveVector -= _mainCamera.transform.forward;
            if (Input.GetKey(KeyCode.A)) moveVector -= _mainCamera.transform.right;
            if (Input.GetKey(KeyCode.D)) moveVector += _mainCamera.transform.right;
            if (Input.GetKey(KeyCode.E)) moveVector += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) moveVector -= Vector3.up;

            if (moveVector.sqrMagnitude > 0f)
            {
                _mainCamera.transform.position += moveVector.normalized * speed * unscaledDeltaTime;
            }

            if (Input.GetMouseButton(1))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                float mouseX = Input.GetAxis("Mouse X") * _freeCamLookSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * _freeCamLookSensitivity;
                _freeCamYaw += mouseX;
                _freeCamPitch -= mouseY;
                _freeCamPitch = Mathf.Clamp(_freeCamPitch, -89f, 89f);
                _mainCamera.transform.rotation = Quaternion.Euler(_freeCamPitch, _freeCamYaw, 0f);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void EnableFreeCam()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Globals.Log("No main camera found!");
                return;
            }

            _originalCameraParent = _mainCamera.transform.parent;
            _originalCameraLocalPos = _mainCamera.transform.localPosition;
            _originalCameraLocalRot = _mainCamera.transform.localRotation;
            _mainCamera.transform.SetParent(null);

            Vector3 eulerAngles = _mainCamera.transform.eulerAngles;
            _freeCamYaw = eulerAngles.y;
            _freeCamPitch = eulerAngles.x;

            if (_freeCamPitch > 180f) _freeCamPitch -= 360f;

            _freeCamEnabled = true;
            Globals.Log("Free-cam enabled");
        }

        private void DisableFreeCam()
        {
            if (_mainCamera != null && _originalCameraParent != null)
            {
                _mainCamera.transform.SetParent(_originalCameraParent);
                _mainCamera.transform.localPosition = _originalCameraLocalPos;
                _mainCamera.transform.localRotation = _originalCameraLocalRot;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _freeCamEnabled = false;
            Globals.Log("Free-cam disabled");
        }

        private bool IsMouseOverWindow()
        {
            return _windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        private void TrySelectObjectUnderMouse()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit raycastHit, 1000f))
            {
                GameObject hitObject = raycastHit.collider.gameObject;

                // Check if it's one of our spawned objects
                for (int i = 0; i < _spawnedObjects.Count; i++)
                {
                    if (_spawnedObjects[i].Instance == hitObject || (_spawnedObjects[i].Instance != null &&
                                                                     hitObject.transform.IsChildOf(_spawnedObjects[i]
                                                                         .Instance.transform)))
                    {
                        _selectedObjectIndex = i;
                        _currentTab = 1; // Objects tab
                        return;
                    }
                }

                // Otherwise select as scene object
                GameObject logicalRoot = FindLogicalRoot(hitObject);
                _selectedSceneObject = logicalRoot;
                _currentTab = 2; // Scene Objects tab
            }
        }

        private GameObject FindLogicalRoot(GameObject hitObj)
        {
            Transform current = hitObj.transform;
            Transform result = current;

            while (current.parent != null)
            {
                string parentName = current.parent.name.ToLowerInvariant();
                bool isContainer = parentName.Contains("environment") || parentName.Contains("terrain") ||
                                   parentName.Contains("world") || parentName.Contains("level") ||
                                   parentName.Contains("scene") || parentName == "map" ||
                                   parentName == "static" || parentName == "dynamic";

                if (isContainer) break;

                current = current.parent;
                result = current;

                if (GetHierarchyDepth(result) < 2) break;
            }

            return result.gameObject;
        }

        private int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }

            return depth;
        }

        private void OnGUI()
        {
            if (!_showWindow) return;

            if (!_windowRectInitialized)
            {
                float width = Mathf.Min(750f, Screen.width * 0.85f);
                float height = Mathf.Min(700f, Screen.height * 0.85f);
                float x = (Screen.width - width) * .5f;
                float y = (Screen.height - height) * .5f;
                _windowRect = new Rect(x, y, width, height);
                _windowRectInitialized = true;
            }

            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - _windowRect.height);

            GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);

            string title = "Map Editor - " + _currentSceneAcronym;
            if (_selectionModeActive) title += " [CLICK SELECT]";
            if (_freeCamEnabled) title += " [FREE-CAM]";

            _windowRect = GUI.Window(9998, _windowRect, DrawWindow, title);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            _showGizmos = GUILayout.Toggle(_showGizmos, "Gizmos", GUILayout.Width(70f));
            _selectionModeActive = GUILayout.Toggle(_selectionModeActive, "Click Select", GUILayout.Width(90f));

            bool newFreeCamState = GUILayout.Toggle(_freeCamEnabled, "Free-Cam", GUILayout.Width(80f));
            if (newFreeCamState != _freeCamEnabled)
            {
                if (newFreeCamState) EnableFreeCam();
                else DisableFreeCam();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Scene: " + _currentSceneAcronym, InfoStyle());
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            _currentTab = GUILayout.Toolbar(_currentTab, _tabNames);
            GUILayout.Space(5f);

            _mainScrollPosition = GUILayout.BeginScrollView(_mainScrollPosition);
            switch (_currentTab)
            {
                case 0: DrawSpawnTab(); break;
                case 1: DrawObjectsTab(); break;
                case 2: DrawSceneObjectsTab(); break;
                case 3: DrawLayoutsTab(); break;
                case 4: DrawMeatTab(); break;
                case 5: DrawPropsTab(); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void EnsureFoldersExist()
        {
            if (!Directory.Exists(AssetRoot)) Directory.CreateDirectory(AssetRoot);
            if (!Directory.Exists(LayoutsFolder)) Directory.CreateDirectory(LayoutsFolder);
            if (!Directory.Exists(BundlesFolder)) Directory.CreateDirectory(BundlesFolder);
        }

        private void DrawSpawnTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Asset Bundle", HeaderStyle());

            GUILayout.BeginHorizontal();
            GUILayout.Label("Bundle:", GUILayout.Width(60f));
            _bundlePath = GUILayout.TextField(_bundlePath);
            if (GUILayout.Button("Browse", GUILayout.Width(60f))) ListAvailableBundles();
            if (GUILayout.Button("Load", GUILayout.Width(50f))) LoadAssetBundle(_bundlePath);
            GUILayout.EndHorizontal();

            if (_availableAssets.Count > 0)
            {
                GUILayout.Label($"Available Assets ({_availableAssets.Count}):", SubHeaderStyle());
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Mathf.Min(_availableAssets.Count, 10); i++)
                {
                    string shortName = Path.GetFileNameWithoutExtension(_availableAssets[i]);
                    if (GUILayout.Toggle(i == _selectedAssetIndex, shortName, "Button"))
                    {
                        _selectedAssetIndex = i;
                        _assetName = _availableAssets[i];
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Asset:", GUILayout.Width(60f));
            _assetName = GUILayout.TextField(_assetName);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Spawn Settings", HeaderStyle());

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position:", GUILayout.Width(70f));
            if (GUILayout.Button("Player Pos", GUILayout.Width(80f)))
            {
                PlayerAnimalControls pac = FindObjectOfType<PlayerAnimalControls>();
                if (pac?.ControlledAnimal != null)
                {
                    _spawnPosition = pac.ControlledAnimal.transform.position + Vector3.forward * 3f;
                }
            }

            GUILayout.EndHorizontal();

            _spawnPosition = DrawVector3Field(_spawnPosition, "Pos");
            _spawnRotation = DrawVector3Field(_spawnRotation, "Rot");
            _spawnScale = DrawVector3Field(_spawnScale, "Scale");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Layer:", GUILayout.Width(70f));
            string layerStr = GUILayout.TextField(_spawnLayer.ToString(), GUILayout.Width(50f));
            if (int.TryParse(layerStr, out int layerVal)) _spawnLayer = Mathf.Clamp(layerVal, 0, 31);
            GUILayout.Label(LayerMask.LayerToName(_spawnLayer), GUILayout.Width(150f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("AI Obstacle:", GUILayout.Width(70f));
            _spawnObstacleRadius = GUILayout.HorizontalSlider(_spawnObstacleRadius, 0f, 10f, GUILayout.Width(100f));
            string radiusStr = GUILayout.TextField(_spawnObstacleRadius.ToString("F1"), GUILayout.Width(40f));
            if (float.TryParse(radiusStr, out float radiusVal)) _spawnObstacleRadius = Mathf.Clamp(radiusVal, 0f, 10f);
            GUILayout.Label((_spawnObstacleRadius > 0f) ? $"({_spawnObstacleRadius:F1}m radius)" : "(disabled)",
                GUILayout.Width(100f));
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.2f);
            if (GUILayout.Button("SPAWN OBJECT", GUILayout.Height(40f))) SpawnObject();
            GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);
            GUILayout.EndVertical();
        }

        private void ListAvailableBundles()
        {
            if (Directory.Exists(BundlesFolder))
            {
                string[] files = Directory.GetFiles(BundlesFolder);
                Globals.Log("Available bundles in " + BundlesFolder + ":");
                foreach (string text in files)
                {
                    if (!text.EndsWith(".manifest") && !text.EndsWith(".meta"))
                    {
                        Globals.Log("  - " + Path.GetFileName(text));
                    }
                }
            }
        }

        private void LoadAssetBundle(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Globals.Log("Please enter an AssetBundle path");
                return;
            }

            string fullPath = path;
            if (!Path.IsPathRooted(path)) fullPath = Path.Combine(BundlesFolder, path);

            if (_loadedBundles.ContainsKey(fullPath))
            {
                Globals.Log("Bundle already loaded: " + fullPath);
                UpdateAssetList(fullPath);
                return;
            }

            if (!File.Exists(fullPath))
            {
                Globals.Log("AssetBundle not found: " + fullPath);
                return;
            }

            try
            {
                AssetBundle assetBundle = AssetBundle.LoadFromFile(fullPath);
                if (assetBundle == null)
                {
                    Globals.Log("Failed to load bundle: " + fullPath);
                }
                else
                {
                    _loadedBundles[fullPath] = assetBundle;
                    Globals.Log("Loaded AssetBundle: " + fullPath);
                    UpdateAssetList(fullPath);
                }
            }
            catch (Exception ex)
            {
                Globals.Log("Error loading bundle: " + ex.Message);
            }
        }

        private void UpdateAssetList(string bundlePath)
        {
            _availableAssets.Clear();
            _selectedAssetIndex = -1;
            if (_loadedBundles.TryGetValue(bundlePath, out AssetBundle assetBundle))
            {
                _availableAssets.AddRange(assetBundle.GetAllAssetNames());
                Globals.Log($"Found {_availableAssets.Count} assets in bundle");
            }
        }

        private void SpawnObject()
        {
            if (string.IsNullOrEmpty(_bundlePath))
            {
                Globals.Log("Please enter a bundle path");
                return;
            }

            if (string.IsNullOrEmpty(_assetName))
            {
                Globals.Log("Please enter an asset name");
                return;
            }

            string key = _bundlePath;
            if (!Path.IsPathRooted(_bundlePath)) key = Path.Combine(BundlesFolder, _bundlePath);

            if (!_loadedBundles.TryGetValue(key, out AssetBundle assetBundle))
            {
                LoadAssetBundle(_bundlePath);
                _loadedBundles.TryGetValue(key, out assetBundle);
            }

            if (assetBundle == null)
            {
                Globals.Log("Failed to load bundle");
                return;
            }

            try
            {
                GameObject original = assetBundle.LoadAsset<GameObject>(_assetName);
                if (original == null)
                {
                    Globals.Log("Asset '" + _assetName + "' not found in bundle");
                    return;
                }

                GameObject instance = Instantiate(original);
                instance.name = $"MapObj_{original.name}_{_spawnedObjects.Count}";
                instance.transform.position = _spawnPosition;
                instance.transform.rotation = Quaternion.Euler(_spawnRotation);
                instance.transform.localScale = _spawnScale;
                SetLayerRecursively(instance, _spawnLayer);

                Containers container = Containers.CreateNew();
                container.BundlePath = _bundlePath;
                container.AssetName = _assetName;
                container.Position = _spawnPosition;
                container.Rotation = _spawnRotation;
                container.Scale = _spawnScale;
                container.Layer = _spawnLayer;
                container.ObstacleRadius = _spawnObstacleRadius;
                container.Instance = instance;

                _spawnedObjects.Add(container);
                _selectedObjectIndex = _spawnedObjects.Count - 1;

                if (Instance.EnableNavObstacles && _spawnObstacleRadius > 0f)
                {
                    Patch.RegisterObstacle(instance, _spawnObstacleRadius);
                }

                Globals.Log($"Spawned '{original.name}' at {_spawnPosition}");
            }
            catch (Exception ex)
            {
                Globals.Log("Failed to spawn: " + ex.Message);
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void DrawObjectsTab()
        {
            GUILayout.Label($"Spawned Objects ({_spawnedObjects.Count})", HeaderStyle());
            _objectListScrollPosition = GUILayout.BeginScrollView(_objectListScrollPosition, GUILayout.Height(200f));

            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                Containers container = _spawnedObjects[i];
                GUILayout.BeginHorizontal();

                if (GUILayout.Toggle(i == _selectedObjectIndex, "", GUILayout.Width(20f)))
                {
                    _selectedObjectIndex = i;
                }

                GUILayout.Label((container.Instance != null) ? "●" : "○", GUILayout.Width(15f));
                GUILayout.Label($"{container.AssetName} [{container.UniqueId}]");

                if (GUILayout.Button("Focus", GUILayout.Width(50f))) FocusOnObject(container);

                GUI.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
                if (GUILayout.Button("X", GUILayout.Width(25f)))
                {
                    DeleteSpawnedObject(i);
                    i--;
                }

                GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (SelectedObject != null)
            {
                GUILayout.Space(10f);
                DrawSelectedObjectEditor();
            }
        }

        private void DrawSelectedObjectEditor()
        {
            Containers selected = SelectedObject;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Editing: " + selected.AssetName, HeaderStyle());

            if (selected.Instance != null)
            {
                selected.Position = selected.Instance.transform.position;
                selected.Rotation = selected.Instance.transform.eulerAngles;
                selected.Scale = selected.Instance.transform.localScale;

                GUILayout.Label("Position:", SubHeaderStyle());
                GUILayout.BeginHorizontal();
                float spd = Instance.MoveSpeed;
                if (GUILayout.Button("-X")) selected.Position += Vector3.left * spd;
                if (GUILayout.Button("+X")) selected.Position += Vector3.right * spd;
                if (GUILayout.Button("-Y")) selected.Position += Vector3.down * spd;
                if (GUILayout.Button("+Y")) selected.Position += Vector3.up * spd;
                if (GUILayout.Button("-Z")) selected.Position += Vector3.back * spd;
                if (GUILayout.Button("+Z")) selected.Position += Vector3.forward * spd;
                GUILayout.EndHorizontal();
                selected.Position = DrawVector3Field(selected.Position, "Pos");

                GUILayout.Label("Rotation:", SubHeaderStyle());
                GUILayout.BeginHorizontal();
                float rot = Instance.RotateSpeed;
                if (GUILayout.Button("-X")) selected.Rotation += new Vector3(-rot, 0f, 0f);
                if (GUILayout.Button("+X")) selected.Rotation += new Vector3(rot, 0f, 0f);
                if (GUILayout.Button("-Y")) selected.Rotation += new Vector3(0f, -rot, 0f);
                if (GUILayout.Button("+Y")) selected.Rotation += new Vector3(0f, rot, 0f);
                if (GUILayout.Button("-Z")) selected.Rotation += new Vector3(0f, 0f, -rot);
                if (GUILayout.Button("+Z")) selected.Rotation += new Vector3(0f, 0f, rot);
                GUILayout.EndHorizontal();
                selected.Rotation = DrawVector3Field(selected.Rotation, "Rot");

                GUILayout.Label("Scale:", SubHeaderStyle());
                GUILayout.BeginHorizontal();
                float scl = Instance.ScaleSpeed;
                if (GUILayout.Button("-All")) selected.Scale -= Vector3.one * scl;
                if (GUILayout.Button("+All")) selected.Scale += Vector3.one * scl;
                if (GUILayout.Button("Reset")) selected.Scale = Vector3.one;
                GUILayout.EndHorizontal();
                selected.Scale = DrawVector3Field(selected.Scale, "Scale");

                GUILayout.BeginHorizontal();
                GUILayout.Label("Layer:", GUILayout.Width(70f));
                string lStr = GUILayout.TextField(selected.Layer.ToString(), GUILayout.Width(50f));
                if (int.TryParse(lStr, out int lVal)) selected.Layer = Mathf.Clamp(lVal, 0, 31);
                GUILayout.Label(LayerMask.LayerToName(selected.Layer));
                GUILayout.EndHorizontal();

                // Apply back to transform
                selected.Instance.transform.position = selected.Position;
                selected.Instance.transform.rotation = Quaternion.Euler(selected.Rotation);
                selected.Instance.transform.localScale = selected.Scale;
                SetLayerRecursively(selected.Instance, selected.Layer);
            }
            else
            {
                GUILayout.Label("Object instance is null. Try respawning.", WarningStyle());
            }

            GUILayout.EndVertical();
        }

        private void FocusOnObject(Containers obj)
        {
            if (obj.Instance != null)
            {
                PlayerAnimalControls pac = FindObjectOfType<PlayerAnimalControls>();
                if (pac?.ControlledAnimal != null)
                {
                    pac.ControlledAnimal.transform.position =
                        obj.Instance.transform.position - Vector3.forward * 5f + Vector3.up * 2f;
                }
            }
        }

        private void DeleteSpawnedObject(int index)
        {
            if (index >= 0 && index < _spawnedObjects.Count)
            {
                Containers container = _spawnedObjects[index];
                if (container.Instance != null)
                {
                    Patch.UnregisterObstacle(container.Instance);
                    Destroy(container.Instance);
                }

                _spawnedObjects.RemoveAt(index);
                if (_selectedObjectIndex >= _spawnedObjects.Count)
                {
                    _selectedObjectIndex = _spawnedObjects.Count - 1;
                }

                Globals.Log("Deleted object: " + container.AssetName);
            }
        }

        private void DrawSceneObjectsTab()
        {
            GUILayout.Label("Scene Object Selection", HeaderStyle());
            GUILayout.Label("Click on objects in the scene (with Click Select enabled) or search below.", InfoStyle());

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(60f));
            string query = GUILayout.TextField(_sceneSearchQuery);
            if (query != _sceneSearchQuery)
            {
                _sceneSearchQuery = query;
                if (_sceneSearchQuery.Length >= 3) SearchSceneObjects();
            }

            if (GUILayout.Button("Search", GUILayout.Width(60f))) SearchSceneObjects();
            if (GUILayout.Button("Clear", GUILayout.Width(50f)))
            {
                _sceneSearchQuery = "";
                _sceneSearchResults.Clear();
            }

            GUILayout.EndHorizontal();

            if (_sceneSearchResults.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Results ({_sceneSearchResults.Count}):", SubHeaderStyle());
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Hide All Results", GUILayout.Width(120f))) HideAllSearchResults();
                GUILayout.EndHorizontal();

                _sceneObjectsScrollPosition =
                    GUILayout.BeginScrollView(_sceneObjectsScrollPosition, GUILayout.Height(150f));
                foreach (GameObject go in _sceneSearchResults)
                {
                    if (go != null)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Toggle(go == _selectedSceneObject, "", GUILayout.Width(20f)))
                        {
                            _selectedSceneObject = go;
                        }

                        GUILayout.Label($"{go.name} (ID: {go.GetInstanceID()})");
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.EndScrollView();
            }

            if (_selectedSceneObject != null)
            {
                GUILayout.Space(10f);
                DrawSelectedSceneObject();
            }

            GUILayout.Space(10f);
            DrawDeletedObjectsList();
        }

        private void SearchSceneObjects()
        {
            _sceneSearchResults.Clear();
            if (!string.IsNullOrEmpty(_sceneSearchQuery))
            {
                string lowerQuery = _sceneSearchQuery.ToLowerInvariant();
                GameObject[] allObjects = FindObjectsOfType<GameObject>();

                foreach (GameObject go in allObjects)
                {
                    if (go.name.ToLowerInvariant().Contains(lowerQuery))
                    {
                        if (!IsObjectAlreadyHidden(go))
                        {
                            _sceneSearchResults.Add(go);
                            if (_sceneSearchResults.Count >= 50) break;
                        }
                    }
                }

                Globals.Log($"Found {_sceneSearchResults.Count} matching objects");
            }
        }

        private bool IsObjectAlreadyHidden(GameObject obj)
        {
            foreach (DeletedSceneObject dso in _deletedSceneObjects)
            {
                if (dso.HiddenObject == obj) return true;
            }

            return false;
        }

        private void HideAllSearchResults()
        {
            int count = 0;
            foreach (GameObject go in _sceneSearchResults)
            {
                if (go != null && !IsObjectAlreadyHidden(go))
                {
                    HideSceneObject(go);
                    count++;
                }
            }

            _sceneSearchResults.Clear();
            _selectedSceneObject = null;
            Globals.Log($"Hidden {count} scene objects");
        }

        private void DrawSelectedSceneObject()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Selected: " + _selectedSceneObject.name, HeaderStyle());
            GUILayout.Label($"Instance ID: {_selectedSceneObject.GetInstanceID()}", InfoStyle());
            GUILayout.Label("Path: " + GetHierarchyPath(_selectedSceneObject.transform), InfoStyle());
            GUILayout.Label($"Position: {_selectedSceneObject.transform.position}", InfoStyle());
            GUILayout.Label(
                $"Layer: {_selectedSceneObject.layer} ({LayerMask.LayerToName(_selectedSceneObject.layer)})",
                InfoStyle());

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide (Delete)")) HideSceneObject(_selectedSceneObject);
            if (GUILayout.Button("Teleport To")) TeleportToObject(_selectedSceneObject);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void HideSceneObject(GameObject obj)
        {
            DeletedSceneObject dso = new DeletedSceneObject
            {
                OriginalInstanceId = obj.GetInstanceID(),
                ObjectName = obj.name,
                HierarchyPath = GetHierarchyPath(obj.transform),
                Position = obj.transform.position,
                IsApplied = true,
                HiddenObject = obj
            };
            obj.SetActive(false);
            _deletedSceneObjects.Add(dso);
            _selectedSceneObject = null;
            Globals.Log($"Hidden scene object: {obj.name} (ID: {dso.OriginalInstanceId})");
        }

        private void TeleportToObject(GameObject obj)
        {
            PlayerAnimalControls pac = FindObjectOfType<PlayerAnimalControls>();
            if (pac?.ControlledAnimal != null)
            {
                pac.ControlledAnimal.transform.position = obj.transform.position + Vector3.up * 2f;
            }
        }

        private void DrawDeletedObjectsList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Deleted Scene Objects ({_deletedSceneObjects.Count})", HeaderStyle());

            for (int i = 0; i < _deletedSceneObjects.Count; i++)
            {
                DeletedSceneObject dso = _deletedSceneObjects[i];
                GUILayout.BeginHorizontal();
                string status = dso.IsApplied ? "Hidden" : "Pending";
                GUILayout.Label($"[{status}] {dso.ObjectName}");
                if (GUILayout.Button("Restore", GUILayout.Width(60f)))
                {
                    RestoreDeletedObject(i);
                    i--;
                }

                GUILayout.EndHorizontal();
            }

            if (_deletedSceneObjects.Count > 0)
            {
                if (GUILayout.Button("Restore All")) RestoreAllDeletedObjects();
            }

            GUILayout.EndVertical();
        }

        private void RestoreDeletedObject(int index)
        {
            if (index >= 0 && index < _deletedSceneObjects.Count)
            {
                DeletedSceneObject dso = _deletedSceneObjects[index];
                if (dso.HiddenObject != null)
                {
                    dso.HiddenObject.SetActive(true);
                    Globals.Log("Restored scene object: " + dso.ObjectName);
                }

                _deletedSceneObjects.RemoveAt(index);
            }
        }

        private void RestoreAllDeletedObjects()
        {
            foreach (DeletedSceneObject dso in _deletedSceneObjects)
            {
                if (dso.HiddenObject != null)
                {
                    dso.HiddenObject.SetActive(true);
                }
            }

            _deletedSceneObjects.Clear();
            Globals.Log("Restored all deleted scene objects");
        }

        private string GetHierarchyPath(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            Transform current = t;
            while (current != null)
            {
                if (sb.Length > 0) sb.Insert(0, "/");
                sb.Insert(0, current.name);
                current = current.parent;
            }

            return sb.ToString();
        }

        private void DrawLayoutsTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Save Current Layout", HeaderStyle());
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(50f));
            _layoutName = GUILayout.TextField(_layoutName, GUILayout.Width(200f));

            GUI.backgroundColor = new Color(0.2f, 0.5f, 0.7f);
            if (GUILayout.Button("Save Layout")) SaveLayout(_layoutName);
            GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);

            if (GUILayout.Button("Refresh", GUILayout.Width(60f))) RefreshLayoutList();
            GUILayout.EndHorizontal();
            GUILayout.Label("Will save as: " + _layoutName + GetCurrentFileExtension(), InfoStyle());
            GUILayout.EndVertical();

            GUILayout.Space(10f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Saved Layouts for " + _currentSceneAcronym, HeaderStyle());
            if (_savedLayoutNames.Count == 0)
            {
                GUILayout.Label("No saved layouts found for this scene.", WarningStyle());
            }
            else
            {
                _layoutListScrollPosition =
                    GUILayout.BeginScrollView(_layoutListScrollPosition, GUILayout.Height(200f));
                foreach (string name in _savedLayoutNames)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(name);
                    if (GUILayout.Button("Load", GUILayout.Width(60f))) LoadLayout(name);

                    GUI.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
                    if (GUILayout.Button("Delete", GUILayout.Width(60f))) DeleteLayout(name);
                    GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            GUILayout.Space(10f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Current State", HeaderStyle());
            GUILayout.Label($"Spawned Objects: {_spawnedObjects.Count}", InfoStyle());
            GUILayout.Label($"Deleted Scene Objects: {_deletedSceneObjects.Count}", InfoStyle());
            GUILayout.Label($"Loaded Bundles: {_loadedBundles.Count}", InfoStyle());
            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 0.2f, 0.2f);
            if (GUILayout.Button("Clear All Spawned")) ClearAllSpawnedObjects();
            if (GUILayout.Button("Restore All Deleted")) RestoreAllDeletedObjects();
            GUI.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 0.98f);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private string GetCurrentFileExtension()
        {
            return "." + _currentSceneAcronym.ToLowerInvariant() + ".mapl";
        }

        private void RefreshLayoutList()
        {
            _savedLayoutNames.Clear();
            if (Directory.Exists(LayoutsFolder))
            {
                string ext = GetCurrentFileExtension();
                string[] files = Directory.GetFiles(LayoutsFolder, "*" + ext);
                foreach (string path in files)
                {
                    string fileName = Path.GetFileName(path);
                    fileName = fileName.Substring(0, fileName.Length - ext.Length);
                    _savedLayoutNames.Add(fileName);
                }
            }

            Globals.Log($"Found {_savedLayoutNames.Count} layouts for {_currentSceneAcronym}");
        }

        private void SaveLayout(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Globals.Log("Please enter a layout name");
                return;
            }

            List<string> lines = new List<string>();
            string timestamp = DateTime.UtcNow.ToString("o");
            lines.Add($"#HEADER|{_currentSceneAcronym}|{timestamp}|{timestamp}");

            foreach (Containers obj in _spawnedObjects)
            {
                string pos =
                    $"{obj.Position.x.ToString(CultureInfo.InvariantCulture)},{obj.Position.y.ToString(CultureInfo.InvariantCulture)},{obj.Position.z.ToString(CultureInfo.InvariantCulture)}";
                string rot =
                    $"{obj.Rotation.x.ToString(CultureInfo.InvariantCulture)},{obj.Rotation.y.ToString(CultureInfo.InvariantCulture)},{obj.Rotation.z.ToString(CultureInfo.InvariantCulture)}";
                string scl =
                    $"{obj.Scale.x.ToString(CultureInfo.InvariantCulture)},{obj.Scale.y.ToString(CultureInfo.InvariantCulture)},{obj.Scale.z.ToString(CultureInfo.InvariantCulture)}";
                lines.Add(
                    $"SPAWN|{obj.UniqueId}|{obj.BundlePath}|{obj.AssetName}|{pos}|{rot}|{scl}|{obj.Layer}|{obj.ObstacleRadius}");
                Globals.Log("Saving spawned object: " + obj.AssetName);
            }

            foreach (DeletedSceneObject obj in _deletedSceneObjects)
            {
                string pos =
                    $"{obj.Position.x.ToString(CultureInfo.InvariantCulture)},{obj.Position.y.ToString(CultureInfo.InvariantCulture)},{obj.Position.z.ToString(CultureInfo.InvariantCulture)}";
                string safeName = obj.ObjectName.Replace("|", "_");
                string safePath = obj.HierarchyPath.Replace("|", "_");
                lines.Add($"DELETE|{safeName}|{safePath}|{pos}");
                Globals.Log("Saving deleted object: " + obj.ObjectName);
            }

            string path = Path.Combine(LayoutsFolder, name + GetCurrentFileExtension());
            try
            {
                File.WriteAllLines(path, lines.ToArray());
                Globals.Log(
                    $"Saved layout '{name}' with {_spawnedObjects.Count} spawned and {_deletedSceneObjects.Count} deleted objects");
                RefreshLayoutList();
            }
            catch (Exception ex)
            {
                Globals.Log("Failed to save layout: " + ex.Message);
            }
        }

        private void LoadLayout(string name)
        {
            string path = Path.Combine(LayoutsFolder, name + GetCurrentFileExtension());
            if (!File.Exists(path))
            {
                Globals.Log("Layout file not found: " + path);
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                Globals.Log($"Loading layout '{name}' with {lines.Length} lines");

                ClearAllSpawnedObjects();
                RestoreAllDeletedObjects();

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 2) continue;

                    string type = parts[0];

                    if (type == "#HEADER")
                    {
                        if (parts.Length >= 2)
                        {
                            string sceneName = parts[1];
                            if (sceneName != _currentSceneAcronym)
                            {
                                Globals.Log(
                                    $"Layout is for {sceneName}, current scene is {_currentSceneAcronym}. Aborting.");
                                return;
                            }
                        }
                    }
                    else if (type == "SPAWN" && parts.Length >= 8)
                    {
                        int.TryParse(parts[7], out int layer);
                        float radius = (parts.Length >= 9 && float.TryParse(parts[8], out float r)) ? r : 2f;

                        Containers container = new Containers
                        {
                            UniqueId = parts[1],
                            BundlePath = parts[2],
                            AssetName = parts[3],
                            Position = ParseVector3(parts[4]),
                            Rotation = ParseVector3(parts[5]),
                            Scale = ParseVector3(parts[6]),
                            Layer = layer,
                            ObstacleRadius = radius
                        };
                        Globals.Log("Loading spawned object: " + container.AssetName);
                        RespawnObject(container);
                    }
                    else if (type == "DELETE" && parts.Length >= 4)
                    {
                        DeletedSceneObject dso = new DeletedSceneObject
                        {
                            ObjectName = parts[1],
                            HierarchyPath = parts[2],
                            Position = ParseVector3(parts[3])
                        };
                        Globals.Log("Loading deleted object: " + dso.ObjectName);
                        TryHideSceneObjectByPath(dso);
                    }
                }

                Globals.Log(
                    $"Loaded layout '{name}' with {_spawnedObjects.Count} objects and {_deletedSceneObjects.Count} deletions");
            }
            catch (Exception ex)
            {
                Globals.Log("Failed to load layout: " + ex.Message);
            }
        }

        private Vector3 ParseVector3(string str)
        {
            try
            {
                string[] parts = str.Split(',');
                if (parts.Length >= 3)
                {
                    return new Vector3(
                        float.Parse(parts[0], CultureInfo.InvariantCulture),
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture));
                }
            }
            catch
            {
                Globals.Log("Failed to parse Vector3: " + str);
            }

            return Vector3.zero;
        }

        private void RespawnObject(Containers data)
        {
            string key = data.BundlePath;
            if (!Path.IsPathRooted(data.BundlePath)) key = Path.Combine(BundlesFolder, data.BundlePath);

            if (!_loadedBundles.TryGetValue(key, out AssetBundle assetBundle))
            {
                LoadAssetBundle(data.BundlePath);
                _loadedBundles.TryGetValue(key, out assetBundle);
            }

            if (assetBundle == null)
            {
                Globals.Log("Could not load bundle for: " + data.AssetName);
                data.Instance = null;
                _spawnedObjects.Add(data);
                return;
            }

            try
            {
                GameObject original = assetBundle.LoadAsset<GameObject>(data.AssetName);
                if (original == null)
                {
                    Globals.Log("Asset not found: " + data.AssetName);
                    data.Instance = null;
                    _spawnedObjects.Add(data);
                }
                else
                {
                    GameObject instance = Instantiate(original);
                    instance.name = $"MapObj_{original.name}_{_spawnedObjects.Count}";
                    instance.transform.position = data.Position;
                    instance.transform.rotation = Quaternion.Euler(data.Rotation);
                    instance.transform.localScale = data.Scale;
                    SetLayerRecursively(instance, data.Layer);

                    data.Instance = instance;
                    _spawnedObjects.Add(data);

                    if (Instance.EnableNavObstacles && data.ObstacleRadius > 0f)
                    {
                        Patch.RegisterObstacle(instance, data.ObstacleRadius);
                    }
                }
            }
            catch (Exception ex)
            {
                Globals.Log($"Failed to respawn {data.AssetName}: {ex.Message}");
            }
        }

        private void TryHideSceneObjectByPath(DeletedSceneObject deleted)
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject go in allObjects)
            {
                if (go.name == deleted.ObjectName && Vector3.Distance(go.transform.position, deleted.Position) < 0.1f)
                {
                    go.SetActive(false);
                    deleted.IsApplied = true;
                    deleted.HiddenObject = go;
                    _deletedSceneObjects.Add(deleted);
                    Globals.Log("Re-hidden scene object: " + deleted.ObjectName);
                    return;
                }
            }

            Globals.Log("Could not find scene object to hide: " + deleted.ObjectName);
            deleted.IsApplied = false;
            _deletedSceneObjects.Add(deleted);
        }

        private void DeleteLayout(string name)
        {
            string path = Path.Combine(LayoutsFolder, name + GetCurrentFileExtension());
            if (File.Exists(path))
            {
                File.Delete(path);
                Globals.Log("Deleted layout: " + name);
                RefreshLayoutList();
            }
        }

        private void ClearAllSpawnedObjects()
        {
            foreach (Containers container in _spawnedObjects)
            {
                if (container.Instance != null)
                {
                    Patch.UnregisterObstacle(container.Instance);
                    Destroy(container.Instance);
                }
            }

            _spawnedObjects.Clear();
            _selectedObjectIndex = -1;
            Globals.Log("Cleared all spawned objects");
        }

        private void DrawMeatTab()
        {
            if (!_sceneAssets) _sceneAssets = FindObjectOfType<SceneAssetContainer>();

            if (_sceneAssets != null && _sceneAssets.meatPrefabs != null)
            {
                DrawPrefabList(_sceneAssets.meatPrefabs, "Meat & Carcasses", SpawnMeat);
            }
            else
            {
                GUILayout.Label("No meat prefabs found.", WarningStyle());
            }
        }

        private void SpawnMeat(MeatObject prefab)
        {
            if (Globals.meatManager && Globals.animalManager && Globals.animalManager.LocalPlayer)
            {
                Vector3 pos = Globals.animalManager.LocalPlayer.Position;
                Quaternion rot = Quaternion.identity;

                if (prefab is Carcass carcass)
                {
                    Globals.meatManager.SpawnCarcass(carcass, pos, rot, ushort.MaxValue, 1f, null, true, false);
                }
                else if (prefab is MeatChunk chunk)
                {
                    Globals.meatManager.SpawnMeatChunk(chunk, pos, rot, ushort.MaxValue, float.PositiveInfinity, null);
                }

                Globals.Log("Spawned meat: " + prefab.name);
            }
        }

        private void DrawPropsTab()
        {
            if (!_sceneAssets) _sceneAssets = FindObjectOfType<SceneAssetContainer>();

            if (_sceneAssets != null)
            {
                if (_sceneAssets.toyPrefabs?.Count > 0)
                    DrawPrefabList(_sceneAssets.toyPrefabs, "Toys", SpawnToy);

                if (_sceneAssets.wolfTrapPrefabs?.Count > 0)
                    DrawPrefabList(_sceneAssets.wolfTrapPrefabs, "Wolf Traps", SpawnTrap);

                if (_sceneAssets.scentTrackPrefabs?.Count > 0)
                    DrawPrefabList(_sceneAssets.scentTrackPrefabs, "Scent Tracks", SpawnScentTrack);

                if (_sceneAssets.vehiclePrefabs?.Count > 0)
                    DrawPrefabList(_sceneAssets.vehiclePrefabs, "Vehicles (Experimental)", SpawnVehicle);
            }
            else
            {
                GUILayout.Label("No scene assets found.", WarningStyle());
            }
        }

        private void SpawnToy(Toy prefab)
        {
            if (Globals.instantiationManager && Globals.animalManager && Globals.animalManager.LocalPlayer)
            {
                Toy toy = Globals.instantiationManager.InstantiateWqFromPool<Toy, ToyInitializer>(prefab,
                    i => i.Initialize(true));
                if (toy != null)
                {
                    toy.transform.position = Globals.animalManager.LocalPlayer.Position + Vector3.up * 1f;
                    Globals.Log("Spawned toy: " + prefab.name);
                }
            }
        }

        private void SpawnTrap(WolfTrap prefab)
        {
            if (Globals.instantiationManager && Globals.animalManager && Globals.animalManager.LocalPlayer)
            {
                WolfTrap trap =
                    Globals.instantiationManager.InstantiateWq<WolfTrap, WolfTrap>(prefab, i => i.Initialize(false));
                if (trap != null)
                {
                    trap.transform.position = Globals.animalManager.LocalPlayer.Position;
                    Globals.Log("Spawned trap: " + prefab.name);
                }
            }
        }

        private void SpawnScentTrack(ScentTrack prefab)
        {
            if (Globals.instantiationManager && Globals.animalManager && Globals.animalManager.LocalPlayer)
            {
                ScentTrack track =
                    Globals.instantiationManager.InstantiateWq<ScentTrack, ScentTrack>(prefab,
                        i => i.Initialize(false));
                if (track != null)
                {
                    track.transform.position = Globals.animalManager.LocalPlayer.Position;
                    Globals.Log("Spawned scent track: " + prefab.name);
                }
            }
        }

        private void SpawnVehicle(Vehicle prefab)
        {
            if (Globals.instantiationManager && Globals.animalManager && Globals.animalManager.LocalPlayer)
            {
                Instantiate(prefab, Globals.animalManager.LocalPlayer.Position + Vector3.up * 1f, Quaternion.identity);
            }
        }

        private void DrawPrefabList<T>(IEnumerable<T> prefabs, string label, Action<T> spawnAction)
            where T : UnityEngine.Object
        {
            if (prefabs == null) return;

            GUILayout.Label(label, HeaderStyle());
            foreach (T t in prefabs)
            {
                if (t != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(t.name);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Spawn", GUILayout.Width(80f))) spawnAction(t);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(10f);
        }

        private Vector3 DrawVector3Field(Vector3 value, string label)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(50f));

            GUILayout.Label("X:", GUILayout.Width(20f));
            string xStr = GUILayout.TextField(value.x.ToString("F3"), GUILayout.Width(70f));
            if (float.TryParse(xStr, out float x)) value.x = x;

            GUILayout.Label("Y:", GUILayout.Width(20f));
            string yStr = GUILayout.TextField(value.y.ToString("F3"), GUILayout.Width(70f));
            if (float.TryParse(yStr, out float y)) value.y = y;

            GUILayout.Label("Z:", GUILayout.Width(20f));
            string zStr = GUILayout.TextField(value.z.ToString("F3"), GUILayout.Width(70f));
            if (float.TryParse(zStr, out float z)) value.z = z;

            GUILayout.EndHorizontal();
            return value;
        }

        // GUI Styles
        private GUIStyle HeaderStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                normal = { textColor = new Color(0.9f, 0.95f, 1f) }
            };
        }

        private GUIStyle SubHeaderStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.85f, 0.95f) }
            };
        }

        private GUIStyle InfoStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.6f, 0.8f, 0.6f) }
            };
        }

        private GUIStyle WarningStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(1f, 0.8f, 0.4f) },
                fontStyle = FontStyle.Italic
            };
        }

        // Gizmos
        private void OnRenderObject()
        {
            if (!_showGizmos || !_showWindow) return;

            if (_gizmoMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;

                _gizmoMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _gizmoMaterial.SetInt("_SrcBlend", 5);
                _gizmoMaterial.SetInt("_DstBlend", 10);
                _gizmoMaterial.SetInt("_Cull", 0);
                _gizmoMaterial.SetInt("_ZWrite", 0);
                _gizmoMaterial.SetInt("_ZTest", 8);
            }

            _gizmoMaterial.SetPass(0);

            // Draw Spawned Objects
            foreach (Containers container in _spawnedObjects)
            {
                if (container.Instance != null)
                {
                    GL.PushMatrix();
                    Vector3 pos = container.Instance.transform.position;
                    float size = 0.5f;
                    bool isSelected = _spawnedObjects.IndexOf(container) == _selectedObjectIndex;

                    GL.Begin(GL.LINES);

                    // X Axis
                    GL.Color(isSelected ? Color.red : new Color(0.5f, 0.2f, 0.2f));
                    GL.Vertex(pos);
                    GL.Vertex(pos + container.Instance.transform.right * size);

                    // Y Axis
                    GL.Color(isSelected ? Color.green : new Color(0.2f, 0.5f, 0.2f));
                    GL.Vertex(pos);
                    GL.Vertex(pos + container.Instance.transform.up * size);

                    // Z Axis
                    GL.Color(isSelected ? Color.blue : new Color(0.2f, 0.2f, 0.5f));
                    GL.Vertex(pos);
                    GL.Vertex(pos + container.Instance.transform.forward * size);

                    GL.End();
                    GL.PopMatrix();
                }
            }

            // Draw Scene Object Selection
            if (_selectedSceneObject != null)
            {
                GL.PushMatrix();
                Vector3 pos = _selectedSceneObject.transform.position;
                float size = 1f;
                GL.Begin(GL.LINES);
                GL.Color(Color.yellow);

                // Simple box-like star around object
                GL.Vertex(pos + Vector3.up * size);
                GL.Vertex(pos - Vector3.up * 0.1f);

                GL.Vertex(pos + Vector3.right * size);
                GL.Vertex(pos - Vector3.right * 0.1f);

                GL.Vertex(pos + Vector3.forward * size);
                GL.Vertex(pos - Vector3.forward * 0.1f);

                GL.End();
                GL.PopMatrix();
            }
        }
    }
}