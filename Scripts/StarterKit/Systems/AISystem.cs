using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Core;
using Core.Events;
using Core.Queries;
using Core.Systems;
using Map.Rendering.Terrain;

namespace StarterKit
{
    /// <summary>
    /// Simple AI for StarterKit. Non-player countries colonize and build farms on monthly tick.
    /// AI countries use the shared EconomySystem for gold (same as player).
    /// Priority: Colonize first (expand), then build farms (develop).
    ///
    /// Demonstrates: ProvinceQueryBuilder for fluent province filtering.
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
            var engine = Engine.ArchonEngine.Instance;
            string dataDirectory = engine?.GameSettings?.DataDirectory;
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
            // Use ProvinceQueryBuilder (ENGINE) to find unowned provinces bordering our country
            // Then apply GAME-layer filter for ownable terrain
            using var query = new ProvinceQueryBuilder(provinceSystem, gameState.Adjacencies);
            using var candidates = query
                .BorderingCountry(countryId)  // Adjacent to our provinces
                .IsUnowned()                   // Not owned by anyone
                .Execute(Allocator.Temp);

            if (candidates.Length == 0)
                return false;

            // GAME-layer filter: only ownable terrain (ENGINE doesn't know about this concept)
            var colonizeCandidates = new List<ushort>();
            for (int i = 0; i < candidates.Length; i++)
            {
                ushort provinceId = candidates[i];
                ushort terrainType = provinceSystem.GetProvinceTerrain(provinceId);

                if (terrainLookup == null || terrainLookup.IsTerrainOwnable(terrainType))
                {
                    colonizeCandidates.Add(provinceId);
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

        private bool TryBuildFarm(ushort countryId, Core.Systems.ProvinceSystem provinceSystem, int cost)
        {
            var farmType = buildingSystem.GetBuildingType("farm");
            if (farmType == null)
                return false;

            // Use ProvinceQueryBuilder (ENGINE) to get all provinces owned by this country
            using var query = new ProvinceQueryBuilder(provinceSystem, gameState.Adjacencies);
            using var ownedProvinces = query
                .OwnedBy(countryId)
                .Execute(Allocator.Temp);

            if (ownedProvinces.Length == 0)
                return false;

            // GAME-layer filter: provinces that can have more farms
            var validProvinces = new List<ushort>();
            for (int i = 0; i < ownedProvinces.Length; i++)
            {
                ushort provinceId = ownedProvinces[i];
                int farmCount = buildingSystem.GetBuildingCount(provinceId, farmType.ID);

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
