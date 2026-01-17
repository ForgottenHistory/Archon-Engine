using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Core;
using Core.Data;
using Core.Events;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple economy for StarterKit.
    /// 1 gold per province + building bonuses, collected monthly.
    /// Tracks gold for ALL countries (for ledger display).
    ///
    /// Uses FixedPoint64 for deterministic calculations across all platforms.
    /// This ensures multiplayer sync - float operations produce different results
    /// on different CPUs, but FixedPoint64 is identical everywhere.
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly bool logCollection;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Gold storage for all countries (FixedPoint64 for determinism)
        private Dictionary<ushort, FixedPoint64> countryGold = new Dictionary<ushort, FixedPoint64>();

        // Optional building system for bonus calculation
        private BuildingSystem buildingSystem;

        /// <summary>
        /// Player's gold (convenience property, returns int for simple display)
        /// </summary>
        public int Gold => GetCountryGold(playerState?.PlayerCountryId ?? 0).ToInt();

        /// <summary>
        /// Player's gold as FixedPoint64 (for precise calculations)
        /// </summary>
        public FixedPoint64 GoldFixed => GetCountryGold(playerState?.PlayerCountryId ?? 0);


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
            // Use FixedPoint64 for all calculations - deterministic across platforms
            FixedPoint64 baseIncome = FixedPoint64.FromInt(provinceCount); // 1 gold per province
            FixedPoint64 buildingBonus = CalculateBuildingBonus(countryId);
            FixedPoint64 income = baseIncome + buildingBonus;

            if (income > FixedPoint64.Zero)
            {
                FixedPoint64 oldGold = GetCountryGold(countryId);
                FixedPoint64 newGold = oldGold + income;
                countryGold[countryId] = newGold;

                // Emit gold changed event
                EmitGoldChanged(countryId, oldGold, newGold);

                if (logCollection && playerState != null && countryId == playerState.PlayerCountryId)
                {
                    ArchonLogger.Log($"EconomySystem: Collected {income.ToInt()} gold ({baseIncome.ToInt()} base + {buildingBonus.ToInt()} buildings) from {provinceCount} provinces (Total: {newGold.ToInt()})", "starter_kit");
                }
            }
        }

        private FixedPoint64 CalculateBuildingBonus(ushort countryId)
        {
            if (buildingSystem == null || gameState?.ProvinceQueries == null)
                return FixedPoint64.Zero;

            FixedPoint64 totalBonus = FixedPoint64.Zero;

            // Get all provinces owned by the country (must dispose NativeArray)
            var provinceIds = gameState.ProvinceQueries.GetCountryProvinces(countryId);
            try
            {
                foreach (var provinceId in provinceIds)
                {
                    // Convert int bonus to FixedPoint64
                    totalBonus = totalBonus + FixedPoint64.FromInt(buildingSystem.GetProvinceGoldBonus(provinceId));
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
        /// Get gold for a specific country as FixedPoint64.
        /// </summary>
        public FixedPoint64 GetCountryGold(ushort countryId)
        {
            if (countryId == 0) return FixedPoint64.Zero;
            return countryGold.TryGetValue(countryId, out FixedPoint64 gold) ? gold : FixedPoint64.Zero;
        }

        /// <summary>
        /// Get gold for a specific country as int (for simple display).
        /// </summary>
        public int GetCountryGoldInt(ushort countryId)
        {
            return GetCountryGold(countryId).ToInt();
        }

        /// <summary>
        /// Get monthly income for a country (province count + building bonuses)
        /// </summary>
        public FixedPoint64 GetMonthlyIncome(ushort countryId)
        {
            return FixedPoint64.FromInt(CountProvinces(countryId)) + CalculateBuildingBonus(countryId);
        }

        /// <summary>
        /// Get monthly income for a country as int (for simple display)
        /// </summary>
        public int GetMonthlyIncomeInt(ushort countryId)
        {
            return GetMonthlyIncome(countryId).ToInt();
        }

        /// <summary>
        /// Get monthly income for player (convenience)
        /// </summary>
        public int GetMonthlyIncomeInt()
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return 0;

            return GetMonthlyIncomeInt(playerState.PlayerCountryId);
        }

        /// <summary>
        /// Add gold to player (for commands/cheats)
        /// </summary>
        public void AddGold(int amount)
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return;

            AddGoldToCountry(playerState.PlayerCountryId, FixedPoint64.FromInt(amount));
        }

        /// <summary>
        /// Add gold to a specific country (FixedPoint64 version for precise calculations)
        /// </summary>
        public void AddGoldToCountry(ushort countryId, FixedPoint64 amount)
        {
            FixedPoint64 oldGold = GetCountryGold(countryId);
            FixedPoint64 newGold = oldGold + amount;
            countryGold[countryId] = newGold;

            EmitGoldChanged(countryId, oldGold, newGold);
        }

        /// <summary>
        /// Add gold to a specific country (int version for simple cases)
        /// </summary>
        public void AddGoldToCountry(ushort countryId, int amount)
        {
            AddGoldToCountry(countryId, FixedPoint64.FromInt(amount));
        }

        /// <summary>
        /// Remove gold from player (returns false if insufficient)
        /// </summary>
        public bool RemoveGold(int amount)
        {
            if (playerState == null || !playerState.HasPlayerCountry)
                return false;

            return RemoveGoldFromCountry(playerState.PlayerCountryId, FixedPoint64.FromInt(amount));
        }

        /// <summary>
        /// Remove gold from a specific country (returns false if insufficient)
        /// </summary>
        public bool RemoveGoldFromCountry(ushort countryId, FixedPoint64 amount)
        {
            FixedPoint64 currentGold = GetCountryGold(countryId);
            if (currentGold < amount)
                return false;

            FixedPoint64 newGold = currentGold - amount;
            countryGold[countryId] = newGold;

            EmitGoldChanged(countryId, currentGold, newGold);
            return true;
        }

        /// <summary>
        /// Remove gold from a specific country (int version for simple cases)
        /// </summary>
        public bool RemoveGoldFromCountry(ushort countryId, int amount)
        {
            return RemoveGoldFromCountry(countryId, FixedPoint64.FromInt(amount));
        }

        /// <summary>
        /// Emit gold changed event via EventBus (Pattern 3: Event-Driven Architecture)
        /// </summary>
        private void EmitGoldChanged(ushort countryId, FixedPoint64 oldGold, FixedPoint64 newGold)
        {
            gameState.EventBus.Emit(new GoldChangedEvent
            {
                CountryId = countryId,
                OldValue = oldGold.ToInt(),
                NewValue = newGold.ToInt()
            });
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
        /// Serialize economy state to byte array.
        /// FixedPoint64 uses its RawValue (long) for deterministic serialization.
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
                    // Serialize FixedPoint64 as raw long value for determinism
                    writer.Write(kvp.Value.RawValue);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize economy state from byte array.
        /// Restores FixedPoint64 from raw long values.
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
                    // Deserialize FixedPoint64 from raw long value
                    long rawGold = reader.ReadInt64();
                    countryGold[countryId] = FixedPoint64.FromRaw(rawGold);
                }

                if (logCollection)
                {
                    ArchonLogger.Log($"EconomySystem: Loaded gold for {count} countries", "starter_kit");
                }
            }
        }
    }
}
