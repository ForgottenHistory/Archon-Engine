using System;
using System.Collections.Generic;
using System.IO;
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

                // Emit gold changed event
                EmitGoldChanged(countryId, oldGold, newGold);

                if (logCollection && playerState != null && countryId == playerState.PlayerCountryId)
                {
                    ArchonLogger.Log($"EconomySystem: Collected {income} gold ({baseIncome} base + {buildingBonus} buildings) from {provinceCount} provinces (Total: {newGold})", "starter_kit");
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

            EmitGoldChanged(countryId, oldGold, newGold);
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

            EmitGoldChanged(countryId, currentGold, newGold);
            return true;
        }

        /// <summary>
        /// Emit gold changed event via EventBus and C# event
        /// </summary>
        private void EmitGoldChanged(ushort countryId, int oldGold, int newGold)
        {
            // Emit via EventBus for all listeners
            gameState.EventBus.Emit(new GoldChangedEvent
            {
                CountryId = countryId,
                OldValue = oldGold,
                NewValue = newGold
            });

            // Also fire C# event for backward compatibility (player only)
            if (playerState != null && countryId == playerState.PlayerCountryId)
            {
                OnGoldChanged?.Invoke(oldGold, newGold);
            }
        }

        /// <summary>
        /// Get all countries with tracked gold (for ledger)
        /// </summary>
        public IEnumerable<ushort> GetCountriesWithGold()
        {
            return countryGold.Keys;
        }

        // ====================================================================
        // SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Serialize economy state to byte array
        /// </summary>
        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(countryGold.Count);
                foreach (var kvp in countryGold)
                {
                    writer.Write(kvp.Key);
                    writer.Write(kvp.Value);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize economy state from byte array
        /// </summary>
        public void Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                countryGold.Clear();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    ushort countryId = reader.ReadUInt16();
                    int gold = reader.ReadInt32();
                    countryGold[countryId] = gold;
                }

                if (logCollection)
                {
                    ArchonLogger.Log($"EconomySystem: Loaded gold for {count} countries", "starter_kit");
                }
            }
        }
    }
}
