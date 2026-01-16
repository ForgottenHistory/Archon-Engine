using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Localization;
using Core.Systems;
using Core.Units;
using Core.UI;
using Map.Interaction;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Unit information panel
    /// Shows units in the selected province with option to create new units.
    /// Pattern: UI Presenter Pattern - View Component
    /// </summary>
    public class UnitInfoUI : StarterKitPanel
    {
        // UI Elements
        private Label headerLabel;
        private VisualElement unitsListContainer;
        private Button createUnitButton;
        private Button moveUnitsButton;
        private Button cancelMoveButton;
        private Label noUnitsLabel;
        private Label moveModeLabel;

        // References
        private UnitSystem unitSystem;
        private EconomySystem economySystem;
        private ProvinceSelector provinceSelector;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> unitEntries = new List<VisualElement>();

        // Move mode state
        private bool isInMoveMode;
        private ushort moveSourceProvinceID;
        private List<ushort> unitsToMove = new List<ushort>();

        public void Initialize(GameState gameStateRef, UnitSystem unitSystemRef, ProvinceSelector provinceSelectorRef, EconomySystem economySystemRef = null)
        {
            unitSystem = unitSystemRef;
            economySystem = economySystemRef;
            provinceSelector = provinceSelectorRef;

            if (unitSystemRef == null || provinceSelectorRef == null)
            {
                ArchonLogger.LogError("UnitInfoUI: Cannot initialize with null references!", "starter_kit");
                return;
            }

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to province selection events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleProvinceDeselected;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to unit events for auto-refresh
            unitSystem.OnUnitCreated += HandleUnitCreated;
            unitSystem.OnUnitDestroyed += HandleUnitDestroyed;
            unitSystem.OnUnitMoved += HandleUnitMoved;

            // Subscribe to EventBus events
            Subscribe<GoldChangedEvent>(HandleGoldChanged);
            Subscribe<ProvinceOwnershipChangedEvent>(HandleOwnershipChanged);

            // Hide until province selected
            Hide();

            ArchonLogger.Log("UnitInfoUI: Initialized", "starter_kit");
        }

        protected override void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                provinceSelector.OnProvinceRightClicked -= HandleProvinceDeselected;
                provinceSelector.OnSelectionCleared -= HandleSelectionCleared;
            }

            if (unitSystem != null)
            {
                unitSystem.OnUnitCreated -= HandleUnitCreated;
                unitSystem.OnUnitDestroyed -= HandleUnitDestroyed;
                unitSystem.OnUnitMoved -= HandleUnitMoved;
            }

            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Create panel container - positioned above province info (bottom left, higher up)
            panelContainer = CreateStyledPanel("unit-info-panel", minWidth: 180f);
            PositionPanel(bottom: 200f, left: 10f);

            // Header
            headerLabel = CreateTitle(LocalizationManager.Get("UI_UNITS"));
            headerLabel.style.marginBottom = SpacingMd;
            panelContainer.Add(headerLabel);

            // Units list container
            unitsListContainer = new VisualElement();
            unitsListContainer.name = "units-list";
            unitsListContainer.style.marginBottom = SpacingMd;
            panelContainer.Add(unitsListContainer);

            // No units label
            noUnitsLabel = CreateLabelText(LocalizationManager.Get("UI_NO_UNITS"));
            noUnitsLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            unitsListContainer.Add(noUnitsLabel);

            // Create unit button
            var infantryType = unitSystem.GetUnitType("infantry");
            int infantryCost = infantryType?.Cost ?? 20;
            string infantryName = LocalizationManager.Get("UNIT_INFANTRY");
            if (infantryName == "UNIT_INFANTRY") infantryName = "Infantry";

            createUnitButton = CreateStyledButton(
                $"+ {LocalizationManager.Get("UI_CREATE_UNIT")} ({infantryName}) - {infantryCost}g",
                OnCreateUnitClicked);
            createUnitButton.style.marginTop = SpacingXs;
            panelContainer.Add(createUnitButton);

            // Move units button
            string moveText = LocalizationManager.Get("UI_MOVE_UNITS");
            if (moveText == "UI_MOVE_UNITS") moveText = "Move Units";
            moveUnitsButton = CreateStyledButton(moveText, OnMoveUnitsClicked);
            moveUnitsButton.style.marginTop = SpacingXs;
            moveUnitsButton.style.display = DisplayStyle.None;
            panelContainer.Add(moveUnitsButton);

            // Cancel move button (shown during move mode)
            string cancelText = LocalizationManager.Get("UI_CANCEL");
            if (cancelText == "UI_CANCEL") cancelText = "Cancel";
            cancelMoveButton = CreateStyledButton(cancelText, OnCancelMoveClicked);
            cancelMoveButton.style.marginTop = SpacingXs;
            cancelMoveButton.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f, 1f);
            cancelMoveButton.style.display = DisplayStyle.None;
            panelContainer.Add(cancelMoveButton);

            // Move mode label (instruction text)
            moveModeLabel = new Label("Click a province to move units");
            moveModeLabel.style.fontSize = FontSizeSmall;
            moveModeLabel.style.color = TextWarning;
            moveModeLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            moveModeLabel.style.marginTop = SpacingSm;
            moveModeLabel.style.display = DisplayStyle.None;
            panelContainer.Add(moveModeLabel);
        }

        private void HandleProvinceClicked(ushort provinceID)
        {
            if (provinceID == 0)
            {
                HandleSelectionCleared();
                return;
            }

            // If in move mode, this click is the destination
            if (isInMoveMode)
            {
                ExecuteMove(provinceID);
                return;
            }

            selectedProvinceID = provinceID;
            RefreshUnitsList();
            Show();
        }

        private void HandleProvinceDeselected(ushort provinceID)
        {
            HandleSelectionCleared();
        }

        private void HandleSelectionCleared()
        {
            if (isInMoveMode)
            {
                ExitMoveMode();
            }

            selectedProvinceID = 0;
            Hide();
        }

        private void HandleUnitCreated(ushort unitId)
        {
            if (selectedProvinceID != 0)
            {
                var unit = unitSystem.GetUnit(unitId);
                if (unit.provinceID == selectedProvinceID)
                {
                    RefreshUnitsList();
                }
            }
        }

        private void HandleUnitDestroyed(ushort unitId)
        {
            if (selectedProvinceID != 0)
            {
                RefreshUnitsList();
            }
        }

        private void HandleUnitMoved(ushort unitId, ushort fromProvince, ushort toProvince)
        {
            if (selectedProvinceID != 0 &&
                (fromProvince == selectedProvinceID || toProvince == selectedProvinceID))
            {
                RefreshUnitsList();
            }
        }

        private void HandleGoldChanged(GoldChangedEvent evt)
        {
            if (selectedProvinceID == 0) return;

            var playerState = Initializer.Instance?.PlayerState;
            if (playerState == null || evt.CountryId != playerState.PlayerCountryId) return;

            UpdateCreateButtonState();
        }

        private void HandleOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            if (selectedProvinceID == 0 || evt.ProvinceId != selectedProvinceID) return;
            RefreshUnitsList();
        }

        private void UpdateCreateButtonState()
        {
            if (createUnitButton == null || economySystem == null) return;

            var infantryType = unitSystem?.GetUnitType("infantry");
            int cost = infantryType?.Cost ?? 20;

            bool canAfford = economySystem.Gold >= cost;
            bool isOwnedByPlayer = unitSystem?.IsProvinceOwnedByPlayer(selectedProvinceID) ?? false;

            createUnitButton.SetEnabled(canAfford && isOwnedByPlayer);
            createUnitButton.tooltip = !canAfford ? $"Not enough gold (need {cost}g)" : "";
        }

        private void OnCreateUnitClicked()
        {
            if (selectedProvinceID == 0)
            {
                ArchonLogger.LogWarning("UnitInfoUI: No province selected", "starter_kit");
                return;
            }

            var infantryType = unitSystem.GetUnitType("infantry");
            int cost = infantryType?.Cost ?? 20;

            if (economySystem != null)
            {
                if (!economySystem.RemoveGold(cost))
                {
                    ArchonLogger.Log($"UnitInfoUI: Not enough gold to recruit unit (need {cost}g, have {economySystem.Gold}g)", "starter_kit");
                    return;
                }
            }

            ushort unitId = unitSystem.CreateUnit(selectedProvinceID, "infantry");

            if (unitId == 0)
            {
                economySystem?.AddGold(cost);
                ArchonLogger.LogWarning("UnitInfoUI: Failed to create unit", "starter_kit");
            }
        }

        private void RefreshUnitsList()
        {
            if (selectedProvinceID == 0 || unitSystem == null)
                return;

            // Clear existing entries
            foreach (var entry in unitEntries)
            {
                unitsListContainer.Remove(entry);
            }
            unitEntries.Clear();

            // Show/hide create button based on province ownership
            bool isOwnedByPlayer = unitSystem.IsProvinceOwnedByPlayer(selectedProvinceID);
            createUnitButton.style.display = isOwnedByPlayer ? DisplayStyle.Flex : DisplayStyle.None;

            if (isOwnedByPlayer)
            {
                UpdateCreateButtonState();
            }

            // Get units in province
            var unitIds = unitSystem.GetUnitsInProvince(selectedProvinceID);

            // Check if there are player units (for move button)
            bool hasPlayerUnits = false;
            var playerState = Initializer.Instance?.PlayerState;
            if (playerState != null && playerState.HasPlayerCountry)
            {
                foreach (var unitId in unitIds)
                {
                    var unit = unitSystem.GetUnit(unitId);
                    if (unit.countryID == playerState.PlayerCountryId && unit.unitCount > 0)
                    {
                        hasPlayerUnits = true;
                        break;
                    }
                }
            }

            // Show move button only if there are player units and not in move mode
            moveUnitsButton.style.display = (hasPlayerUnits && !isInMoveMode) ? DisplayStyle.Flex : DisplayStyle.None;

            if (unitIds.Count == 0)
            {
                noUnitsLabel.style.display = DisplayStyle.Flex;
                return;
            }

            noUnitsLabel.style.display = DisplayStyle.None;

            // Group units by type and sum troop counts
            var unitsByType = new Dictionary<ushort, int>();
            foreach (var unitId in unitIds)
            {
                var unitState = unitSystem.GetUnit(unitId);
                if (!unitsByType.ContainsKey(unitState.unitTypeID))
                    unitsByType[unitState.unitTypeID] = 0;
                unitsByType[unitState.unitTypeID] += unitState.unitCount;
            }

            // Create entry for each unit type (stacked)
            foreach (var kvp in unitsByType)
            {
                var unitType = unitSystem.GetUnitType(kvp.Key);
                var entry = CreateUnitEntry(unitType, kvp.Value);
                unitsListContainer.Add(entry);
                unitEntries.Add(entry);
            }
        }

        private VisualElement CreateUnitEntry(UnitType unitType, int totalTroops)
        {
            var entry = CreateRowEntry();

            string typeName = unitType != null
                ? LocalizationManager.Get($"UNIT_{unitType.StringID.ToUpperInvariant()}")
                : "Unknown";
            if (typeName.StartsWith("UNIT_") && unitType != null) typeName = unitType.Name;

            var nameLabel = CreateText($"{typeName} x{totalTroops}");
            nameLabel.style.fontSize = FontSizeNormal - 1;
            entry.Add(nameLabel);

            return entry;
        }

        #region Move Mode

        private void OnMoveUnitsClicked()
        {
            if (selectedProvinceID == 0)
            {
                ArchonLogger.LogWarning("UnitInfoUI: No province selected for move", "starter_kit");
                return;
            }

            EnterMoveMode();
        }

        private void OnCancelMoveClicked()
        {
            ExitMoveMode();
        }

        private void EnterMoveMode()
        {
            var playerState = Initializer.Instance?.PlayerState;
            if (playerState == null || !playerState.HasPlayerCountry)
            {
                ArchonLogger.LogWarning("UnitInfoUI: Cannot enter move mode - no player country", "starter_kit");
                return;
            }

            unitsToMove.Clear();
            var unitIds = unitSystem.GetUnitsInProvince(selectedProvinceID);
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
                ArchonLogger.LogWarning("UnitInfoUI: No player units to move", "starter_kit");
                return;
            }

            isInMoveMode = true;
            moveSourceProvinceID = selectedProvinceID;

            // Update UI
            string moveModeText = LocalizationManager.Get("UI_MOVE_MODE");
            if (moveModeText == "UI_MOVE_MODE") moveModeText = "Move Mode";
            headerLabel.text = moveModeText;

            createUnitButton.style.display = DisplayStyle.None;
            moveUnitsButton.style.display = DisplayStyle.None;
            cancelMoveButton.style.display = DisplayStyle.Flex;
            moveModeLabel.style.display = DisplayStyle.Flex;

            ArchonLogger.Log($"UnitInfoUI: Entered move mode with {unitsToMove.Count} units from province {moveSourceProvinceID}", "starter_kit");
        }

        private void ExitMoveMode()
        {
            isInMoveMode = false;
            moveSourceProvinceID = 0;
            unitsToMove.Clear();

            // Restore UI
            string unitsText = LocalizationManager.Get("UI_UNITS");
            if (unitsText == "UI_UNITS") unitsText = "Units";
            headerLabel.text = unitsText;

            cancelMoveButton.style.display = DisplayStyle.None;
            moveModeLabel.style.display = DisplayStyle.None;

            RefreshUnitsList();

            ArchonLogger.Log("UnitInfoUI: Exited move mode", "starter_kit");
        }

        private void ExecuteMove(ushort targetProvinceID)
        {
            if (!isInMoveMode || moveSourceProvinceID == 0 || unitsToMove.Count == 0)
            {
                ArchonLogger.LogWarning("UnitInfoUI: Invalid move state", "starter_kit");
                ExitMoveMode();
                return;
            }

            if (targetProvinceID == moveSourceProvinceID)
            {
                ArchonLogger.Log("UnitInfoUI: Same province selected, cancelling move", "starter_kit");
                ExitMoveMode();
                return;
            }

            if (gameState.Pathfinding == null || !gameState.Pathfinding.IsInitialized)
            {
                ArchonLogger.LogWarning("UnitInfoUI: Pathfinding not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            var movementQueue = gameState.Units?.MovementQueue;
            if (movementQueue == null)
            {
                ArchonLogger.LogWarning("UnitInfoUI: MovementQueue not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            var playerState = Initializer.Instance?.PlayerState;
            ushort countryId = playerState?.PlayerCountryId ?? 0;

            var path = gameState.Pathfinding.FindPath(moveSourceProvinceID, targetProvinceID, countryId);

            if (path == null || path.Count < 2)
            {
                ArchonLogger.Log($"UnitInfoUI: No path from province {moveSourceProvinceID} to {targetProvinceID}", "starter_kit");
                return;
            }

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

            ArchonLogger.Log($"UnitInfoUI: Issued {ordersIssued} movement orders from province {moveSourceProvinceID} to {targetProvinceID} (path length: {path.Count})", "starter_kit");

            ExitMoveMode();
        }

        #endregion
    }
}
