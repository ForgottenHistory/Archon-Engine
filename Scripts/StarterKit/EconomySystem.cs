using System;
using Core;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple economy for StarterKit.
    /// 1 gold per province, collected monthly.
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly bool logCollection;
        private int gold;
        private bool isDisposed;

        public int Gold => gold;

        // Event for UI updates
        public event Action<int, int> OnGoldChanged; // oldValue, newValue

        public EconomySystem(GameState gameStateRef, PlayerState playerStateRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            logCollection = log;
            gold = 0;

            // Subscribe to monthly tick
            gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick);

            if (logCollection)
            {
                ArchonLogger.Log("EconomySystem: Initialized", "starter_kit");
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;

            gameState?.EventBus?.Unsubscribe<MonthlyTickEvent>(OnMonthlyTick);
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
            int income = provinceCount; // 1 gold per province

            if (income > 0)
            {
                int oldGold = gold;
                gold += income;

                OnGoldChanged?.Invoke(oldGold, gold);

                if (logCollection)
                {
                    ArchonLogger.Log($"EconomySystem: Collected {income} gold from {provinceCount} provinces (Total: {gold})", "starter_kit");
                }
            }
        }

        private int CountProvinces(ushort countryId)
        {
            if (gameState?.ProvinceQueries == null)
                return 0;

            return gameState.ProvinceQueries.GetCountryProvinceCount(countryId);
        }

        /// <summary>
        /// Get monthly income (province count)
        /// </summary>
        public int GetMonthlyIncome()
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return 0;

            return CountProvinces(playerState.PlayerCountryId);
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
