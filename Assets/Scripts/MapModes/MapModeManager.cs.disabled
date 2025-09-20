using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ProvinceSystem.MapModes
{
    /// <summary>
    /// Manages different map visualization modes
    /// </summary>
    public class MapModeManager : MonoBehaviour
    {
        [Header("Settings")]
        public MapModeType defaultMode = MapModeType.Terrain;
        public bool showDebugInfo = true;

        [Header("References")]
        public ProvinceManager provinceManager;

        public enum MapModeType
        {
            Terrain,
            Political,
            Province,
            Debug
        }

        private Dictionary<MapModeType, IMapMode> mapModes = new Dictionary<MapModeType, IMapMode>();
        private IMapMode currentMapMode;
        private MapModeType currentModeType;

        public MapModeType CurrentModeType => currentModeType;
        public IMapMode CurrentMode => currentMapMode;

        void Start()
        {
            Initialize();
        }

        void Update()
        {
            HandleKeyboardInput();
        }

        private void Initialize()
        {
            if (provinceManager == null)
                provinceManager = GetComponent<ProvinceManager>();

            if (provinceManager == null)
            {
                Debug.LogError("MapModeManager requires ProvinceManager!");
                enabled = false;
                return;
            }

            RegisterMapModes();
            SetMapMode(defaultMode);
        }

        private void RegisterMapModes()
        {
            // Register built-in map modes
            RegisterMapMode(MapModeType.Terrain, new TerrainMapMode());
            RegisterMapMode(MapModeType.Province, new ProvinceMapMode());
            RegisterMapMode(MapModeType.Debug, new DebugMapMode());

            // Political mode will be registered after country generation
        }

        public void RegisterMapMode(MapModeType type, IMapMode mode)
        {
            if (mode != null)
            {
                mode.Initialize(provinceManager);
                mapModes[type] = mode;
                Debug.Log($"Registered map mode: {type} ({mode.ModeName})");
            }
        }

        public void SetMapMode(MapModeType type)
        {
            if (!mapModes.ContainsKey(type))
            {
                Debug.LogWarning($"Map mode {type} not registered!");
                return;
            }

            // Exit current mode
            if (currentMapMode != null)
            {
                currentMapMode.OnExitMode();
            }

            // Enter new mode
            currentModeType = type;
            currentMapMode = mapModes[type];
            currentMapMode.OnEnterMode();

            if (showDebugInfo)
                Debug.Log($"Switched to map mode: {currentMapMode.ModeName}");
        }

        private void HandleKeyboardInput()
        {
            // Quick map mode switching with number keys
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetMapMode(MapModeType.Terrain);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SetMapMode(MapModeType.Political);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SetMapMode(MapModeType.Province);
            else if (Input.GetKeyDown(KeyCode.Alpha4))
                SetMapMode(MapModeType.Debug);

            // Cycle through modes with Tab
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var types = System.Enum.GetValues(typeof(MapModeType)).Cast<MapModeType>().ToList();
                int currentIndex = types.IndexOf(currentModeType);
                int nextIndex = (currentIndex + 1) % types.Count;
                SetMapMode(types[nextIndex]);
            }
        }

        public void RefreshCurrentMode()
        {
            currentMapMode?.UpdateAllProvinceColors();
        }

        public void UpdateProvince(int provinceId)
        {
            currentMapMode?.UpdateProvinceColor(provinceId);
        }

        void OnGUI()
        {
            if (!showDebugInfo) return;

            // Display current map mode in top-left corner
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.fontStyle = FontStyle.Bold;

            GUI.Label(new Rect(10, 10, 300, 30), $"Map Mode: {currentMapMode?.ModeName ?? "None"}", style);

            // Show available modes
            style.fontSize = 12;
            style.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(10, 40, 300, 100),
                "Press 1-4 to switch modes:\n" +
                "1: Terrain\n" +
                "2: Political\n" +
                "3: Province\n" +
                "4: Debug\n" +
                "Tab: Cycle modes", style);
        }
    }

    /// <summary>
    /// Default terrain map mode - shows provinces with their default colors
    /// </summary>
    public class TerrainMapMode : BaseMapMode
    {
        public override string ModeName => "Terrain";

        public override void UpdateProvinceColor(int provinceId)
        {
            var dataService = GetDataService();
            if (dataService == null) return;

            var province = dataService.GetProvinceById(provinceId);
            if (province != null)
            {
                // Use the province's original display color
                provinceColors[provinceId] = province.displayColor;
            }
        }
    }

    /// <summary>
    /// Province ID map mode - colors provinces by their ID
    /// </summary>
    public class ProvinceMapMode : BaseMapMode
    {
        public override string ModeName => "Province ID";

        public override void UpdateProvinceColor(int provinceId)
        {
            // Generate a unique color based on province ID
            Random.InitState(provinceId);
            provinceColors[provinceId] = new Color(
                Random.Range(0.2f, 1f),
                Random.Range(0.2f, 1f),
                Random.Range(0.2f, 1f),
                1f
            );
        }
    }

    /// <summary>
    /// Debug map mode - shows province connectivity
    /// </summary>
    public class DebugMapMode : BaseMapMode
    {
        public override string ModeName => "Debug";

        public override void UpdateProvinceColor(int provinceId)
        {
            // Color based on neighbor count
            var neighbors = provinceManager.GetNeighbors(provinceId);
            int neighborCount = neighbors?.Count ?? 0;

            if (neighborCount == 0)
                provinceColors[provinceId] = Color.red;
            else if (neighborCount <= 2)
                provinceColors[provinceId] = Color.yellow;
            else if (neighborCount <= 4)
                provinceColors[provinceId] = Color.green;
            else if (neighborCount <= 6)
                provinceColors[provinceId] = Color.cyan;
            else
                provinceColors[provinceId] = Color.blue;
        }
    }
}