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
        public ushort CountryId;
        public int OldValue;
        public int NewValue;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when a building is constructed.
    /// UI subscribes to refresh building lists, income displays, etc.
    /// </summary>
    public struct BuildingConstructedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public ushort BuildingTypeId;
        public ushort CountryId;
        public float TimeStamp { get; set; }
    }
}
