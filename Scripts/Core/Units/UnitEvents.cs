namespace Core.Units
{
    /// <summary>
    /// Emitted when a new unit is created.
    /// UI can subscribe to show notifications, update displays, etc.
    /// </summary>
    public struct UnitCreatedEvent : IGameEvent
    {
        public ushort UnitID;
        public ushort ProvinceID;
        public ushort CountryID;
        public ushort UnitTypeID;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when a unit is destroyed (disbanded, killed in combat, etc.)
    /// </summary>
    public struct UnitDestroyedEvent : IGameEvent
    {
        public ushort UnitID;
        public ushort ProvinceID;
        public ushort CountryID;
        public ushort UnitTypeID;
        public DestructionReason Reason;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when a unit moves to a new province (future - movement system)
    /// </summary>
    public struct UnitMovedEvent : IGameEvent
    {
        public ushort UnitID;
        public ushort OldProvinceID;
        public ushort NewProvinceID;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when unit strength changes (combat damage, reinforcement, etc.)
    /// </summary>
    public struct UnitStrengthChangedEvent : IGameEvent
    {
        public ushort UnitID;
        public byte OldStrength;
        public byte NewStrength;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when unit morale changes (combat, attrition, etc.)
    /// </summary>
    public struct UnitMoraleChangedEvent : IGameEvent
    {
        public ushort UnitID;
        public byte OldMorale;
        public byte NewMorale;
        public float TimeStamp { get; set; }
    }

    /// <summary>Reason for unit destruction (for statistics, events, etc.)</summary>
    public enum DestructionReason
    {
        Disbanded,      // Player manually disbanded
        Combat,         // Destroyed in battle
        Attrition,      // Starved/lost to terrain
        Merged,         // Merged into another unit
        Event           // Scripted event
    }
}
