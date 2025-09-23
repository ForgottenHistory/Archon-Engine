using UnityEngine;

namespace MapModes
{
    public class TerrainMapMode : BaseMapMode
    {
        [Header("Terrain Colors")]
        public Color landColor = new Color(0.4f, 0.8f, 0.2f, 1f);  // Green for land
        public Color seaColor = new Color(0.2f, 0.4f, 0.8f, 1f);   // Blue for sea
        public Color lakeColor = new Color(0.3f, 0.6f, 0.9f, 1f);  // Light blue for lakes

        private MapDefinitionLoader mapDefinitionLoader;
        private MapDefinition mapDefinition;

        public override string ModeName => "Terrain (Land/Sea)";

        public override void Initialize(MapController controller)
        {
            base.Initialize(controller);

            mapDefinitionLoader = controller.GetComponent<MapDefinitionLoader>();
            if (mapDefinitionLoader == null)
            {
                mapDefinitionLoader = controller.gameObject.AddComponent<MapDefinitionLoader>();
            }

            LoadMapDefinition();
        }

        private async void LoadMapDefinition()
        {
            if (mapDefinitionLoader != null && !mapDefinitionLoader.IsLoaded)
            {
                bool success = await mapDefinitionLoader.LoadMapDefinition();
                if (success)
                {
                    mapDefinition = mapDefinitionLoader.GetMapDefinition();
                    Debug.Log($"Terrain map mode loaded map definition with {mapDefinition.seaProvinces.Count} sea provinces");
                }
                else
                {
                    Debug.LogWarning("Failed to load map definition for terrain mode");
                }
            }
            else if (mapDefinitionLoader != null)
            {
                mapDefinition = mapDefinitionLoader.GetMapDefinition();
            }
        }

        public override void UpdateProvinceColor(int provinceId)
        {
            if (mapDefinition == null)
            {
                provinceColors[provinceId] = Color.gray;
                return;
            }

            if (mapDefinition.IsSeaProvince(provinceId))
            {
                provinceColors[provinceId] = seaColor;
            }
            else if (mapDefinition.IsLakeProvince(provinceId))
            {
                provinceColors[provinceId] = lakeColor;
            }
            else
            {
                provinceColors[provinceId] = landColor;
            }
        }

        public override void OnEnterMode()
        {
            if (mapDefinition == null)
            {
                LoadMapDefinition();
                return;
            }

            base.OnEnterMode();
        }

        public override void OnExitMode()
        {
            base.OnExitMode();
            RestoreOriginalColors();
        }
    }
}