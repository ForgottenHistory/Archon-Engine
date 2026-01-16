using System;
using System.Collections.Generic;
using Unity.Collections;
using Core;
using Core.Events;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple economy for StarterKit.
    /// 1 gold per province + building bonuses, collected monthly.
    /// Tracks gold for ALL countries (for ledger display).
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly bool logCollection;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Gold storage for all countries
        private Dictionary<ushort, int> countryGold = new Dictionary<ushort, int>();

        // Optional building system for bonus calculation
        private BuildingSystem buildingSystem;

        /// <summary>
        /// Player's gold (convenience property)
        /// </summary>
        public int Gold => GetCountryGold(playerState?.PlayerCountryId ?? 0);

        // Event for UI updates
        public event Action<int, int> OnGoldChanged; // oldValue, newValue

        public EconomySystem(GameState gameStateRef, PlayerState playerStateRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            logCollection = log;

            // Subscribe to monthly tick (token auto-disposed on Dispose)
            subscriptions.Add(gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick));

            if (logCollection)
            {
                ArchonLogger.Log("EconomySystem: Initialized", "starter_kit");
            }
        }

        /// <summary>
        /// Set building system for bonus calculation.
        /// Called after building system is created.
        /// </summary>
        public void SetBuildingSystem(BuildingSystem buildingSystemRef)
        {
            buildingSystem = buildingSystemRef;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            subscriptions.Dispose();
            isDisposed = true;
        }

        private void OnMonthlyTick(MonthlyTickEvent evt)
        {
            // Collect income for ALL countries with provinces
            CollectIncomeForAllCountries();
        }

        private void CollectIncomeForAllCountries()
        {
            if (gameState?.Countries == null) return;

            // Get all countries
            var countries = gameState.Countries.GetAllCountryIds();
            try
            {
                foreach (ushort countryId in countries)
                {
                    // Skip if country has no provinces
                    int provinceCount = CountProvinces(countryId);
                    if (provinceCount > 0)
                    {
                        CollectIncomeForCountry(countryId);
                    }
                }
            }
            finally
            {
                countries.Dispose();
            }
        }

        private void CollectIncomeForCountry(ushort countryId)
        {
            int provinceCount = CountProvinces(countryId);
            int baseIncome = provinceCount; // 1 gold per province
            int buildingBonus = CalculateBuildingBonus(countryId);
            int income = baseIncome + buildingBonus;

            if (income > 0)
            {
                int oldGold = GetCountryGold(countryId);
                int newGold = oldGold + income;
                countryGold[countryId] = newGold;

                // Fire event only for player
                if (playerState != null && countryId == playerState.PlayerCountryId)
                {
                    OnGoldChanged?.Invoke(oldGold, newGold);

                    if (logCollection)
                    {
                        ArchonLogger.Log($"EconomySystem: Collected {income} gold ({baseIncome} base + {buildingBonus} buildings) from {provinceCount} provinces (Total: {newGold})", "starter_kit");
                    }
                }
            }
        }

        private int CalculateBuildingBonus(ushort countryId)
        {
            if (buildingSystem == null || gameState?.ProvinceQueries == null)
                return 0;

            int totalBonus = 0;

            // Get all provinces owned by the country (must dispose NativeArray)
            var provinceIds = gameState.ProvinceQueries.GetCountryProvinces(countryId);
            try
            {
                foreach (var provinceId in provinceIds)
                {
                    totalBonus += buildingSystem.GetProvinceGoldBonus(provinceId);
                }
            }
            finally
            {
                provinceIds.Dispose();
            }

            return totalBonus;
        }

        private int CountProvinces(ushort countryId)
        {
            if (gameState?.ProvinceQueries == null)
                return 0;

            return gameState.ProvinceQueries.GetCountryProvinceCount(countryId);
        }

        /// <summary>
        /// Get gold for a specific country.
        /// </summary>
        public int GetCountryGold(ushort countryId)
        {
            if (countryId == 0) return 0;
            return countryGold.TryGetValue(countryId, out int gold) ? gold : 0;
        }

        /// <summary>
        /// Get monthly income for a country (province count + building bonuses)
        /// </summary>
        public int GetMonthlyIncome(ushort countryId)
        {
            return CountProvinces(countryId) + CalculateBuildingBonus(countryId);
        }

        /// <summary>
        /// Get monthly income for player (convenience)
        /// </summary>
        public int GetMonthlyIncome()
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return 0;

            return GetMonthlyIncome(playerState.PlayerCountryId);
        }

        /// <summary>
        /// Add gold to player (for commands/cheats)
        /// </summary>
        public void AddGold(int amount)
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return;

            AddGoldToCountry(playerState.PlayerCountryId, amount);
        }

        /// <summary>
        /// Add gold to a specific country
        /// </summary>
        public void AddGoldToCountry(ushort countryId, int amount)
        {
            int oldGold = GetCountryGold(countryId);
            int newGold = oldGold + amount;
            countryGold[countryId] = newGold;

            if (playerState != null && countryId == playerState.PlayerCountryId)
            {
                OnGoldChanged?.Invoke(oldGold, newGold);
            }
        }

        /// <summary>
        /// Remove gold from player (returns false if insufficient)
        /// </summary>
        public bool RemoveGold(int amount)
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return false;

            return RemoveGoldFromCountry(playerState.PlayerCountryId, amount);
        }

        /// <summary>
        /// Remove gold from a specific country (returns false if insufficient)
        /// </summary>
        public bool RemoveGoldFromCountry(ushort countryId, int amount)
        {
            int currentGold = GetCountryGold(countryId);
            if (currentGold < amount)
                return false;

            int newGold = currentGold - amount;
            countryGold[countryId] = newGold;

            if (playerState != null && countryId == playerState.PlayerCountryId)
            {
                OnGoldChanged?.Invoke(currentGold, newGold);
            }
            return true;
        }

        /// <summary>
        /// Get all countries with tracked gold (for ledger)
        /// </summary>
        public IEnumerable<ushort> GetCountriesWithGold()
        {
            return countryGold.Keys;
        }
    }
}
