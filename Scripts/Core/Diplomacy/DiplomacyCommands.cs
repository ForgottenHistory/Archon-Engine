using System.IO;
using Core.Commands;
using Core.Data;
using Core.Systems;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Command to declare war between two countries
    ///
    /// Validation:
    /// - Both countries exist
    /// - Not already at war
    /// - Not the same country
    ///
    /// Execution:
    /// - Set war state in DiplomacySystem
    /// - Add "Declared War" opinion modifier
    /// - Emit DiplomacyWarDeclaredEvent
    /// </summary>
    public class DeclareWarCommand : BaseCommand
    {
        public ushort AttackerID { get; set; }
        public ushort DefenderID { get; set; }

        // Optional: modifier type and values (set by factory/UI)
        public ushort DeclaredWarModifierType { get; set; } = 1;  // Default to type 1
        public FixedPoint64 DeclaredWarModifierValue { get; set; } = FixedPoint64.FromInt(-50);
        public int DeclaredWarDecayTicks { get; set; } = 3600;  // 10 years

        public override int Priority => 90;  // High priority for war declarations

        public override bool Validate(GameState gameState)
        {
            // Check if countries exist
            if (!ValidateCountryId(gameState, AttackerID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Invalid attacker ID {AttackerID}");
                return false;
            }

            if (!ValidateCountryId(gameState, DefenderID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Invalid defender ID {DefenderID}");
                return false;
            }

            // Cannot declare war on self
            if (AttackerID == DefenderID)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Cannot declare war on self ({AttackerID})");
                return false;
            }

            // Check if already at war
            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            if (diplomacy.IsAtWar(AttackerID, DefenderID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Already at war ({AttackerID} vs {DefenderID})");
                return false;
            }

            // Phase 2: Check for Non-Aggression Pact
            if (diplomacy.HasNonAggressionPact(AttackerID, DefenderID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Cannot declare war - Non-Aggression Pact exists ({AttackerID} vs {DefenderID})");
                return false;
            }

            // Phase 2: Check for Alliance (cannot attack allies)
            if (diplomacy.AreAllied(AttackerID, DefenderID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DeclareWarCommand: Cannot declare war - countries are allied ({AttackerID} and {DefenderID})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Country {AttackerID} declaring war on {DefenderID}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            // Declare war
            diplomacy.DeclareWar(AttackerID, DefenderID, currentTick);

            // Add "Declared War" opinion modifier
            var modifier = new OpinionModifier
            {
                modifierTypeID = DeclaredWarModifierType,
                value = DeclaredWarModifierValue,
                appliedTick = currentTick,
                decayRate = DeclaredWarDecayTicks
            };

            diplomacy.AddOpinionModifier(AttackerID, DefenderID, modifier, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            // Undo not implemented for diplomacy commands
            // War declarations have lasting consequences
            ArchonLogger.LogCoreDiplomacyWarning("DeclareWarCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(AttackerID);
            writer.Write(DefenderID);
            writer.Write(DeclaredWarModifierType);
            writer.Write(DeclaredWarModifierValue.RawValue);
            writer.Write(DeclaredWarDecayTicks);
        }

        public override void Deserialize(BinaryReader reader)
        {
            AttackerID = reader.ReadUInt16();
            DefenderID = reader.ReadUInt16();
            DeclaredWarModifierType = reader.ReadUInt16();
            DeclaredWarModifierValue = FixedPoint64.FromRaw(reader.ReadInt64());
            DeclaredWarDecayTicks = reader.ReadInt32();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to make peace between two countries
    ///
    /// Validation:
    /// - Both countries exist
    /// - Currently at war
    ///
    /// Execution:
    /// - Remove war state in DiplomacySystem
    /// - Add "Made Peace" opinion modifier
    /// - Emit DiplomacyPeaceMadeEvent
    /// </summary>
    public class MakePeaceCommand : BaseCommand
    {
        public ushort Country1 { get; set; }
        public ushort Country2 { get; set; }

        // Optional: modifier type and values
        public ushort MadePeaceModifierType { get; set; } = 2;  // Default to type 2
        public FixedPoint64 MadePeaceModifierValue { get; set; } = FixedPoint64.FromInt(10);
        public int MadePeaceDecayTicks { get; set; } = 720;  // 2 years

        public override int Priority => 90;  // High priority for peace

        public override bool Validate(GameState gameState)
        {
            // Check if countries exist
            if (!ValidateCountryId(gameState, Country1))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"MakePeaceCommand: Invalid country ID {Country1}");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"MakePeaceCommand: Invalid country ID {Country2}");
                return false;
            }

            // Check if at war
            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            if (!diplomacy.IsAtWar(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"MakePeaceCommand: Not at war ({Country1} vs {Country2})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Making peace between {Country1} and {Country2}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            // Make peace
            diplomacy.MakePeace(Country1, Country2, currentTick);

            // Add "Made Peace" opinion modifier
            var modifier = new OpinionModifier
            {
                modifierTypeID = MadePeaceModifierType,
                value = MadePeaceModifierValue,
                appliedTick = currentTick,
                decayRate = MadePeaceDecayTicks
            };

            diplomacy.AddOpinionModifier(Country1, Country2, modifier, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            // Undo not implemented for diplomacy commands
            ArchonLogger.LogCoreDiplomacyWarning("MakePeaceCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Country1);
            writer.Write(Country2);
            writer.Write(MadePeaceModifierType);
            writer.Write(MadePeaceModifierValue.RawValue);
            writer.Write(MadePeaceDecayTicks);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Country1 = reader.ReadUInt16();
            Country2 = reader.ReadUInt16();
            MadePeaceModifierType = reader.ReadUInt16();
            MadePeaceModifierValue = FixedPoint64.FromRaw(reader.ReadInt64());
            MadePeaceDecayTicks = reader.ReadInt32();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to improve relations between two countries
    ///
    /// Architecture:
    /// - ENGINE: Generic mechanism using ushort resourceId (resource-agnostic)
    /// - GAME: Policy layer sets which resource (gold) via factory: (ushort)ResourceType.Gold
    ///
    /// Validation:
    /// - Both countries exist
    /// - Not at war
    /// - Source country has enough of the specified resource (if cost > 0)
    ///
    /// Execution:
    /// - Deduct resource cost (if specified)
    /// - Add "Improved Relations" opinion modifier
    /// - Emit DiplomacyOpinionChangedEvent
    /// </summary>
    public class ImproveRelationsCommand : BaseCommand
    {
        public ushort SourceCountry { get; set; }
        public ushort TargetCountry { get; set; }

        // Resource cost (optional, set by GAME layer factory)
        public ushort ResourceId { get; set; } = 0;  // Default: gold (GAME layer sets this)
        public FixedPoint64 ResourceCost { get; set; } = FixedPoint64.Zero;  // Default: free

        // Opinion modifier parameters (set by GAME layer factory)
        public ushort ImproveRelationsModifierType { get; set; } = 3;  // Default to type 3
        public FixedPoint64 ImproveRelationsModifierValue { get; set; } = FixedPoint64.FromInt(5);
        public int ImproveRelationsDecayTicks { get; set; } = 360;  // 1 year

        public override int Priority => 50;  // Medium priority

        public override bool Validate(GameState gameState)
        {
            // Check if countries exist
            if (!ValidateCountryId(gameState, SourceCountry))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"ImproveRelationsCommand: Invalid source country {SourceCountry}");
                return false;
            }

            if (!ValidateCountryId(gameState, TargetCountry))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"ImproveRelationsCommand: Invalid target country {TargetCountry}");
                return false;
            }

            // Cannot improve relations with self
            if (SourceCountry == TargetCountry)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"ImproveRelationsCommand: Cannot improve relations with self ({SourceCountry})");
                return false;
            }

            // Check if at war (cannot improve relations during war)
            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            if (diplomacy.IsAtWar(SourceCountry, TargetCountry))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"ImproveRelationsCommand: Cannot improve relations during war ({SourceCountry} vs {TargetCountry})");
                return false;
            }

            // Check if source has enough resources (if cost is specified)
            if (ResourceCost > FixedPoint64.Zero)
            {
                var resources = gameState.Resources;
                FixedPoint64 currentAmount = resources.GetResource(SourceCountry, ResourceId);
                if (currentAmount < ResourceCost)
                {
                    ArchonLogger.LogCoreDiplomacyWarning($"ImproveRelationsCommand: Insufficient resources ({currentAmount} < {ResourceCost})");
                    return false;
                }
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Country {SourceCountry} improving relations with {TargetCountry} (cost: {ResourceCost})");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            // Deduct resource cost (if specified)
            if (ResourceCost > FixedPoint64.Zero)
            {
                // ResourceSystem is registered with GameState, not a component
                var resources = gameState.Resources;
                resources.RemoveResource(SourceCountry, ResourceId, ResourceCost);
            }

            // Add "Improved Relations" opinion modifier
            var modifier = new OpinionModifier
            {
                modifierTypeID = ImproveRelationsModifierType,
                value = ImproveRelationsModifierValue,
                appliedTick = currentTick,
                decayRate = ImproveRelationsDecayTicks
            };

            diplomacy.AddOpinionModifier(SourceCountry, TargetCountry, modifier, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            // Undo not implemented for diplomacy commands
            ArchonLogger.LogCoreDiplomacyWarning("ImproveRelationsCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(SourceCountry);
            writer.Write(TargetCountry);
            writer.Write(ResourceId);
            writer.Write(ResourceCost.RawValue);
            writer.Write(ImproveRelationsModifierType);
            writer.Write(ImproveRelationsModifierValue.RawValue);
            writer.Write(ImproveRelationsDecayTicks);
        }

        public override void Deserialize(BinaryReader reader)
        {
            SourceCountry = reader.ReadUInt16();
            TargetCountry = reader.ReadUInt16();
            ResourceId = reader.ReadUInt16();
            ResourceCost = FixedPoint64.FromRaw(reader.ReadInt64());
            ImproveRelationsModifierType = reader.ReadUInt16();
            ImproveRelationsModifierValue = FixedPoint64.FromRaw(reader.ReadInt64());
            ImproveRelationsDecayTicks = reader.ReadInt32();
        }
    }
}
