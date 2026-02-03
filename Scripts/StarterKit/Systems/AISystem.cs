using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Core;
using Core.Commands;
using Core.Data;
using Core.Data.Ids;
using Core.Events;
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
    /// Performance: Pre-allocated buffers, zero gameplay allocations.
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

        // Pre-allocated buffers (zero gameplay allocations)
        private NativeList<ushort> ownedProvincesBuffer;
        private NativeList<ushort> neighborBuffer;
        private NativeList<ushort> candidateBuffer;

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

            // Pre-allocate reusable buffers
            ownedProvincesBuffer = new NativeList<ushort>(512, Allocator.Persistent);
            neighborBuffer = new NativeList<ushort>(32, Allocator.Persistent);
            candidateBuffer = new NativeList<ushort>(256, Allocator.Persistent);

            // Subscribe to monthly tick (token auto-disposed on Dispose)
            subscriptions.Add(gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick));

            if (logProgress)
            {
                ArchonLogger.Log("AISystem: Initialized (zero-alloc)", "starter_kit");
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
            // Find unowned provinces bordering this country using pre-allocated buffers
            // Instead of ProvinceQueryBuilder (which allocates NativeList + NativeHashSet per call),
            // we manually iterate owned provinces and check neighbors.

            // Get owned provinces into reusable buffer
            provinceSystem.GetCountryProvinces(countryId, ownedProvincesBuffer);

            if (ownedProvincesBuffer.Length == 0)
                return false;

            // Build candidate list: unowned neighbors with ownable terrain
            candidateBuffer.Clear();

            for (int i = 0; i < ownedProvincesBuffer.Length; i++)
            {
                ushort ownedProvince = ownedProvincesBuffer[i];

                // Get neighbors into reusable buffer
                gameState.Adjacencies.GetNeighbors(ownedProvince, neighborBuffer);

                for (int j = 0; j < neighborBuffer.Length; j++)
                {
                    ushort neighbor = neighborBuffer[j];

                    // Must be unowned
                    if (provinceSystem.GetProvinceOwner(neighbor) != 0)
                        continue;

                    // Must be ownable terrain
                    ushort terrainType = provinceSystem.GetProvinceTerrain(neighbor);
                    if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
                        continue;

                    // Avoid duplicates (multiple owned provinces may border same unowned province)
                    bool alreadyAdded = false;
                    for (int k = 0; k < candidateBuffer.Length; k++)
                    {
                        if (candidateBuffer[k] == neighbor)
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }
                    if (!alreadyAdded)
                    {
                        candidateBuffer.Add(neighbor);
                    }
                }
            }

            if (candidateBuffer.Length == 0)
                return false;

            // Pick random province to colonize
            ushort targetProvince = candidateBuffer[random.NextInt(candidateBuffer.Length)];

            // Colonize via ColonizeCommand (handles gold + ownership, syncs over network)
            var command = new ColonizeCommand
            {
                ProvinceId = new ProvinceId(targetProvince),
                CountryId = countryId
            };

            bool success = gameState.TryExecuteCommand(command);

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

            // Get owned provinces into reusable buffer
            provinceSystem.GetCountryProvinces(countryId, ownedProvincesBuffer);

            if (ownedProvincesBuffer.Length == 0)
                return false;

            // Build candidate list: provinces that can have more farms
            candidateBuffer.Clear();
            for (int i = 0; i < ownedProvincesBuffer.Length; i++)
            {
                ushort provinceId = ownedProvincesBuffer[i];
                int farmCount = buildingSystem.GetBuildingCount(provinceId, farmType.ID);

                if (farmCount < farmType.MaxPerProvince)
                {
                    candidateBuffer.Add(provinceId);
                }
            }

            if (candidateBuffer.Length == 0)
                return false;

            // Pick random province
            ushort targetProvince = candidateBuffer[random.NextInt(candidateBuffer.Length)];

            // Build farm directly (BuildingSystem runs deterministically on all clients)
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

            if (ownedProvincesBuffer.IsCreated) ownedProvincesBuffer.Dispose();
            if (neighborBuffer.IsCreated) neighborBuffer.Dispose();
            if (candidateBuffer.IsCreated) candidateBuffer.Dispose();

            if (logProgress)
            {
                ArchonLogger.Log("AISystem: Disposed", "starter_kit");
            }
        }
    }
}
