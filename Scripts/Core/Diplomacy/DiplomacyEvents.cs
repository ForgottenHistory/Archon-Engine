using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Diplomatic event definitions
    ///
    /// Architecture:
    /// - Structs for zero-allocation EventBus
    /// - Emitted by DiplomacySystem on state changes
    /// - GAME layer subscribes to trigger UI updates, notifications, AI reactions
    ///
    /// Pattern 3: Event-Driven Architecture (Zero-Allocation)
    /// - Events are structs (no boxing)
    /// - EventBus uses EventQueue<T> wrapper
    /// - Frame-coherent processing
    /// </summary>

    /// <summary>
    /// Emitted when a country declares war on another
    /// AI uses this to: form defensive coalitions, update war goals
    /// UI uses this to: show notifications, update relations panel
    /// </summary>
    public struct DiplomacyWarDeclaredEvent
    {
        public readonly ushort attackerID;
        public readonly ushort defenderID;
        public readonly int tick;

        public DiplomacyWarDeclaredEvent(ushort attacker, ushort defender, int tick)
        {
            this.attackerID = attacker;
            this.defenderID = defender;
            this.tick = tick;
        }
    }

    /// <summary>
    /// Emitted when two countries make peace
    /// AI uses this to: recalculate threat levels, consider new targets
    /// UI uses this to: show notifications, update war list
    /// </summary>
    public struct DiplomacyPeaceMadeEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;

        public DiplomacyPeaceMadeEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
        }
    }

    /// <summary>
    /// Emitted when opinion changes between two countries
    /// AI uses this to: detect improving/worsening relations, adjust diplomacy
    /// UI uses this to: update relations panel, show opinion trends
    /// </summary>
    public struct DiplomacyOpinionChangedEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly FixedPoint64 oldOpinion;
        public readonly FixedPoint64 newOpinion;
        public readonly ushort modifierType;  // What caused the change
        public readonly int tick;

        public DiplomacyOpinionChangedEvent(ushort c1, ushort c2, FixedPoint64 oldOp, FixedPoint64 newOp, ushort modType, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.oldOpinion = oldOp;
            this.newOpinion = newOp;
            this.modifierType = modType;
            this.tick = tick;
        }
    }

    /// <summary>
    /// Emitted when DiplomacySystem initializes
    /// Used by UI and AI to know when diplomatic queries are available
    /// </summary>
    public struct DiplomacySystemInitializedEvent
    {
        public readonly int countryCount;
        public readonly int relationshipCount;

        public DiplomacySystemInitializedEvent(int countries, int relationships)
        {
            this.countryCount = countries;
            this.relationshipCount = relationships;
        }
    }
}
