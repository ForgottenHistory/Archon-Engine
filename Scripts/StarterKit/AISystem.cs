using System;
using System.Collections.Generic;
using Core;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple AI for StarterKit. Non-player countries build farms on monthly tick.
    /// AI countries earn and spend gold like the player.
    /// </summary>
    public class AISystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly BuildingSystem buildingSystem;
        private readonly bool logProgress;
        private bool isDisposed;

        // Random for province selection
        private System.Random random;

        // Gold tracking per AI country (countryId -> gold)
        private readonly Dictionary<ushort, int> aiGold;

        public AISystem(GameState gameStateRef, PlayerState playerStateRef, BuildingSystem buildingSystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            buildingSystem = buildingSystemRef;
            logProgress = log;

            random = new System.Random();
            aiGold = new Dictionary<ushort, int>();

            // Pre-allocate gold entries for all countries (zero allocations during gameplay)
            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem != null)
            {
                for (ushort i = 1; i <= countrySystem.CountryCount; i++)
                {
                    aiGold[i] = 0;
                }
            }

            // Subscribe to monthly tick
            gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick);

            if (logProgress)
            {
                ArchonLogger.Log($"AISystem: Initialized with {aiGold.Count} country slots", "starter_kit");
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

                // Calculate and add income (same formula as player: provinces + building bonus)
                int income = CalculateIncome(countryId, provinceSystem);
                aiGold[countryId] += income;

                // Try to build a farm if we can afford it
                var farmType = buildingSystem.GetBuildingType("farm");
                if (farmType != null && aiGold[countryId] >= farmType.Cost)
                {
                    if (TryBuildFarm(countryId, provinceSystem, farmType.Cost))
                    {
                        aiGold[countryId] -= farmType.Cost;
                    }
                }
            }
        }

        private int CalculateIncome(ushort countryId, Core.Systems.ProvinceSystem provinceSystem)
        {
            var provinces = provinceSystem.GetCountryProvinces(countryId);
            try
            {
                int baseIncome = provinces.Length; // 1 gold per province
                int buildingBonus = 0;

                // Add building bonuses
                for (int i = 0; i < provinces.Length; i++)
                {
                    buildingBonus += buildingSystem.GetProvinceGoldBonus(provinces[i]);
                }

                return baseIncome + buildingBonus;
            }
            finally
            {
                provinces.Dispose();
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

            gameState?.EventBus?.Unsubscribe<MonthlyTickEvent>(OnMonthlyTick);

            if (logProgress)
            {
                ArchonLogger.Log("AISystem: Disposed", "starter_kit");
            }
        }
    }
}
