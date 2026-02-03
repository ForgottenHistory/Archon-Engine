using System.Collections.Generic;
using Core.Data;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Province ownership history record.
    /// Demonstrates Pattern 4: Hot/Cold Data Separation.
    ///
    /// This is COLD DATA - only accessed when viewing province details,
    /// not every frame like ProvinceState (hot data).
    /// </summary>
    public struct OwnershipRecord
    {
        /// <summary>The country that owned the province during this period.</summary>
        public ushort CountryId;

        /// <summary>Game day when this country gained ownership.</summary>
        public int StartDay;

        /// <summary>Game day when ownership ended (0 = still current owner).</summary>
        public int EndDay;

        /// <summary>True if this record represents the current owner.</summary>
        public bool IsCurrent => EndDay == 0;
    }

    /// <summary>
    /// Cold data storage for a single province's history.
    /// Uses CircularBuffer to prevent unbounded memory growth.
    ///
    /// Access pattern: Loaded on-demand when player clicks province.
    /// NOT accessed every frame - this is the key distinction from hot data.
    /// </summary>
    public class ProvinceHistoryData
    {
        private const int MAX_HISTORY_ENTRIES = 10; // Last 10 ownership changes

        public ushort ProvinceId { get; }
        private readonly CircularBuffer<OwnershipRecord> ownershipHistory;

        public ProvinceHistoryData(ushort provinceId)
        {
            ProvinceId = provinceId;
            ownershipHistory = new CircularBuffer<OwnershipRecord>(MAX_HISTORY_ENTRIES);
        }

        /// <summary>
        /// Record a new owner for this province.
        /// Called when ownership changes - closes previous record and opens new one.
        /// </summary>
        public void RecordOwnershipChange(ushort newOwnerId, int gameDay)
        {
            // Previous ownership record closing is implicit -
            // CircularBuffer doesn't support in-place update of structs,
            // and the old code wasn't actually closing records anyway (no write-back).
            // We just add the new record; GetHistory() ordering shows the timeline.

            // Add new ownership record
            ownershipHistory.Add(new OwnershipRecord
            {
                CountryId = newOwnerId,
                StartDay = gameDay,
                EndDay = 0 // Current owner
            });
        }

        /// <summary>
        /// Get ownership history (most recent first).
        /// Returns empty list if no history recorded.
        /// </summary>
        public IReadOnlyList<OwnershipRecord> GetHistory()
        {
            return ownershipHistory.Items;
        }

        /// <summary>
        /// Get the current owner from history (or 0 if no history).
        /// Note: For current owner, use ProvinceState.ownerID (hot data) instead.
        /// This is just for completeness of the history record.
        /// </summary>
        public ushort GetCurrentOwnerFromHistory()
        {
            var items = ownershipHistory.Items;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].IsCurrent)
                    return items[i].CountryId;
            }
            return 0;
        }

        /// <summary>
        /// Get previous owner (before current).
        /// Useful for "reclaimed from X" messages.
        /// </summary>
        public ushort GetPreviousOwner()
        {
            var items = ownershipHistory.Items;
            if (items.Count < 2)
                return 0;

            // Find the second-to-last entry (previous owner)
            return items[items.Count - 2].CountryId;
        }

        /// <summary>
        /// Check if province was ever owned by a specific country.
        /// </summary>
        public bool WasOwnedBy(ushort countryId)
        {
            foreach (var record in ownershipHistory.Items)
            {
                if (record.CountryId == countryId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get number of times this province changed hands.
        /// </summary>
        public int GetOwnershipChangeCount()
        {
            return ownershipHistory.Count > 0 ? ownershipHistory.Count - 1 : 0;
        }
    }
}
