using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Core;
using Core.Data;
using Core.Events;
using Core.Modifiers;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// Simple economy for StarterKit.
    /// 1 gold per province + modifier bonuses, collected monthly.
    /// Tracks gold for ALL countries (for ledger display).
    ///
    /// Uses FixedPoint64 for deterministic calculations across all platforms.
    /// This ensures multiplayer sync - float operations produce different results
    /// on different CPUs, but FixedPoint64 is identical everywhere.
    ///
    /// Income Formula (per province):
    ///   baseIncome = 1 gold
    ///   localModified = baseIncome * (1 + LocalIncomeModifier)
    ///   finalIncome = localModified * (1 + CountryIncomeModifier)
    /// </summary>
    public class EconomySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly ModifierSystem modifierSystem;
        private readonly bool logCollection;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Gold storage for all countries (FixedPoint64 for determinism)
        private Dictionary<ushort, FixedPoint64> countryGold = new Dictionary<ushort, FixedPoint64>();

        // Income caching (Pattern 11: Dirty flag system)
        private const int MAX_COUNTRIES = 256;
        private readonly FixedPoint64[] cachedCountryIncome = new FixedPoint64[MAX_COUNTRIES];
        private readonly bool[] incomeNeedsRecalculation = new bool[MAX_COUNTRIES];

        /// <summary>
        /// Player's gold (convenience property, returns int for simple display)
        /// </summary>
        public int Gold => GetCountryGold(playerState?.PlayerCountryId ?? 0).ToInt();

        /// <summary>
        /// Player's gold as FixedPoint64 (for precise calculations)
        /// </summary>
        public FixedPoint64 GoldFixed => GetCountryGold(playerState?.PlayerCountryId ?? 0);

        /// <summary>
        /// Create a new EconomySystem.
        /// </summary>
        /// <param name="gameStateRef">Reference to the game state.</param>
        /// <param name="playerStateRef">Reference to player state for identifying player country.</param>
        /// <param name="modifierSystemRef">Modifier system for building bonuses.</param>
        /// <param name="log">Whether to log income collection.</param>
        public EconomySystem(GameState gameStateRef, PlayerState playerStateRef, ModifierSystem modifierSystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            modifierSystem = modifierSystemRef;
            logCollection = log;

            // Mark all income as needing recalculation
            InvalidateAllIncome();

            // Subscribe to monthly tick (token auto-disposed on Dispose)
            subscriptions.Add(gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick));

            // Subscribe to province ownership changes to invalidate income cache
            subscriptions.Add(gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnProvinceOwnershipChanged));

            // Subscribe to building construction to invalidate income cache
            subscriptions.Add(gameState.EventBus.Subscribe<BuildingConstructedEvent>(OnBuildingConstructed));

            if (logCollection)
            {
                ArchonLogger.Log("EconomySystem: Initialized with income caching", "starter_kit");
            }
        }

        private void OnBuildingConstructed(BuildingConstructedEvent evt)
        {
            // Invalidate income for the country that owns the building
            if (evt.CountryId > 0 && evt.CountryId < MAX_COUNTRIES)
                incomeNeedsRecalculation[evt.CountryId] = true;
        }

        private void OnProvinceOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            // Invalidate income for both old and new owner
            if (evt.OldOwner > 0 && evt.OldOwner < MAX_COUNTRIES)
                incomeNeedsRecalculation[evt.OldOwner] = true;
            if (evt.NewOwner > 0 && evt.NewOwner < MAX_COUNTRIES)
                incomeNeedsRecalculation[evt.NewOwner] = true;
        }

        /// <summary>
        /// Invalidate income cache for a specific country.
        /// Call when buildings change or modifiers are added/removed.
        /// </summary>
        public void InvalidateCountryIncome(ushort countryId)
        {
            if (countryId > 0 && countryId < MAX_COUNTRIES)
                incomeNeedsRecalculation[countryId] = true;
        }

        /// <summary>
        /// Invalidate all income caches.
        /// Call after load or major changes.
        /// </summary>
        public void InvalidateAllIncome()
        {
            for (int i = 0; i < MAX_COUNTRIES; i++)
                incomeNeedsRecalculation[i] = true;
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
            // Use cached income (recalculates only if dirty)
            FixedPoint64 income = GetCachedIncome(countryId);

            if (income > FixedPoint64.Zero)
            {
                FixedPoint64 oldGold = GetCountryGold(countryId);
                FixedPoint64 newGold = oldGold + income;
                countryGold[countryId] = newGold;

                // Emit gold changed event
                EmitGoldChanged(countryId, oldGold, newGold);

                if (logCollection && playerState != null && countryId == playerState.PlayerCountryId)
                {
                    int provinceCount = CountProvinces(countryId);
                    ArchonLogger.Log($"EconomySystem: Collected {income.ToFloat():F1} gold from {provinceCount} provinces (Total: {newGold.ToInt()})", "starter_kit");
                }
            }
        }

        /// <summary>
        /// Get cached income for a country, recalculating only if dirty.
        /// </summary>
        private FixedPoint64 GetCachedIncome(ushort countryId)
        {
            if (countryId == 0 || countryId >= MAX_COUNTRIES)
                return FixedPoint64.Zero;

            // Recalculate if dirty
            if (incomeNeedsRecalculation[countryId])
            {
                cachedCountryIncome[countryId] = CalculateCountryIncome(countryId);
                incomeNeedsRecalculation[countryId] = false;
            }

            return cachedCountryIncome[countryId];
        }

        /// <summary>
        /// Calculate total income for a country using ModifierSystem.
        /// Formula per province: (baseIncome + additiveBonus) * (1 + localModifier) * (1 + countryModifier)
        /// </summary>
        private FixedPoint64 CalculateCountryIncome(ushort countryId)
        {
            if (gameState?.ProvinceQueries == null)
                return FixedPoint64.Zero;

            FixedPoint64 totalIncome = FixedPoint64.Zero;
            FixedPoint64 baseIncomePerProvince = FixedPoint64.One; // 1 gold per province

            // Get country-wide income modifier (applies to all provinces)
            FixedPoint64 countryModifier = FixedPoint64.Zero;
            if (modifierSystem != null)
            {
                // Get the multiplicative modifier value for country income
                // ModifierSystem stores multiplicative values that get applied as (1 + modifier)
                var countryMod = modifierSystem.GetCountryModifier(
                    countryId,
                    (ushort)ModifierType.CountryIncomeModifier,
                    FixedPoint64.One);
                // GetCountryModifier returns base * (1 + modifier), so subtract 1 to get modifier value
                countryModifier = countryMod - FixedPoint64.One;
            }

            // Get all provinces owned by the country (must dispose NativeArray)
            var provinceIds = gameState.ProvinceQueries.GetCountryProvinces(countryId);
            try
            {
                foreach (var provinceId in provinceIds)
                {
                    // Start with base income
                    FixedPoint64 provinceIncome = baseIncomePerProvince;

                    if (modifierSystem != null)
                    {
                        // Add flat income bonus from buildings (additive modifier)
                        FixedPoint64 additiveBonus = modifierSystem.GetProvinceModifier(
                            provinceId,
                            countryId,
                            (ushort)ModifierType.LocalIncomeAdditive,
                            FixedPoint64.Zero);  // Base 0, returns just the additive value
                        provinceIncome = provinceIncome + additiveBonus;

                        // Apply local income modifier (multiplicative, from buildings in this province)
                        provinceIncome = modifierSystem.GetProvinceModifier(
                            provinceId,
                            countryId,
                            (ushort)ModifierType.LocalIncomeModifier,
                            provinceIncome);
                    }

                    // Apply country-wide modifier
                    provinceIncome = provinceIncome * (FixedPoint64.One + countryModifier);

                    totalIncome = totalIncome + provinceIncome;
                }
            }
            finally
            {
                provinceIds.Dispose();
            }

            return totalIncome;
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
        /// Get monthly income for a country (cached, using ModifierSystem)
        /// </summary>
        public FixedPoint64 GetMonthlyIncome(ushort countryId)
        {
            return GetCachedIncome(countryId);
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
