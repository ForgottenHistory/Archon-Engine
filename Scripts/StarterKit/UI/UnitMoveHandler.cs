using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Localization;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Handles unit movement mode for UnitInfoUI.
    /// Extracted to keep UnitInfoUI focused on display concerns.
    /// </summary>
    public class UnitMoveHandler
    {
        // State
        private bool isInMoveMode;
        private ushort moveSourceProvinceID;
        private List<ushort> unitsToMove = new List<ushort>();

        // References
        private readonly GameState gameState;
        private readonly UnitSystem unitSystem;

        // UI elements (passed from UnitInfoUI)
        private Label headerLabel;
        private Button createUnitButton;
        private Button moveUnitsButton;
        private Button cancelMoveButton;
        private Label moveModeLabel;

        // Callbacks
        public System.Action OnMoveComplete;

        public bool IsInMoveMode => isInMoveMode;

        public UnitMoveHandler(GameState gameState, UnitSystem unitSystem)
        {
            this.gameState = gameState;
            this.unitSystem = unitSystem;
        }

        /// <summary>
        /// Set UI element references for move mode display updates.
        /// </summary>
        public void SetUIElements(Label header, Button createBtn, Button moveBtn, Button cancelBtn, Label moveLabel)
        {
            headerLabel = header;
            createUnitButton = createBtn;
            moveUnitsButton = moveBtn;
            cancelMoveButton = cancelBtn;
            moveModeLabel = moveLabel;
        }

        /// <summary>
        /// Enter move mode for units in the specified province.
        /// </summary>
        public bool TryEnterMoveMode(ushort sourceProvinceID)
        {
            var playerState = Initializer.Instance?.PlayerState;
            if (playerState == null || !playerState.HasPlayerCountry)
            {
                ArchonLogger.LogWarning("UnitMoveHandler: Cannot enter move mode - no player country", "starter_kit");
                return false;
            }

            // Collect player units in province
            unitsToMove.Clear();
            var unitIds = unitSystem.GetUnitsInProvince(sourceProvinceID);
            foreach (var unitId in unitIds)
            {
                var unit = unitSystem.GetUnit(unitId);
                if (unit.countryID == playerState.PlayerCountryId && unit.unitCount > 0)
                {
                    unitsToMove.Add(unitId);
                }
            }

            if (unitsToMove.Count == 0)
            {
                ArchonLogger.LogWarning("UnitMoveHandler: No player units to move", "starter_kit");
                return false;
            }

            isInMoveMode = true;
            moveSourceProvinceID = sourceProvinceID;

            UpdateUIForMoveMode(true);

            ArchonLogger.Log($"UnitMoveHandler: Entered move mode with {unitsToMove.Count} units from province {moveSourceProvinceID}", "starter_kit");
            return true;
        }

        /// <summary>
        /// Exit move mode without executing a move.
        /// </summary>
        public void ExitMoveMode()
        {
            isInMoveMode = false;
            moveSourceProvinceID = 0;
            unitsToMove.Clear();

            UpdateUIForMoveMode(false);

            ArchonLogger.Log("UnitMoveHandler: Exited move mode", "starter_kit");
            OnMoveComplete?.Invoke();
        }

        /// <summary>
        /// Handle a province click while in move mode.
        /// Returns true if the click was handled (move executed or cancelled).
        /// </summary>
        public bool HandleProvinceClick(ushort targetProvinceID)
        {
            if (!isInMoveMode)
                return false;

            ExecuteMove(targetProvinceID);
            return true;
        }

        private void ExecuteMove(ushort targetProvinceID)
        {
            if (!isInMoveMode || moveSourceProvinceID == 0 || unitsToMove.Count == 0)
            {
                ArchonLogger.LogWarning("UnitMoveHandler: Invalid move state", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Same province = cancel
            if (targetProvinceID == moveSourceProvinceID)
            {
                ArchonLogger.Log("UnitMoveHandler: Same province selected, cancelling move", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Check pathfinding availability
            if (gameState.Pathfinding == null || !gameState.Pathfinding.IsInitialized)
            {
                ArchonLogger.LogWarning("UnitMoveHandler: Pathfinding not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            var movementQueue = gameState.Units?.MovementQueue;
            if (movementQueue == null)
            {
                ArchonLogger.LogWarning("UnitMoveHandler: MovementQueue not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Find path
            var playerState = Initializer.Instance?.PlayerState;
            ushort countryId = playerState?.PlayerCountryId ?? 0;
            var path = gameState.Pathfinding.FindPath(moveSourceProvinceID, targetProvinceID, countryId);

            if (path == null || path.Count < 2)
            {
                ArchonLogger.Log($"UnitMoveHandler: No path from province {moveSourceProvinceID} to {targetProvinceID}", "starter_kit");
                return;
            }

            // Issue movement orders
            int ordersIssued = 0;
            foreach (var unitId in unitsToMove)
            {
                var unit = unitSystem.GetUnit(unitId);
                if (unit.unitCount > 0)
                {
                    var unitType = unitSystem.GetUnitType(unit.unitTypeID);
                    int movementDays = unitType?.Speed ?? 2;

                    ushort firstDestination = path[1];
                    movementQueue.StartMovement(unitId, firstDestination, movementDays, path);
                    ordersIssued++;
                }
            }

            ArchonLogger.Log($"UnitMoveHandler: Issued {ordersIssued} movement orders from province {moveSourceProvinceID} to {targetProvinceID} (path length: {path.Count})", "starter_kit");

            ExitMoveMode();
        }

        private void UpdateUIForMoveMode(bool entering)
        {
            if (entering)
            {
                string moveModeText = LocalizationManager.Get("UI_MOVE_MODE");
                if (moveModeText == "UI_MOVE_MODE") moveModeText = "Move Mode";
                if (headerLabel != null) headerLabel.text = moveModeText;

                if (createUnitButton != null) createUnitButton.style.display = DisplayStyle.None;
                if (moveUnitsButton != null) moveUnitsButton.style.display = DisplayStyle.None;
                if (cancelMoveButton != null) cancelMoveButton.style.display = DisplayStyle.Flex;
                if (moveModeLabel != null) moveModeLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                string unitsText = LocalizationManager.Get("UI_UNITS");
                if (unitsText == "UI_UNITS") unitsText = "Units";
                if (headerLabel != null) headerLabel.text = unitsText;

                if (cancelMoveButton != null) cancelMoveButton.style.display = DisplayStyle.None;
                if (moveModeLabel != null) moveModeLabel.style.display = DisplayStyle.None;
            }
        }
    }
}
