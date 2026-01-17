using System;
using System.Collections.Generic;
using Core;
using Core.Events;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Tracks province ownership history.
    /// Demonstrates Pattern 4: Hot/Cold Data Separation.
    ///
    /// - Hot data: ProvinceState.ownerID (accessed every frame, 2 bytes)
    /// - Cold data: ProvinceHistoryData (accessed on-demand when viewing province)
    ///
    /// This system:
    /// - Subscribes to ProvinceOwnershipChangedEvent
    /// - Records ownership changes to cold storage
    /// - Provides on-demand access to history when UI needs it
    ///
    /// Memory is bounded via CircularBuffer (last N changes per province).
    /// </summary>
    public class ProvinceHistorySystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly bool logProgress;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Cold data storage - only created when province has history
        // Key: provinceId, Value: history data (lazy-loaded)
        private readonly Dictionary<ushort, ProvinceHistoryData> provinceHistories;

        public ProvinceHistorySystem(GameState gameStateRef, bool log = true)
        {
            gameState = gameStateRef;
            logProgress = log;
            provinceHistories = new Dictionary<ushort, ProvinceHistoryData>();

            // Subscribe to ownership changes
            subscriptions.Add(gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnOwnershipChanged));

            if (logProgress)
            {
                ArchonLogger.Log("ProvinceHistorySystem: Initialized (cold data storage for province history)", "starter_kit");
            }
        }

        private void OnOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            // Get or create history data for this province (lazy allocation)
            if (!provinceHistories.TryGetValue(evt.ProvinceId, out var historyData))
            {
                historyData = new ProvinceHistoryData(evt.ProvinceId);
                provinceHistories[evt.ProvinceId] = historyData;
            }

            // Get current game day from TimeManager
            int gameDay = GetCurrentGameDay();

            // Record the ownership change
            historyData.RecordOwnershipChange(evt.NewOwner, gameDay);

            if (logProgress)
            {
                string oldOwnerName = GetCountryName(evt.OldOwner);
                string newOwnerName = GetCountryName(evt.NewOwner);
                ArchonLogger.Log($"ProvinceHistorySystem: Province {evt.ProvinceId} changed from {oldOwnerName} to {newOwnerName}", "starter_kit");
            }
        }

        /// <summary>
        /// Get history data for a province (on-demand access).
        /// Returns null if no history recorded for this province.
        ///
        /// This is the key API demonstrating cold data access:
        /// - Only called when player clicks/views a province
        /// - NOT called every frame
        /// </summary>
        public ProvinceHistoryData GetProvinceHistory(ushort provinceId)
        {
            return provinceHistories.TryGetValue(provinceId, out var data) ? data : null;
        }

        /// <summary>
        /// Check if a province has any recorded history.
        /// </summary>
        public bool HasHistory(ushort provinceId)
        {
            return provinceHistories.ContainsKey(provinceId);
        }

        /// <summary>
        /// Get statistics about cold data storage (for debugging/monitoring).
        /// </summary>
        public (int provincesWithHistory, int totalRecords) GetStorageStats()
        {
            int totalRecords = 0;
            foreach (var kvp in provinceHistories)
            {
                totalRecords += kvp.Value.GetHistory().Count;
            }
            return (provinceHistories.Count, totalRecords);
        }

        private int GetCurrentGameDay()
        {
            // Try to get from TimeManager
            var timeManager = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            if (timeManager != null)
            {
                return timeManager.CurrentDay;
            }
            return 0;
        }

        private string GetCountryName(ushort countryId)
        {
            if (countryId == 0)
                return "Unowned";

            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem != null)
            {
                var coldData = countrySystem.GetCountryColdData(countryId);
                return coldData?.displayName ?? $"Country {countryId}";
            }
            return $"Country {countryId}";
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            subscriptions.Dispose();
            provinceHistories.Clear();

            if (logProgress)
            {
                ArchonLogger.Log("ProvinceHistorySystem: Disposed", "starter_kit");
            }
        }
    }
}
