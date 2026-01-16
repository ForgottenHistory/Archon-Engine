using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Localization;
using Core.Units;
using Map.Interaction;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Unit information panel
    /// Shows units in the selected province with option to create new units.
    /// Pattern: UI Presenter Pattern - View Component
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UnitInfoUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color labelColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        [SerializeField] private int fontSize = 14;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement panelContainer;
        private Label headerLabel;
        private VisualElement unitsListContainer;
        private Button createUnitButton;
        private Button moveUnitsButton;
        private Button cancelMoveButton;
        private Label noUnitsLabel;
        private Label moveModeLabel;

        // References
        private GameState gameState;
        private UnitSystem unitSystem;
        private EconomySystem economySystem;
        private ProvinceSelector provinceSelector;
        private bool isInitialized;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> unitEntries = new List<VisualElement>();

        // Move mode state
        private bool isInMoveMode;
        private ushort moveSourceProvinceID;
        private List<ushort> unitsToMove = new List<ushort>();

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, UnitSystem unitSystemRef, ProvinceSelector provinceSelectorRef, EconomySystem economySystemRef = null)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("UnitInfoUI: Already initialized!", "starter_kit");
                return;
            }

            if (gameStateRef == null || unitSystemRef == null || provinceSelectorRef == null)
            {
                ArchonLogger.LogError("UnitInfoUI: Cannot initialize with null references!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            economySystem = economySystemRef;
            unitSystem = unitSystemRef;
            provinceSelector = provinceSelectorRef;

            // Initialize UI
            InitializeUI();

            // Subscribe to province selection events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleProvinceDeselected;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to unit events for auto-refresh
            unitSystem.OnUnitCreated += HandleUnitCreated;
            unitSystem.OnUnitDestroyed += HandleUnitDestroyed;
            unitSystem.OnUnitMoved += HandleUnitMoved;

            isInitialized = true;

            // Hide until province selected
            HidePanel();

            ArchonLogger.Log("UnitInfoUI: Initialized", "starter_kit");
        }

        void OnDestroy()
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
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("UnitInfoUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("UnitInfoUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Create panel container - positioned above province info (bottom left, higher up)
            panelContainer = new VisualElement();
            panelContainer.name = "unit-info-panel";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 10f;
            panelContainer.style.bottom = 200f; // Above province info panel
            panelContainer.style.backgroundColor = backgroundColor;
            panelContainer.style.paddingTop = 10f;
            panelContainer.style.paddingBottom = 10f;
            panelContainer.style.paddingLeft = 12f;
            panelContainer.style.paddingRight = 12f;
            panelContainer.style.borderTopLeftRadius = 6f;
            panelContainer.style.borderTopRightRadius = 6f;
            panelContainer.style.borderBottomLeftRadius = 6f;
            panelContainer.style.borderBottomRightRadius = 6f;
            panelContainer.style.minWidth = 180f;

            // Header
            headerLabel = new Label(LocalizationManager.Get("UI_UNITS"));
            headerLabel.style.fontSize = fontSize;
            headerLabel.style.color = textColor;
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.marginBottom = 8f;
            panelContainer.Add(headerLabel);

            // Units list container
            unitsListContainer = new VisualElement();
            unitsListContainer.name = "units-list";
            unitsListContainer.style.marginBottom = 8f;
            panelContainer.Add(unitsListContainer);

            // No units label
            noUnitsLabel = new Label(LocalizationManager.Get("UI_NO_UNITS"));
            noUnitsLabel.style.fontSize = fontSize - 2;
            noUnitsLabel.style.color = labelColor;
            noUnitsLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            unitsListContainer.Add(noUnitsLabel);

            // Create unit button
            createUnitButton = new Button(OnCreateUnitClicked);
            string infantryName = LocalizationManager.Get("UNIT_INFANTRY");
            if (infantryName == "UNIT_INFANTRY") infantryName = "Infantry"; // Fallback
            var infantryType = unitSystem.GetUnitType("infantry");
            int infantryCost = infantryType?.Cost ?? 20;
            createUnitButton.text = $"+ {LocalizationManager.Get("UI_CREATE_UNIT")} ({infantryName}) - {infantryCost}g";
            createUnitButton.style.marginTop = 4f;
            createUnitButton.style.paddingTop = 6f;
            createUnitButton.style.paddingBottom = 6f;
            createUnitButton.style.paddingLeft = 10f;
            createUnitButton.style.paddingRight = 10f;
            panelContainer.Add(createUnitButton);

            // Move units button
            moveUnitsButton = new Button(OnMoveUnitsClicked);
            moveUnitsButton.text = LocalizationManager.Get("UI_MOVE_UNITS");
            if (moveUnitsButton.text == "UI_MOVE_UNITS") moveUnitsButton.text = "Move Units"; // Fallback
            moveUnitsButton.style.marginTop = 4f;
            moveUnitsButton.style.paddingTop = 6f;
            moveUnitsButton.style.paddingBottom = 6f;
            moveUnitsButton.style.paddingLeft = 10f;
            moveUnitsButton.style.paddingRight = 10f;
            moveUnitsButton.style.display = DisplayStyle.None;
            panelContainer.Add(moveUnitsButton);

            // Cancel move button (shown during move mode)
            cancelMoveButton = new Button(OnCancelMoveClicked);
            cancelMoveButton.text = LocalizationManager.Get("UI_CANCEL");
            if (cancelMoveButton.text == "UI_CANCEL") cancelMoveButton.text = "Cancel"; // Fallback
            cancelMoveButton.style.marginTop = 4f;
            cancelMoveButton.style.paddingTop = 6f;
            cancelMoveButton.style.paddingBottom = 6f;
            cancelMoveButton.style.paddingLeft = 10f;
            cancelMoveButton.style.paddingRight = 10f;
            cancelMoveButton.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f, 1f);
            cancelMoveButton.style.display = DisplayStyle.None;
            panelContainer.Add(cancelMoveButton);

            // Move mode label (instruction text)
            moveModeLabel = new Label("Click a province to move units");
            moveModeLabel.style.fontSize = fontSize - 2;
            moveModeLabel.style.color = new Color(1f, 0.8f, 0.2f, 1f); // Yellow for visibility
            moveModeLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            moveModeLabel.style.marginTop = 6f;
            moveModeLabel.style.display = DisplayStyle.None;
            panelContainer.Add(moveModeLabel);

            rootElement.Add(panelContainer);
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
            ShowPanel();
        }

        private void HandleProvinceDeselected(ushort provinceID)
        {
            HandleSelectionCleared();
        }

        private void HandleSelectionCleared()
        {
            // Cancel move mode if active
            if (isInMoveMode)
            {
                ExitMoveMode();
            }

            selectedProvinceID = 0;
            HidePanel();
        }

        private void HandleUnitCreated(ushort unitId)
        {
            // Refresh if the unit is in our selected province
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
            // Always refresh when unit destroyed (we don't know where it was)
            if (selectedProvinceID != 0)
            {
                RefreshUnitsList();
            }
        }

        private void HandleUnitMoved(ushort unitId, ushort fromProvince, ushort toProvince)
        {
            // Refresh if the unit moved to/from our selected province
            if (selectedProvinceID != 0 &&
                (fromProvince == selectedProvinceID || toProvince == selectedProvinceID))
            {
                RefreshUnitsList();
            }
        }

        private void OnCreateUnitClicked()
        {
            if (selectedProvinceID == 0)
            {
                ArchonLogger.LogWarning("UnitInfoUI: No province selected", "starter_kit");
                return;
            }

            // Get unit cost
            var infantryType = unitSystem.GetUnitType("infantry");
            int cost = infantryType?.Cost ?? 20;

            // Check and deduct gold
            if (economySystem != null)
            {
                if (!economySystem.RemoveGold(cost))
                {
                    ArchonLogger.Log($"UnitInfoUI: Not enough gold to recruit unit (need {cost}g, have {economySystem.Gold}g)", "starter_kit");
                    return;
                }
            }

            // Create infantry at selected province
            ushort unitId = unitSystem.CreateUnit(selectedProvinceID, "infantry");

            if (unitId == 0)
            {
                // Refund gold if creation failed
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

            // Group units by type and sum troop counts (like buildings)
            var unitsByType = new Dictionary<ushort, int>(); // unitTypeID -> total troops
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
            var entry = new VisualElement();
            entry.style.flexDirection = FlexDirection.Row;
            entry.style.alignItems = Align.Center;
            entry.style.justifyContent = Justify.SpaceBetween;
            entry.style.marginBottom = 4f;
            entry.style.paddingTop = 4f;
            entry.style.paddingBottom = 4f;
            entry.style.paddingLeft = 6f;
            entry.style.paddingRight = 6f;
            entry.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            entry.style.borderTopLeftRadius = 3f;
            entry.style.borderTopRightRadius = 3f;
            entry.style.borderBottomLeftRadius = 3f;
            entry.style.borderBottomRightRadius = 3f;

            // Unit name/type with troop count
            string typeName = unitType != null
                ? LocalizationManager.Get($"UNIT_{unitType.StringID.ToUpperInvariant()}")
                : "Unknown";
            if (typeName.StartsWith("UNIT_") && unitType != null) typeName = unitType.Name; // Fallback

            var nameLabel = new Label($"{typeName} x{totalTroops}");
            nameLabel.style.fontSize = fontSize - 1;
            nameLabel.style.color = textColor;
            entry.Add(nameLabel);

            return entry;
        }

        public void ShowPanel()
        {
            if (panelContainer != null)
            {
                panelContainer.style.display = DisplayStyle.Flex;
            }
        }

        public void HidePanel()
        {
            if (panelContainer != null)
            {
                panelContainer.style.display = DisplayStyle.None;
            }
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

            // Collect player units in selected province
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
            headerLabel.text = LocalizationManager.Get("UI_MOVE_MODE");
            if (headerLabel.text == "UI_MOVE_MODE") headerLabel.text = "Move Mode";

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
            headerLabel.text = LocalizationManager.Get("UI_UNITS");
            if (headerLabel.text == "UI_UNITS") headerLabel.text = "Units";

            cancelMoveButton.style.display = DisplayStyle.None;
            moveModeLabel.style.display = DisplayStyle.None;

            // Refresh to restore proper button states
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

            // Same province - cancel
            if (targetProvinceID == moveSourceProvinceID)
            {
                ArchonLogger.Log("UnitInfoUI: Same province selected, cancelling move", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Check if pathfinding is available
            if (gameState.Pathfinding == null || !gameState.Pathfinding.IsInitialized)
            {
                ArchonLogger.LogWarning("UnitInfoUI: Pathfinding not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Check if movement queue is available
            var movementQueue = gameState.Units?.MovementQueue;
            if (movementQueue == null)
            {
                ArchonLogger.LogWarning("UnitInfoUI: MovementQueue not available", "starter_kit");
                ExitMoveMode();
                return;
            }

            // Check if path exists
            var playerState = Initializer.Instance?.PlayerState;
            ushort countryId = playerState?.PlayerCountryId ?? 0;

            var path = gameState.Pathfinding.FindPath(moveSourceProvinceID, targetProvinceID, countryId);

            if (path == null || path.Count < 2)
            {
                ArchonLogger.Log($"UnitInfoUI: No path from province {moveSourceProvinceID} to {targetProvinceID}", "starter_kit");
                // Stay in move mode so player can try another destination
                return;
            }

            // Issue movement orders for all units via Core's MovementQueue
            int ordersIssued = 0;
            foreach (var unitId in unitsToMove)
            {
                var unit = unitSystem.GetUnit(unitId);
                if (unit.unitCount > 0) // Unit still exists
                {
                    // Get speed from unit type (days per province)
                    var unitType = unitSystem.GetUnitType(unit.unitTypeID);
                    int movementDays = unitType?.Speed ?? 2;

                    // Start movement to first waypoint, passing full path for multi-hop
                    ushort firstDestination = path[1]; // path[0] is current position
                    movementQueue.StartMovement(unitId, firstDestination, movementDays, path);
                    ordersIssued++;
                }
            }

            ArchonLogger.Log($"UnitInfoUI: Issued {ordersIssued} movement orders from province {moveSourceProvinceID} to {targetProvinceID} (path length: {path.Count})", "starter_kit");

            // Exit move mode (units will arrive over time)
            ExitMoveMode();
        }

        #endregion
    }
}
