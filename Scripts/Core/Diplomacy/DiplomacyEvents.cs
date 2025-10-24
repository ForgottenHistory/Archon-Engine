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
    public struct DiplomacyWarDeclaredEvent : IGameEvent
    {
        public readonly ushort attackerID;
        public readonly ushort defenderID;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public DiplomacyWarDeclaredEvent(ushort attacker, ushort defender, int tick)
        {
            this.attackerID = attacker;
            this.defenderID = defender;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when two countries make peace
    /// AI uses this to: recalculate threat levels, consider new targets
    /// UI uses this to: show notifications, update war list
    /// </summary>
    public struct DiplomacyPeaceMadeEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public DiplomacyPeaceMadeEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when opinion changes between two countries
    /// AI uses this to: detect improving/worsening relations, adjust diplomacy
    /// UI uses this to: update relations panel, show opinion trends
    /// </summary>
    public struct DiplomacyOpinionChangedEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly FixedPoint64 oldOpinion;
        public readonly FixedPoint64 newOpinion;
        public readonly ushort modifierType;  // What caused the change
        public readonly int tick;
        public float TimeStamp { get; set; }

        public DiplomacyOpinionChangedEvent(ushort c1, ushort c2, FixedPoint64 oldOp, FixedPoint64 newOp, ushort modType, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.oldOpinion = oldOp;
            this.newOpinion = newOp;
            this.modifierType = modType;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when DiplomacySystem initializes
    /// Used by UI and AI to know when diplomatic queries are available
    /// </summary>
    public struct DiplomacySystemInitializedEvent : IGameEvent
    {
        public readonly int countryCount;
        public readonly int relationshipCount;
        public float TimeStamp { get; set; }

        public DiplomacySystemInitializedEvent(int countries, int relationships)
        {
            this.countryCount = countries;
            this.relationshipCount = relationships;
            this.TimeStamp = 0f;
        }
    }

    // ========== TREATY EVENTS (Phase 2) ==========

    /// <summary>
    /// Emitted when an alliance is formed between two countries
    /// GAME layer uses this to add opinion bonuses, UI notifications
    /// </summary>
    public struct AllianceFormedEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public AllianceFormedEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when an alliance is broken
    /// GAME layer uses this to add opinion penalties, prestige loss
    /// </summary>
    public struct AllianceBrokenEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public AllianceBrokenEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when a non-aggression pact is formed
    /// </summary>
    public struct NonAggressionPactFormedEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public NonAggressionPactFormedEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when a non-aggression pact is broken
    /// GAME layer uses this to add opinion penalties
    /// </summary>
    public struct NonAggressionPactBrokenEvent : IGameEvent
    {
        public readonly ushort country1;
        public readonly ushort country2;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public NonAggressionPactBrokenEvent(ushort c1, ushort c2, int tick)
        {
            this.country1 = c1;
            this.country2 = c2;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when a country guarantees another's independence (directional)
    /// </summary>
    public struct GuaranteeGrantedEvent : IGameEvent
    {
        public readonly ushort guarantorID;
        public readonly ushort guaranteedID;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public GuaranteeGrantedEvent(ushort guarantor, ushort guaranteed, int tick)
        {
            this.guarantorID = guarantor;
            this.guaranteedID = guaranteed;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when a guarantee is revoked
    /// </summary>
    public struct GuaranteeRevokedEvent : IGameEvent
    {
        public readonly ushort guarantorID;
        public readonly ushort guaranteedID;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public GuaranteeRevokedEvent(ushort guarantor, ushort guaranteed, int tick)
        {
            this.guarantorID = guarantor;
            this.guaranteedID = guaranteed;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when military access is granted (directional)
    /// </summary>
    public struct MilitaryAccessGrantedEvent : IGameEvent
    {
        public readonly ushort granterID;
        public readonly ushort recipientID;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public MilitaryAccessGrantedEvent(ushort granter, ushort recipient, int tick)
        {
            this.granterID = granter;
            this.recipientID = recipient;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }

    /// <summary>
    /// Emitted when military access is revoked
    /// </summary>
    public struct MilitaryAccessRevokedEvent : IGameEvent
    {
        public readonly ushort granterID;
        public readonly ushort recipientID;
        public readonly int tick;
        public float TimeStamp { get; set; }

        public MilitaryAccessRevokedEvent(ushort granter, ushort recipient, int tick)
        {
            this.granterID = granter;
            this.recipientID = recipient;
            this.tick = tick;
            this.TimeStamp = 0f;
        }
    }
}
