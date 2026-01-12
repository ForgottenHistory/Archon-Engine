using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Add or remove gold from player treasury.
    /// Uses simple int-based economy (not FixedPoint64).
    /// </summary>
    public class AddGoldCommand : BaseCommand
    {
        private int amount;
        private int previousGold; // For undo

        public AddGoldCommand() { }

        public AddGoldCommand(int amount)
        {
            this.amount = amount;
        }

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer == null || initializer.EconomySystem == null)
            {
                return false;
            }

            // If removing gold, check if enough exists
            if (amount < 0 && initializer.EconomySystem.Gold < -amount)
            {
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.EconomySystem == null)
            {
                ArchonLogger.LogError("AddGoldCommand: Initializer or EconomySystem not found", "starter_kit");
                return;
            }

            var economy = initializer.EconomySystem;
            previousGold = economy.Gold;

            if (amount >= 0)
            {
                economy.AddGold(amount);
                LogExecution($"Added {amount} gold (total: {economy.Gold})");
            }
            else
            {
                economy.RemoveGold(-amount);
                LogExecution($"Removed {-amount} gold (total: {economy.Gold})");
            }
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.EconomySystem == null) return;

            var economy = initializer.EconomySystem;
            int currentGold = economy.Gold;
            int difference = previousGold - currentGold;

            if (difference > 0)
                economy.AddGold(difference);
            else if (difference < 0)
                economy.RemoveGold(-difference);

            LogExecution($"Undid gold change (restored to {economy.Gold})");
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(amount);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            amount = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Factory for creating AddGoldCommand from console input.
    /// </summary>
    [CommandMetadata("add_gold",
        Aliases = new[] { "gold" },
        Description = "Add or remove gold from treasury",
        Usage = "add_gold <amount>",
        Examples = new[] { "add_gold 100", "add_gold -50" })]
    public class AddGoldCommandFactory : ICommandFactory
    {
        public bool TryCreateCommand(string[] args, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            if (args.Length < 1)
            {
                errorMessage = "Usage: add_gold <amount>";
                return false;
            }

            if (!int.TryParse(args[0], out int amount))
            {
                errorMessage = $"Invalid amount: '{args[0]}'";
                return false;
            }

            command = new AddGoldCommand(amount);
            return true;
        }
    }
}
