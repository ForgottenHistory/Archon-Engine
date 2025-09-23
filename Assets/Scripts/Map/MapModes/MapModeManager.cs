using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace MapModes
{
    public class MapModeManager : MonoBehaviour
    {
        [Header("Settings")]
        public MapModeType defaultMode = MapModeType.Default;
        public bool showDebugInfo = true;

        [Header("References")]
        public MapController mapController;

        public enum MapModeType
        {
            Default,
            Province,
            Terrain
        }

        private Dictionary<MapModeType, IMapMode> mapModes = new Dictionary<MapModeType, IMapMode>();
        private IMapMode currentMapMode;
        private MapModeType currentModeType;

        public MapModeType CurrentModeType => currentModeType;
        public IMapMode CurrentMode => currentMapMode;

        void Start()
        {
            if (mapController == null)
                mapController = GetComponent<MapController>();

            if (mapController != null && mapController.IsInitialized)
            {
                Initialize();
            }
            else
            {
                StartCoroutine(WaitForMapControllerInitialization());
            }
        }

        private System.Collections.IEnumerator WaitForMapControllerInitialization()
        {
            while (mapController == null || !mapController.IsInitialized)
            {
                yield return new WaitForSeconds(0.1f);
            }
            Initialize();
        }

        void Update()
        {
            HandleKeyboardInput();
        }

        private void Initialize()
        {
            if (mapController == null)
            {
                Debug.LogError("MapModeManager requires MapController!");
                enabled = false;
                return;
            }

            RegisterMapModes();
            SetMapMode(defaultMode);
        }

        private void RegisterMapModes()
        {
            RegisterMapMode(MapModeType.Default, new DefaultMapMode());
            RegisterMapMode(MapModeType.Province, new ProvinceMapMode());
            RegisterMapMode(MapModeType.Terrain, new TerrainMapMode());
        }

        public void RegisterMapMode(MapModeType type, IMapMode mode)
        {
            if (mode != null)
            {
                mode.Initialize(mapController);
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

            if (currentMapMode != null)
            {
                currentMapMode.OnExitMode();
            }

            currentModeType = type;
            currentMapMode = mapModes[type];
            currentMapMode.OnEnterMode();

            if (showDebugInfo)
                Debug.Log($"Switched to map mode: {currentMapMode.ModeName}");
        }

        private void HandleKeyboardInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetMapMode(MapModeType.Default);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SetMapMode(MapModeType.Province);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SetMapMode(MapModeType.Terrain);

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

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.fontStyle = FontStyle.Bold;

            GUI.Label(new Rect(10, 10, 300, 30), $"Map Mode: {currentMapMode?.ModeName ?? "None"}", style);

            style.fontSize = 12;
            style.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(10, 40, 300, 100),
                "Press 1-3 to switch modes:\n" +
                "1: Default (Original)\n" +
                "2: Province (Identifier Colors)\n" +
                "3: Terrain (Land/Sea)\n" +
                "Tab: Cycle modes", style);
        }
    }
}