using System;
using Unity.Collections;
using Core;
using Core.Events;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple economy for StarterKit.
    /// 1 gold per province + building bonuses, collected monthly.
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly bool logCollection;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private int gold;
        private bool isDisposed;

        // Optional building system for bonus calculation
        private BuildingSystem buildingSystem;

        public int Gold => gold;

        // Event for UI updates
        public event Action<int, int> OnGoldChanged; // oldValue, newValue

        public EconomySystem(GameState gameStateRef, PlayerState playerStateRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            logCollection = log;
            gold = 0;

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
            if (playerState == null || !playerState.HasPlayerCountry)
                return;

            CollectIncome();
        }

        private void CollectIncome()
        {
            ushort countryId = playerState.PlayerCountryId;
            int provinceCount = CountProvinces(countryId);
            int baseIncome = provinceCount; // 1 gold per province
            int buildingBonus = CalculateBuildingBonus(countryId);
            int income = baseIncome + buildingBonus;

            if (income > 0)
            {
                int oldGold = gold;
                gold += income;

                OnGoldChanged?.Invoke(oldGold, gold);

                if (logCollection)
                {
                    ArchonLogger.Log($"EconomySystem: Collected {income} gold ({baseIncome} base + {buildingBonus} buildings) from {provinceCount} provinces (Total: {gold})", "starter_kit");
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
        /// Get monthly income (province count + building bonuses)
        /// </summary>
        public int GetMonthlyIncome()
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return 0;

            ushort countryId = playerState.PlayerCountryId;
            return CountProvinces(countryId) + CalculateBuildingBonus(countryId);
        }

        /// <summary>
        /// Add gold (for commands/cheats)
        /// </summary>
        public void AddGold(int amount)
        {
            int oldGold = gold;
            gold += amount;
            OnGoldChanged?.Invoke(oldGold, gold);
        }

        /// <summary>
        /// Remove gold (returns false if insufficient)
        /// </summary>
        public bool RemoveGold(int amount)
        {
            if (gold < amount)
                return false;

            int oldGold = gold;
            gold -= amount;
            OnGoldChanged?.Invoke(oldGold, gold);
            return true;
        }
    }
}
