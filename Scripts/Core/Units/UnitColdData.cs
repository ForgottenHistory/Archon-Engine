using System;
using System.Collections.Generic;

namespace Core.Units
{
    /// <summary>
    /// Cold data for units - rarely accessed, stored separately from hot data.
    ///
    /// DESIGN:
    /// - NOT in NativeArray (uses managed types)
    /// - Loaded on-demand (Dictionary lookup)
    /// - Stores optional/rare data (custom names, history, etc.)
    ///
    /// EXAMPLES:
    /// - Custom unit names ("The Old Guard")
    /// - Combat history (battles participated in)
    /// - Achievement tracking (most kills, longest march, etc.)
    /// </summary>
    public class UnitColdData
    {
        /// <summary>Custom name (e.g., "The Old Guard", "Royal Dragoons")</summary>
        public string CustomName { get; set; }

        /// <summary>Creation tick (for age tracking, veteran status, etc.)</summary>
        public ulong CreationTick { get; set; }

        /// <summary>Total kills in combat (for statistics, achievements)</summary>
        public int TotalKills { get; set; }

        /// <summary>Battles participated in (for veteran status)</summary>
        public int BattlesCount { get; set; }

        /// <summary>Total provinces traveled (for logistics/attrition tracking)</summary>
        public int ProvincesMarched { get; set; }

        /// <summary>
        /// Recent combat history (limited size to prevent unbounded growth).
        /// Stores recent battle IDs or province IDs where combat occurred.
        /// </summary>
        public List<ushort> RecentCombatHistory { get; set; }

        public UnitColdData()
        {
            CustomName = null;
            CreationTick = 0;
            TotalKills = 0;
            BattlesCount = 0;
            ProvincesMarched = 0;
            RecentCombatHistory = new List<ushort>();
        }

        /// <summary>Is this unit a veteran (fought multiple battles)?</summary>
        public bool IsVeteran => BattlesCount >= 5;

        /// <summary>Add a battle to combat history (with bounded size)</summary>
        public void RecordBattle(ushort battleProvinceID)
        {
            BattlesCount++;
            RecentCombatHistory.Add(battleProvinceID);

            // Keep only last 10 battles to prevent unbounded growth
            if (RecentCombatHistory.Count > 10)
            {
                RecentCombatHistory.RemoveAt(0);
            }
        }
    }
}
