using System;
using System.Collections.Generic;
using UnityEngine;
using Core;
using Core.Events;
using Core.Systems;
using Map.Core;
using Map.Rendering.Terrain;

namespace StarterKit
{
    /// <summary>
    /// Simple AI for StarterKit. Non-player countries colonize and build farms on monthly tick.
    /// AI countries use the shared EconomySystem for gold (same as player).
    /// Priority: Colonize first (expand), then build farms (develop).
    /// </summary>
    public class AISystem : IDisposable
    {
        private const int COLONIZE_COST = 20;

        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly BuildingSystem buildingSystem;
        private readonly EconomySystem economySystem;
        private readonly bool logProgress;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Random for province selection
        private System.Random random;

        // Terrain lookup for ownable checks
        private TerrainRGBLookup terrainLookup;

        public AISystem(GameState gameStateRef, PlayerState playerStateRef, BuildingSystem buildingSystemRef, EconomySystem economySystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            buildingSystem = buildingSystemRef;
            economySystem = economySystemRef;
            logProgress = log;

            random = new System.Random();

            // Initialize terrain lookup for ownable checks (use DataDirectory from GameSettings)
            var mapInitializer = UnityEngine.Object.FindFirstObjectByType<MapInitializer>();
            string dataDirectory = mapInitializer?.DataDirectory;
            terrainLookup = new TerrainRGBLookup();
            terrainLookup.Initialize(dataDirectory, false);

            // Subscribe to monthly tick (token auto-disposed on Dispose)
            subscriptions.Add(gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick));

            if (logProgress)
            {
                ArchonLogger.Log("AISystem: Initialized", "starter_kit");
            }
        }

        private void OnMonthlyTick(MonthlyTickEvent evt)
        {
            ProcessAI();
        }

        private void ProcessAI()
        {
            if (!playerState.HasPlayerCountry)
                return;

            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem == null)
                return;

            var provinceSystem = gameState.GetComponent<Core.Systems.ProvinceSystem>();
            if (provinceSystem == null)
                return;

            ushort playerCountryId = playerState.PlayerCountryId;
            int countryCount = countrySystem.CountryCount;

            // Process each AI country
            for (ushort countryId = 1; countryId <= countryCount; countryId++)
            {
                // Skip player country
                if (countryId == playerCountryId)
                    continue;

                // Skip if country doesn't exist
                if (!countrySystem.HasCountry(countryId))
                    continue;

                // Gold is managed by EconomySystem (income collected there for all countries)
                int currentGold = economySystem.GetCountryGoldInt(countryId);

                // Priority 1: Try to colonize if we can afford it (expand first)
                if (currentGold >= COLONIZE_COST)
                {
                    if (TryColonize(countryId, provinceSystem))
                    {
                        economySystem.RemoveGoldFromCountry(countryId, COLONIZE_COST);
                        continue; // Move to next country after colonizing
                    }
                }

                // Priority 2: Try to build a farm if we can afford it (develop after expanding)
                var farmType = buildingSystem.GetBuildingType("farm");
                if (farmType != null && currentGold >= farmType.Cost)
                {
                    if (TryBuildFarm(countryId, provinceSystem, farmType.Cost))
                    {
                        economySystem.RemoveGoldFromCountry(countryId, farmType.Cost);
                    }
                }
            }
        }

        private bool TryColonize(ushort countryId, Core.Systems.ProvinceSystem provinceSystem)
        {
            // Get owned provinces
            var ownedProvinces = provinceSystem.GetCountryProvinces(countryId);
            try
            {
                if (ownedProvinces.Length == 0)
                    return false;

                // Find all unowned, ownable neighbor provinces
                var colonizeCandidates = new List<ushort>();

                for (int i = 0; i < ownedProvinces.Length; i++)
                {
                    ushort ownedProvince = ownedProvinces[i];

                    // Get neighbors of this owned province
                    using (var neighbors = gameState.Adjacencies.GetNeighbors(ownedProvince))
                    {
                        for (int j = 0; j < neighbors.Length; j++)
                        {
                            ushort neighborId = neighbors[j];

                            // Check if unowned
                            ushort owner = gameState.ProvinceQueries.GetOwner(neighborId);
                            if (owner != 0)
                                continue;

                            // Check if terrain is ownable
                            ushort terrainType = gameState.ProvinceQueries.GetTerrain(neighborId);
                            if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
                                continue;

                            // Avoid duplicates (same province may neighbor multiple owned provinces)
                            if (!colonizeCandidates.Contains(neighborId))
                            {
                                colonizeCandidates.Add(neighborId);
                            }
                        }
                    }
                }

                if (colonizeCandidates.Count == 0)
                    return false;

                // Pick random province to colonize
                ushort targetProvince = colonizeCandidates[random.Next(colonizeCandidates.Count)];

                // Colonize (set owner)
                gameState.Provinces.SetProvinceOwner(targetProvince, countryId);

                if (logProgress)
                {
                    ArchonLogger.Log($"AISystem: Country {countryId} colonized province {targetProvince} (spent {COLONIZE_COST} gold)", "starter_kit");
                }

                return true;
            }
            finally
            {
                ownedProvinces.Dispose();
            }
        }

        private bool TryBuildFarm(ushort countryId, Core.Systems.ProvinceSystem provinceSystem, int cost)
        {
            // Get owned provinces (must dispose NativeArray)
            var provinces = provinceSystem.GetCountryProvinces(countryId);
            try
            {
                if (provinces.Length == 0)
                    return false;

                // Find provinces that can have more farms
                var farmType = buildingSystem.GetBuildingType("farm");
                if (farmType == null)
                    return false;

                var validProvinces = new List<ushort>();
                for (int i = 0; i < provinces.Length; i++)
                {
                    ushort provinceId = provinces[i];
                    int farmCount = buildingSystem.GetBuildingCount(provinceId, farmType.ID);

                    // Check if below max
                    if (farmCount < farmType.MaxPerProvince)
                    {
                        validProvinces.Add(provinceId);
                    }
                }

                if (validProvinces.Count == 0)
                    return false;

                // Pick random province
                ushort targetProvince = validProvinces[random.Next(validProvinces.Count)];

                // Build farm
                bool success = buildingSystem.ConstructForAI(targetProvince, "farm");

                if (success && logProgress)
                {
                    ArchonLogger.Log($"AISystem: Country {countryId} built farm in province {targetProvince} (spent {cost} gold)", "starter_kit");
                }

                return success;
            }
            finally
            {
                provinces.Dispose();
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            subscriptions.Dispose();

            if (logProgress)
            {
                ArchonLogger.Log("AISystem: Disposed", "starter_kit");
            }
        }
    }
}
