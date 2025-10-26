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

        private string lastValidationError = null;

        public override int Priority => 80;  // High priority for diplomatic actions

        public override bool Validate(GameState gameState)
        {
            // Check if countries exist
            if (!ValidateCountryId(gameState, Country1))
            {
                lastValidationError = $"Invalid country ID: {Country1}";
                ArchonLogger.LogWarning($"FormAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                lastValidationError = $"Invalid country ID: {Country2}";
                ArchonLogger.LogWarning($"FormAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            // Cannot ally with self
            if (Country1 == Country2)
            {
                lastValidationError = $"Cannot form alliance with self (Country {Country1})";
                ArchonLogger.LogWarning($"FormAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            // Check if already allied
            if (diplomacy.AreAllied(Country1, Country2))
            {
                lastValidationError = $"Already allied (Countries {Country1} and {Country2})";
                ArchonLogger.LogWarning($"FormAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            // Cannot ally during war
            if (diplomacy.IsAtWar(Country1, Country2))
            {
                lastValidationError = $"Cannot form alliance during war (Countries {Country1} vs {Country2})";
                ArchonLogger.LogWarning($"FormAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Alliance formation failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Alliance formed between Countries {Country1} and {Country2}";
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
            ArchonLogger.LogWarning("FormAllianceCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                lastValidationError = $"Invalid country ID: {Country1}";
                ArchonLogger.LogWarning($"BreakAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                lastValidationError = $"Invalid country ID: {Country2}";
                ArchonLogger.LogWarning($"BreakAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            // Check if actually allied
            if (!diplomacy.AreAllied(Country1, Country2))
            {
                lastValidationError = $"Not allied (Countries {Country1} and {Country2})";
                ArchonLogger.LogWarning($"BreakAllianceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Alliance breaking failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Alliance broken between Countries {Country1} and {Country2}";
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
            ArchonLogger.LogWarning("BreakAllianceCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                lastValidationError = $"Invalid country ID: {Country1}";
                ArchonLogger.LogWarning($"FormNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                lastValidationError = $"Invalid country ID: {Country2}";
                ArchonLogger.LogWarning($"FormNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (Country1 == Country2)
            {
                lastValidationError = $"Cannot form Non-Aggression Pact with self (Country {Country1})";
                ArchonLogger.LogWarning($"FormNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.HasNonAggressionPact(Country1, Country2))
            {
                lastValidationError = $"Non-Aggression Pact already exists (Countries {Country1} and {Country2})";
                ArchonLogger.LogWarning($"FormNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Non-Aggression Pact formation failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Non-Aggression Pact formed between Countries {Country1} and {Country2}";
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
            ArchonLogger.LogWarning("FormNonAggressionPactCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, Country1))
            {
                lastValidationError = $"Invalid country ID: {Country1}";
                ArchonLogger.LogWarning($"BreakNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, Country2))
            {
                lastValidationError = $"Invalid country ID: {Country2}";
                ArchonLogger.LogWarning($"BreakNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.HasNonAggressionPact(Country1, Country2))
            {
                lastValidationError = $"Non-Aggression Pact does not exist (Countries {Country1} and {Country2})";
                ArchonLogger.LogWarning($"BreakNonAggressionPactCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Non-Aggression Pact breaking failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Non-Aggression Pact broken between Countries {Country1} and {Country2}";
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
            ArchonLogger.LogWarning("BreakNonAggressionPactCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GuarantorID))
            {
                lastValidationError = $"Invalid guarantor ID: {GuarantorID}";
                ArchonLogger.LogWarning($"GuaranteeIndependenceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, GuaranteedID))
            {
                lastValidationError = $"Invalid guaranteed ID: {GuaranteedID}";
                ArchonLogger.LogWarning($"GuaranteeIndependenceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (GuarantorID == GuaranteedID)
            {
                lastValidationError = $"Cannot guarantee independence of self (Country {GuarantorID})";
                ArchonLogger.LogWarning($"GuaranteeIndependenceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.IsGuaranteeing(GuarantorID, GuaranteedID))
            {
                lastValidationError = $"Already guaranteeing independence (Country {GuarantorID} → Country {GuaranteedID})";
                ArchonLogger.LogWarning($"GuaranteeIndependenceCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Independence guarantee failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Country {GuarantorID} now guarantees independence of Country {GuaranteedID}";
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
            ArchonLogger.LogWarning("GuaranteeIndependenceCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GuarantorID))
            {
                lastValidationError = $"Invalid guarantor ID: {GuarantorID}";
                ArchonLogger.LogWarning($"RevokeGuaranteeCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, GuaranteedID))
            {
                lastValidationError = $"Invalid guaranteed ID: {GuaranteedID}";
                ArchonLogger.LogWarning($"RevokeGuaranteeCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.IsGuaranteeing(GuarantorID, GuaranteedID))
            {
                lastValidationError = $"Not guaranteeing independence (Country {GuarantorID} → Country {GuaranteedID})";
                ArchonLogger.LogWarning($"RevokeGuaranteeCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Independence guarantee revocation failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Country {GuarantorID} revoked independence guarantee of Country {GuaranteedID}";
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
            ArchonLogger.LogWarning("RevokeGuaranteeCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GranterID))
            {
                lastValidationError = $"Invalid granter ID: {GranterID}";
                ArchonLogger.LogWarning($"GrantMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, RecipientID))
            {
                lastValidationError = $"Invalid recipient ID: {RecipientID}";
                ArchonLogger.LogWarning($"GrantMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (GranterID == RecipientID)
            {
                lastValidationError = $"Cannot grant military access to self (Country {GranterID})";
                ArchonLogger.LogWarning($"GrantMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (diplomacy.HasMilitaryAccess(GranterID, RecipientID))
            {
                lastValidationError = $"Military access already granted (Country {GranterID} → Country {RecipientID})";
                ArchonLogger.LogWarning($"GrantMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Military access grant failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Country {GranterID} granted military access to Country {RecipientID}";
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
            ArchonLogger.LogWarning("GrantMilitaryAccessCommand: Undo not supported", "core_diplomacy");
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

        private string lastValidationError = null;

        public override int Priority => 80;

        public override bool Validate(GameState gameState)
        {
            if (!ValidateCountryId(gameState, GranterID))
            {
                lastValidationError = $"Invalid granter ID: {GranterID}";
                ArchonLogger.LogWarning($"RevokeMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            if (!ValidateCountryId(gameState, RecipientID))
            {
                lastValidationError = $"Invalid recipient ID: {RecipientID}";
                ArchonLogger.LogWarning($"RevokeMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            var diplomacy = gameState.GetComponent<DiplomacySystem>();

            if (!diplomacy.HasMilitaryAccess(GranterID, RecipientID))
            {
                lastValidationError = $"Military access not granted (Country {GranterID} → Country {RecipientID})";
                ArchonLogger.LogWarning($"RevokeMilitaryAccessCommand: {lastValidationError}", "core_diplomacy");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Military access revocation failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Country {GranterID} revoked military access from Country {RecipientID}";
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
            ArchonLogger.LogWarning("RevokeMilitaryAccessCommand: Undo not supported", "core_diplomacy");
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
