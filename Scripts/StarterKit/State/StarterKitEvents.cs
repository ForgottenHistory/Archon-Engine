using Core;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Events for UI updates and cross-system communication.
    /// Uses EventBus pattern (zero-allocation structs).
    /// </summary>

    /// <summary>
    /// Emitted when a country's gold changes.
    /// UI subscribes to refresh build buttons, resource displays, etc.
    /// </summary>
    public struct GoldChangedEvent : IGameEvent
    {
        /// <summary>The country whose gold changed.</summary>
        public ushort CountryId;

        /// <summary>Gold amount before the change.</summary>
        public int OldValue;

        /// <summary>Gold amount after the change.</summary>
        public int NewValue;

        /// <inheritdoc/>
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when a building is constructed.
    /// UI subscribes to refresh building lists, income displays, etc.
    /// </summary>
    public struct BuildingConstructedEvent : IGameEvent
    {
        /// <summary>The province where the building was constructed.</summary>
        public ushort ProvinceId;

        /// <summary>The type of building that was constructed.</summary>
        public ushort BuildingTypeId;

        /// <summary>The country that owns the building.</summary>
        public ushort CountryId;

        /// <inheritdoc/>
        public float TimeStamp { get; set; }
    }

    // NOTE: Unit events (UnitCreatedEvent, UnitDestroyedEvent, UnitMovedEvent) are defined
    // in Core.Units.UnitEvents - subscribe to those via EventBus directly.
}
