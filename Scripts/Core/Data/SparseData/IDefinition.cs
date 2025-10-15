namespace Core.Data.SparseData
{
    /// <summary>
    /// Base interface for all definition types (buildings, modifiers, trade goods, etc.)
    ///
    /// Purpose: Enables generic definition loading and mod support
    /// - ID: Runtime-assigned unique identifier (assigned during loading)
    /// - StringID: Stable identifier for save/load and mod compatibility
    /// - Version: Definition version for compatibility checks
    ///
    /// Pattern:
    /// - Engine defines interface (mechanism)
    /// - Game implements specific definitions (policy)
    /// - Mods add definitions at runtime
    ///
    /// Example implementations:
    /// - BuildingDefinition (game-specific fields: cost, effects)
    /// - ModifierDefinition (game-specific fields: stat bonuses, duration)
    /// - TradeGoodDefinition (game-specific fields: base price, category)
    ///
    /// Mod compatibility:
    /// - StringID stable across saves (e.g., "farm")
    /// - ID assigned at runtime (may differ between sessions)
    /// - Version allows definition evolution
    /// </summary>
    public interface IDefinition
    {
        /// <summary>
        /// Runtime-assigned unique identifier
        /// - Assigned during definition loading (0-65535 range)
        /// - May differ between game sessions
        /// - Used for fast lookups in sparse collections
        /// </summary>
        ushort ID { get; set; }

        /// <summary>
        /// Stable string identifier for save/load compatibility
        /// - Unique across all definitions of this type
        /// - Never changes (mod compatibility)
        /// - Used in save files and mod references
        /// - Example: "farm", "gold_mine", "tax_modifier"
        /// </summary>
        string StringID { get; }

        /// <summary>
        /// Definition version for compatibility checks
        /// - Increment when definition format changes
        /// - Enables graceful handling of old saves
        /// - Allows mod compatibility warnings
        /// </summary>
        ushort Version { get; }
    }
}
