using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Core;
using Core.Commands;
using Core.Data;
using Core.Data.Ids;
using Core.Events;
using Core.Queries;
using Core.Systems;
using Map.Rendering.Terrain;
using StarterKit.Commands;

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

        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly BuildingSystem buildingSystem;
        private readonly EconomySystem economySystem;
        private readonly bool logProgress;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Deterministic random for multiplayer sync (seeded per-tick)
        private DeterministicRandom random;

        // Terrain lookup for ownable checks
        private TerrainRGBLookup terrainLookup;

        public AISystem(GameState gameStateRef, PlayerState playerStateRef, BuildingSystem buildingSystemRef, EconomySystem economySystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            buildingSystem = buildingSystemRef;
            economySystem = economySystemRef;
            logProgress = log;

            // Initialize deterministic random with fixed seed (will be reseeded each tick)
            random = new DeterministicRandom(12345);

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

            // In multiplayer, only host runs AI (commands are broadcast to clients)
            var networkInit = Initializer.Instance?.NetworkInitializer;
            if (networkInit != null && networkInit.IsMultiplayer && !networkInit.IsHost)
                return;

            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem == null)
                return;

            var provinceSystem = gameState.GetComponent<Core.Systems.ProvinceSystem>();
            if (provinceSystem == null)
                return;

            // Reseed random with current tick for deterministic multiplayer behavior
            // All clients at the same tick will generate the same random sequence
            var timeManager = gameState.GetComponent<TimeManager>();
            if (timeManager != null)
            {
                random.SetSeed((uint)timeManager.CurrentTick);
            }

            ushort playerCountryId = playerState.PlayerCountryId;
            int countryCount = countrySystem.CountryCount;

            // Process each AI country
            for (ushort countryId = 1; countryId <= countryCount; countryId++)
            {
                // Skip player country
                if (countryId == playerCountryId)
                    continue;

                // Skip countries controlled by other human players in multiplayer
                if (networkInit != null && networkInit.IsCountryHumanControlled(countryId))
                    continue;

                // Skip if country doesn't exist
                if (!countrySystem.HasCountry(countryId))
                    continue;

                // Gold is managed by EconomySystem (income collected there for all countries)
                int currentGold = economySystem.GetCountryGoldInt(countryId);

                // Priority 1: Try to colonize if we can afford it (expand first)
                if (currentGold >= ColonizeCommand.COLONIZE_COST)
                {
                    if (TryColonize(countryId, provinceSystem))
                    {
                        // Gold deducted inside ColonizeCommand
                        continue; // Move to next country after colonizing
                    }
                }

                // Priority 2: Try to build a farm if we can afford it (develop after expanding)
                // Note: Gold is deducted inside ConstructBuildingCommand via ConstructForCountry
                var farmType = buildingSystem.GetBuildingType("farm");
                if (farmType != null && currentGold >= farmType.Cost)
                {
                    TryBuildFarm(countryId, provinceSystem);
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
            ushort targetProvince = colonizeCandidates[random.NextInt(colonizeCandidates.Count)];

            // Colonize via ColonizeCommand (handles gold + ownership, syncs over network)
            var command = new ColonizeCommand
            {
                ProvinceId = new ProvinceId(targetProvince),
                CountryId = countryId
            };

            // DEBUG: Log adjacency validation
            if (logProgress)
            {
                using var owned = provinceSystem.GetCountryProvinces(countryId, Allocator.Temp);
                bool actuallyAdjacent = false;
                ushort adjacentTo = 0;
                for (int i = 0; i < owned.Length; i++)
                {
                    if (gameState.Adjacencies.IsAdjacent(owned[i], targetProvince))
                    {
                        actuallyAdjacent = true;
                        adjacentTo = owned[i];
                        break;
                    }
                }
                ArchonLogger.Log($"AISystem: Country {countryId} colonizing P{targetProvince} (adjacent={actuallyAdjacent}, adjTo=P{adjacentTo}, owned={owned.Length}, candidates={colonizeCandidates.Count})", "starter_kit");
            }

            bool success = gameState.TryExecuteCommand(command, out string message);

            if (success && logProgress)
            {
                ArchonLogger.Log($"AISystem: Country {countryId} colonized province {targetProvince} (spent {ColonizeCommand.COLONIZE_COST} gold)", "starter_kit");
            }

            return success;
        }

        private bool TryBuildFarm(ushort countryId, Core.Systems.ProvinceSystem provinceSystem)
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
            ushort targetProvince = validProvinces[random.NextInt(validProvinces.Count)];

            // Build farm directly (BuildingSystem is GAME layer, runs deterministically on all clients)
            bool success = buildingSystem.ConstructForCountry(targetProvince, "farm", countryId);

            if (success && logProgress)
            {
                ArchonLogger.Log($"AISystem: Country {countryId} built farm in province {targetProvince}", "starter_kit");
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
