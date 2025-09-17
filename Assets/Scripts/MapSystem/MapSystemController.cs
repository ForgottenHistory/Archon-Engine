using UnityEngine;
using ProvinceSystem;
using ProvinceSystem.MapModes;
using ProvinceSystem.Countries;
using ProvinceSystem.Map;
using System.Collections;

namespace ProvinceSystem.MapSystem
{
    /// <summary>
    /// Main controller that integrates province system, country generation, and map modes
    /// </summary>
    public class MapSystemController : MonoBehaviour
    {
        [Header("Core Components")]
        public OptimizedProvinceMeshGenerator provinceMeshGenerator;
        public FastAdjacencyScanner adjacencyScanner;
        public ProvinceManager provinceManager;

        [Header("Map System Components")]
        public MapModeManager mapModeManager;
        public CountryGenerator countryGenerator;

        [Header("Settings")]
        public bool autoGenerateCountries = true;
        public float countryGenerationDelay = 2f;

        [Header("Debug")]
        public bool showDebugInfo = true;

        private bool isInitialized = false;
        private bool countriesGenerated = false;

        void Awake()
        {
            ValidateComponents();
        }

        void Start()
        {
            StartCoroutine(InitializeMapSystem());
        }

        private void ValidateComponents()
        {
            // Auto-find components if not assigned
            if (provinceMeshGenerator == null)
                provinceMeshGenerator = GetComponent<OptimizedProvinceMeshGenerator>();

            if (adjacencyScanner == null)
                adjacencyScanner = GetComponent<FastAdjacencyScanner>();

            if (provinceManager == null)
                provinceManager = GetComponent<ProvinceManager>();

            if (mapModeManager == null)
                mapModeManager = GetComponent<MapModeManager>();

            if (countryGenerator == null)
                countryGenerator = GetComponent<CountryGenerator>();

            // Add components if missing
            if (provinceManager == null)
            {
                provinceManager = gameObject.AddComponent<ProvinceManager>();
                Debug.Log("Added ProvinceManager component");
            }

            if (mapModeManager == null)
            {
                mapModeManager = gameObject.AddComponent<MapModeManager>();
                Debug.Log("Added MapModeManager component");
            }

            if (countryGenerator == null)
            {
                countryGenerator = gameObject.AddComponent<CountryGenerator>();
                Debug.Log("Added CountryGenerator component");
            }
        }

        private IEnumerator InitializeMapSystem()
        {
            Debug.Log("Starting Map System initialization...");

            // Wait for province mesh generation to complete
            while (provinceMeshGenerator == null || !IsProvinceMeshReady())
            {
                yield return new WaitForSeconds(0.1f);
            }

            Debug.Log("Province meshes ready");

            // Wait for adjacency scanning to complete
            while (adjacencyScanner == null || !IsAdjacencyDataReady())
            {
                yield return new WaitForSeconds(0.1f);
            }

            Debug.Log("Adjacency data ready");

            // Initialize province manager with adjacency data
            provinceManager.BuildNeighborMap();
            Debug.Log("Province neighbor map built");

            // Initialize map mode manager
            mapModeManager.provinceManager = provinceManager;

            // Generate countries if enabled
            if (autoGenerateCountries)
            {
                yield return new WaitForSeconds(countryGenerationDelay);
                GenerateCountries();
            }

            isInitialized = true;
            Debug.Log("Map System initialization complete!");
        }

        private bool IsProvinceMeshReady()
        {
            // Check if province mesh generation is complete
            // This is a simplified check - you may need to add a proper completion flag to OptimizedProvinceMeshGenerator
            var dataService = GetDataService();
            return dataService != null && dataService.GetProvinceCount() > 0;
        }

        private bool IsAdjacencyDataReady()
        {
            return adjacencyScanner != null &&
                   adjacencyScanner.IdAdjacencies != null &&
                   adjacencyScanner.IdAdjacencies.Count > 0;
        }

        public void GenerateCountries()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("Cannot generate countries - system not initialized");
                return;
            }

            if (countriesGenerated)
            {
                Debug.LogWarning("Countries already generated!");
                return;
            }

            Debug.Log("Starting country generation...");
            countryGenerator.GenerateCountries(provinceManager);
            countriesGenerated = true;
        }

        void OnCountryGenerationComplete()
        {
            Debug.Log("Country generation complete - registering political map mode");

            // Load map definition for political mode
            MapDefinitionLoader.MapDefinition mapDef = null;
            string mapDefPath = System.IO.Path.Combine(Application.dataPath, "Resources", "default.map");
            if (System.IO.File.Exists(mapDefPath))
            {
                mapDef = MapDefinitionLoader.LoadMapDefinition(mapDefPath);
            }

            // Register political map mode with the country service and map definition
            var politicalMode = new PoliticalMapMode(countryGenerator.CountryService, mapDef);
            mapModeManager.RegisterMapMode(MapModeManager.MapModeType.Political, politicalMode);

            // Switch to political mode to show the results
            mapModeManager.SetMapMode(MapModeManager.MapModeType.Political);
        }

        public void RegenerateCountries()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("Cannot regenerate countries - system not initialized");
                return;
            }

            countriesGenerated = false;
            countryGenerator.CountryService.Clear();
            GenerateCountries();
        }

        private Services.ProvinceDataService GetDataService()
        {
            if (provinceMeshGenerator == null) return null;

            var field = provinceMeshGenerator.GetType()
                .GetField("dataService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(provinceMeshGenerator) as Services.ProvinceDataService;
        }

        void OnGUI()
        {
            if (!showDebugInfo || !isInitialized) return;

            // Show country generation button
            if (!countriesGenerated)
            {
                if (GUI.Button(new Rect(10, Screen.height - 100, 200, 30), "Generate Countries"))
                {
                    GenerateCountries();
                }
            }
            else
            {
                if (GUI.Button(new Rect(10, Screen.height - 100, 200, 30), "Regenerate Countries"))
                {
                    RegenerateCountries();
                }

                // Show country statistics
                if (countryGenerator != null && countryGenerator.CountryService != null)
                {
                    var countries = countryGenerator.CountryService.GetAllCountries();
                    GUI.Label(new Rect(10, Screen.height - 60, 300, 20),
                        $"Countries: {countries.Count}");
                }
            }
        }
    }
}