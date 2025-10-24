using System.IO;
using Core.Commands;
using Core.Systems;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Command to form alliance between two countries
    ///
    /// Validation:
    /// - Both countries exist
    /// - Not already allied
    /// - Not at war
    ///
    /// Execution:
    /// - Set Alliance bit in DiplomacySystem
    /// - Emit AllianceFormedEvent
    /// </summary>
    public class FormAllianceCommand : BaseCommand
    {
        public ushort Country1 { get; set; }
        public ushort Country2 { get; set; }

        public override int Priority => 80;  // High priority for diplomatic actions

        public override bool Validate(GameState gameState)
        {
            // Check if countries exist
            if (!ValidateCountryId(gameState, Country1))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormAllianceCommand: Invalid country ID {Country1}");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormAllianceCommand: Invalid country ID {Country2}");
                return false;
            }

            // Cannot ally with self
            if (Country1 == Country2)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormAllianceCommand: Cannot ally with self ({Country1})");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            // Check if already allied
            if (diplomacy.AreAllied(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormAllianceCommand: Already allied ({Country1} and {Country2})");
                return false;
            }

            // Cannot ally during war
            if (diplomacy.IsAtWar(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormAllianceCommand: Cannot ally during war ({Country1} vs {Country2})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Forming alliance between {Country1} and {Country2}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.FormAlliance(Country1, Country2, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("FormAllianceCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Country1);
            writer.Write(Country2);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Country1 = reader.ReadUInt16();
            Country2 = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to break alliance between two countries
    /// </summary>
    public class BreakAllianceCommand : BaseCommand
    {
        public ushort Country1 { get; set; }
        public ushort Country2 { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakAllianceCommand: Invalid country ID {Country1}");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakAllianceCommand: Invalid country ID {Country2}");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            // Check if actually allied
            if (!diplomacy.AreAllied(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakAllianceCommand: Not allied ({Country1} and {Country2})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Breaking alliance between {Country1} and {Country2}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.BreakAlliance(Country1, Country2, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("BreakAllianceCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Country1);
            writer.Write(Country2);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Country1 = reader.ReadUInt16();
            Country2 = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to form non-aggression pact
    /// </summary>
    public class FormNonAggressionPactCommand : BaseCommand
    {
        public ushort Country1 { get; set; }
        public ushort Country2 { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormNonAggressionPactCommand: Invalid country ID {Country1}");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormNonAggressionPactCommand: Invalid country ID {Country2}");
                return false;
            }

            if (Country1 == Country2)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormNonAggressionPactCommand: Cannot form NAP with self ({Country1})");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.HasNonAggressionPact(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"FormNonAggressionPactCommand: NAP already exists ({Country1} and {Country2})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Forming non-aggression pact between {Country1} and {Country2}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.FormNonAggressionPact(Country1, Country2, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("FormNonAggressionPactCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Country1);
            writer.Write(Country2);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Country1 = reader.ReadUInt16();
            Country2 = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to break non-aggression pact
    /// </summary>
    public class BreakNonAggressionPactCommand : BaseCommand
    {
        public ushort Country1 { get; set; }
        public ushort Country2 { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakNonAggressionPactCommand: Invalid country ID {Country1}");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakNonAggressionPactCommand: Invalid country ID {Country2}");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.HasNonAggressionPact(Country1, Country2))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"BreakNonAggressionPactCommand: NAP does not exist ({Country1} and {Country2})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Breaking non-aggression pact between {Country1} and {Country2}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.BreakNonAggressionPact(Country1, Country2, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("BreakNonAggressionPactCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(Country1);
            writer.Write(Country2);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Country1 = reader.ReadUInt16();
            Country2 = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to guarantee another country's independence (directional)
    /// </summary>
    public class GuaranteeIndependenceCommand : BaseCommand
    {
        public ushort GuarantorID { get; set; }
        public ushort GuaranteedID { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GuarantorID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GuaranteeIndependenceCommand: Invalid guarantor ID {GuarantorID}");
                return false;
            }

            if (!ValidateCountryId(gameState, GuaranteedID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GuaranteeIndependenceCommand: Invalid guaranteed ID {GuaranteedID}");
                return false;
            }

            if (GuarantorID == GuaranteedID)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GuaranteeIndependenceCommand: Cannot guarantee self ({GuarantorID})");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.IsGuaranteeing(GuarantorID, GuaranteedID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GuaranteeIndependenceCommand: Already guaranteeing ({GuarantorID} → {GuaranteedID})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"{GuarantorID} guaranteeing {GuaranteedID}'s independence");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.GuaranteeIndependence(GuarantorID, GuaranteedID, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("GuaranteeIndependenceCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(GuarantorID);
            writer.Write(GuaranteedID);
        }

        public override void Deserialize(BinaryReader reader)
        {
            GuarantorID = reader.ReadUInt16();
            GuaranteedID = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to revoke guarantee of independence
    /// </summary>
    public class RevokeGuaranteeCommand : BaseCommand
    {
        public ushort GuarantorID { get; set; }
        public ushort GuaranteedID { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GuarantorID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeGuaranteeCommand: Invalid guarantor ID {GuarantorID}");
                return false;
            }

            if (!ValidateCountryId(gameState, GuaranteedID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeGuaranteeCommand: Invalid guaranteed ID {GuaranteedID}");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.IsGuaranteeing(GuarantorID, GuaranteedID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeGuaranteeCommand: Not guaranteeing ({GuarantorID} → {GuaranteedID})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"{GuarantorID} revoking guarantee of {GuaranteedID}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.RevokeGuarantee(GuarantorID, GuaranteedID, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("RevokeGuaranteeCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(GuarantorID);
            writer.Write(GuaranteedID);
        }

        public override void Deserialize(BinaryReader reader)
        {
            GuarantorID = reader.ReadUInt16();
            GuaranteedID = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to grant military access (directional)
    /// </summary>
    public class GrantMilitaryAccessCommand : BaseCommand
    {
        public ushort GranterID { get; set; }
        public ushort RecipientID { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GranterID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GrantMilitaryAccessCommand: Invalid granter ID {GranterID}");
                return false;
            }

            if (!ValidateCountryId(gameState, RecipientID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GrantMilitaryAccessCommand: Invalid recipient ID {RecipientID}");
                return false;
            }

            if (GranterID == RecipientID)
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GrantMilitaryAccessCommand: Cannot grant access to self ({GranterID})");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.HasMilitaryAccess(GranterID, RecipientID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"GrantMilitaryAccessCommand: Access already granted ({GranterID} → {RecipientID})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"{GranterID} granting military access to {RecipientID}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.GrantMilitaryAccess(GranterID, RecipientID, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("GrantMilitaryAccessCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(GranterID);
            writer.Write(RecipientID);
        }

        public override void Deserialize(BinaryReader reader)
        {
            GranterID = reader.ReadUInt16();
            RecipientID = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// ENGINE LAYER - Command to revoke military access
    /// </summary>
    public class RevokeMilitaryAccessCommand : BaseCommand
    {
        public ushort GranterID { get; set; }
        public ushort RecipientID { get; set; }

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GranterID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeMilitaryAccessCommand: Invalid granter ID {GranterID}");
                return false;
            }

            if (!ValidateCountryId(gameState, RecipientID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeMilitaryAccessCommand: Invalid recipient ID {RecipientID}");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.HasMilitaryAccess(GranterID, RecipientID))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"RevokeMilitaryAccessCommand: Access not granted ({GranterID} → {RecipientID})");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"{GranterID} revoking military access from {RecipientID}");

            var diplomacy = gameState.GetComponent<DiplomacySystem>();
            var time = gameState.GetComponent<TimeManager>();
            int currentTick = (int)time.CurrentTick;

            diplomacy.RevokeMilitaryAccess(GranterID, RecipientID, currentTick);
        }

        public override void Undo(GameState gameState)
        {
            ArchonLogger.LogCoreDiplomacyWarning("RevokeMilitaryAccessCommand: Undo not supported");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(GranterID);
            writer.Write(RecipientID);
        }

        public override void Deserialize(BinaryReader reader)
        {
            GranterID = reader.ReadUInt16();
            RecipientID = reader.ReadUInt16();
        }
    }
}
