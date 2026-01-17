using Core;
using Core.Commands;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Add or remove gold from player treasury.
    /// Demonstrates SimpleCommand pattern with fluent validation.
    /// </summary>
    [Command("add_gold",
        Aliases = new[] { "gold" },
        Description = "Add or remove gold from treasury",
        Examples = new[] { "add_gold 100", "add_gold -50" })]
    public class AddGoldCommand : SimpleCommand
    {
        [Arg(0, "amount")]
        public int Amount { get; set; }

        // For undo support
        private int previousGold;
        private string validationError;

        public override bool Validate(GameState gameState)
        {
            // If removing gold, validate sufficient funds
            if (Amount < 0)
            {
                return Core.Validation.Validate.For(gameState)
                    .HasGold(-Amount)
                    .Result(out validationError);
            }

            // Adding gold always valid (if EconomySystem exists)
            var economy = Initializer.Instance?.EconomySystem;
            return economy != null;
        }

        public override void Execute(GameState gameState)
        {
            var economy = Initializer.Instance?.EconomySystem;
            if (economy == null)
            {
                ArchonLogger.LogError("AddGoldCommand: EconomySystem not found", "starter_kit");
                return;
            }
            previousGold = economy.Gold;

            if (Amount >= 0)
                economy.AddGold(Amount);
            else
                economy.RemoveGold(-Amount);

            LogExecution($"Gold changed by {Amount} (total: {economy.Gold})");
        }

        public override void Undo(GameState gameState)
        {
            var economy = Initializer.Instance?.EconomySystem;
            if (economy == null) return;

            int difference = previousGold - economy.Gold;

            if (difference > 0)
                economy.AddGold(difference);
            else if (difference < 0)
                economy.RemoveGold(-difference);

            LogExecution($"Undid gold change (restored to {economy.Gold})");
        }
    }
}
