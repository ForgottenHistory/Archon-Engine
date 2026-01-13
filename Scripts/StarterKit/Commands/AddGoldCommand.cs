using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Add or remove gold from player treasury.
    /// Demonstrates SimpleCommand pattern - no separate factory class needed.
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

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.EconomySystem == null)
                return false;

            // If removing gold, check if enough exists
            if (Amount < 0 && initializer.EconomySystem.Gold < -Amount)
                return false;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.EconomySystem == null)
            {
                ArchonLogger.LogError("AddGoldCommand: EconomySystem not found", "starter_kit");
                return;
            }

            var economy = initializer.EconomySystem;
            previousGold = economy.Gold;

            if (Amount >= 0)
                economy.AddGold(Amount);
            else
                economy.RemoveGold(-Amount);

            LogExecution($"Gold changed by {Amount} (total: {economy.Gold})");
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.EconomySystem == null) return;

            var economy = initializer.EconomySystem;
            int difference = previousGold - economy.Gold;

            if (difference > 0)
                economy.AddGold(difference);
            else if (difference < 0)
                economy.RemoveGold(-difference);

            LogExecution($"Undid gold change (restored to {economy.Gold})");
        }
    }
}
