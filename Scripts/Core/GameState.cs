using UnityEngine;
using Unity.Collections;
using Core.Systems;
using Core.Queries;
using Core.Commands;
using Core.Registries;

namespace Core
{
    /// <summary>
    /// Central hub for all game data access - follows hub-and-spoke architecture
    /// Provides unified access to all game systems without owning the data
    /// Performance: All queries should be <0.01ms, zero allocations during gameplay
    /// </summary>
    public class GameState : MonoBehaviour
    {
        [Header("Core Systems")]
        [SerializeField] private bool initializeOnAwake = true;

        // System References - These own the actual data
        public Systems.ProvinceSystem Provinces { get; private set; }
        public Systems.CountrySystem Countries { get; private set; }
        public TimeManager Time { get; private set; }

        // Query Interfaces - These provide optimized data access
        public ProvinceQueries ProvinceQueries { get; private set; }
        public CountryQueries CountryQueries { get; private set; }

        // Registries - Static data lookups (definition ID → runtime ID, etc.)
        public GameRegistries Registries { get; private set; }

        // Core Infrastructure
        public EventBus EventBus { get; private set; }

        // State Management
        public bool IsInitialized { get; private set; }
        public bool IsLoading { get; private set; }
        public float LoadingProgress { get; private set; }

        /// <summary>
        /// Singleton access for global game state
        /// </summary>
        public static GameState Instance { get; private set; }

        void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                ArchonLogger.LogError("Multiple GameState instances detected! Destroying duplicate.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (initializeOnAwake)
            {
                InitializeSystems();
            }
        }

        /// <summary>
        /// Set game registries (called by EngineInitializer during loading)
        /// </summary>
        public void SetRegistries(GameRegistries registries)
        {
            Registries = registries;
            ArchonLogger.Log("GameState: Registries set");
        }

        /// <summary>
        /// Initialize all core systems in correct dependency order
        /// </summary>
        public void InitializeSystems()
        {
            if (IsInitialized)
            {
                ArchonLogger.LogWarning("GameState already initialized");
                return;
            }

            ArchonLogger.Log("Initializing GameState systems...");

            // 1. Core infrastructure first
            EventBus = new EventBus();
            Time = GetComponent<TimeManager>() ?? gameObject.AddComponent<TimeManager>();

            // 2. Data owner systems
            Provinces = GetComponent<Systems.ProvinceSystem>() ?? gameObject.AddComponent<Systems.ProvinceSystem>();
            Countries = GetComponent<Systems.CountrySystem>() ?? gameObject.AddComponent<Systems.CountrySystem>();

            // 3. Query interfaces
            ProvinceQueries = new ProvinceQueries(Provinces, Countries);
            CountryQueries = new CountryQueries(Countries, Provinces);

            // 4. Initialize systems
            Provinces.Initialize(EventBus);
            Countries.Initialize(EventBus);
            Time.Initialize(EventBus, Provinces); // Pass ProvinceSystem for buffer swapping

            IsInitialized = true;
            ArchonLogger.Log("GameState initialization complete");

            // Emit initialization complete event
            EventBus.Emit(new GameStateInitializedEvent());
        }

        /// <summary>
        /// Unified command execution - all game state changes go through here
        /// Provides validation, event emission, and multiplayer sync
        /// </summary>
        public bool TryExecuteCommand<T>(T command) where T : ICommand
        {
            if (!IsInitialized)
            {
                ArchonLogger.LogError("Cannot execute command - GameState not initialized");
                return false;
            }

            // Validate command
            if (!command.Validate(this))
            {
                ArchonLogger.LogWarning($"Command validation failed: {command.GetType().Name}");
                return false;
            }

            // Execute command
            try
            {
                command.Execute(this);

                // Emit command executed event for systems that need to react
                EventBus.Emit(new CommandExecutedEvent { CommandType = typeof(T), Success = true });

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"Command execution failed: {command.GetType().Name} - {e.Message}");
                EventBus.Emit(new CommandExecutedEvent { CommandType = typeof(T), Success = false, Error = e.Message });
                return false;
            }
        }

        /// <summary>
        /// Get basic province information - most common query
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            return ProvinceQueries.GetOwner(provinceId);
        }

        /// <summary>
        /// Get basic country information - most common query
        /// </summary>
        public Color32 GetCountryColor(ushort countryId)
        {
            return CountryQueries.GetColor(countryId);
        }

        /// <summary>
        /// Complex cross-system query - get all provinces owned by a country
        /// </summary>
        public NativeArray<ushort> GetCountryProvinces(ushort countryId)
        {
            return ProvinceQueries.GetCountryProvinces(countryId);
        }

        // REMOVED: GetCountryTotalDevelopment()
        // Development is game-specific - use Game layer queries instead

        void OnDestroy()
        {
            if (Instance == this)
            {
                // Clean up systems
                Provinces?.Dispose();
                Countries?.Dispose();
                EventBus?.Dispose();

                Instance = null;
            }
        }

        /// <summary>
        /// Process events every frame (frame-coherent batching)
        /// </summary>
        void Update()
        {
            if (!IsInitialized)
                return;

            // Process all queued events
            EventBus?.ProcessEvents();

            #if UNITY_EDITOR
            // Update debug info
            debugProvinceCount = Provinces?.ProvinceCount ?? 0;
            debugCountryCount = Countries?.CountryCount ?? 0;
            debugEventBusActive = EventBus?.IsActive ?? false;
            #endif
        }

        #if UNITY_EDITOR
        [Header("Debug Info")]
        [SerializeField, ReadOnly] private int debugProvinceCount;
        [SerializeField, ReadOnly] private int debugCountryCount;
        [SerializeField, ReadOnly] private bool debugEventBusActive;
        #endif
    }

    /// <summary>
    /// Core events for GameState lifecycle
    /// </summary>
    public struct GameStateInitializedEvent : IGameEvent
    {
        public float TimeStamp { get; set; }
    }

    public struct CommandExecutedEvent : IGameEvent
    {
        public System.Type CommandType;
        public bool Success;
        public string Error;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// ReadOnly attribute for inspector display
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }

    #if UNITY_EDITOR
    [UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            UnityEditor.EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
    #endif
}