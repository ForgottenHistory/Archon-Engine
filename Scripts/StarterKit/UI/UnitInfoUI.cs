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
    /// Movement logic is handled by UnitMoveHandler.
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
        private UnitMoveHandler moveHandler;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> unitEntries = new List<VisualElement>();

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

            // Create move handler
            moveHandler = new UnitMoveHandler(gameState, unitSystem);
            moveHandler.OnMoveComplete = RefreshUnitsList;

            // Subscribe to province selection events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleProvinceDeselected;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to unit events for auto-refresh (via EventBus)
            Subscribe<UnitCreatedEvent>(HandleUnitCreated);
            Subscribe<UnitDestroyedEvent>(HandleUnitDestroyed);
            Subscribe<UnitMovedEvent>(HandleUnitMoved);

            // Subscribe to other EventBus events
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

            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Create panel container - positioned above buildings panel (bottom left)
            panelContainer = CreateStyledPanel("unit-info-panel", minWidth: 180f);
            PositionPanel(bottom: 500f, left: 10f);

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

            // Pass UI elements to move handler
            moveHandler?.SetUIElements(headerLabel, createUnitButton, moveUnitsButton, cancelMoveButton, moveModeLabel);
        }

        #region Event Handlers

        private void HandleProvinceClicked(ushort provinceID)
        {
            if (provinceID == 0)
            {
                HandleSelectionCleared();
                return;
            }

            // If in move mode, delegate to handler
            if (moveHandler != null && moveHandler.HandleProvinceClick(provinceID))
            {
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
            if (moveHandler != null && moveHandler.IsInMoveMode)
            {
                moveHandler.ExitMoveMode();
            }

            selectedProvinceID = 0;
            Hide();
        }

        private void HandleUnitCreated(UnitCreatedEvent evt)
        {
            if (selectedProvinceID != 0 && evt.ProvinceID == selectedProvinceID)
            {
                RefreshUnitsList();
            }
        }

        private void HandleUnitDestroyed(UnitDestroyedEvent evt)
        {
            if (selectedProvinceID != 0 && evt.ProvinceID == selectedProvinceID)
            {
                RefreshUnitsList();
            }
        }

        private void HandleUnitMoved(UnitMovedEvent evt)
        {
            if (selectedProvinceID != 0 &&
                (evt.OldProvinceID == selectedProvinceID || evt.NewProvinceID == selectedProvinceID))
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

        #endregion

        #region Unit Creation

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

        #endregion

        #region Unit Display

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
            bool hasPlayerUnits = HasPlayerUnitsInProvince(unitIds);
            bool inMoveMode = moveHandler?.IsInMoveMode ?? false;
            moveUnitsButton.style.display = (hasPlayerUnits && !inMoveMode) ? DisplayStyle.Flex : DisplayStyle.None;

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

        private bool HasPlayerUnitsInProvince(List<ushort> unitIds)
        {
            var playerState = Initializer.Instance?.PlayerState;
            if (playerState == null || !playerState.HasPlayerCountry)
                return false;

            foreach (var unitId in unitIds)
            {
                var unit = unitSystem.GetUnit(unitId);
                if (unit.countryID == playerState.PlayerCountryId && unit.unitCount > 0)
                    return true;
            }
            return false;
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

        #endregion

        #region Move Mode

        private void OnMoveUnitsClicked()
        {
            if (selectedProvinceID == 0)
            {
                ArchonLogger.LogWarning("UnitInfoUI: No province selected for move", "starter_kit");
                return;
            }

            moveHandler?.TryEnterMoveMode(selectedProvinceID);
        }

        private void OnCancelMoveClicked()
        {
            moveHandler?.ExitMoveMode();
        }

        #endregion
    }
}
