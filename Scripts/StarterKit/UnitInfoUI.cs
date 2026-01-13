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
        private Label noUnitsLabel;

        // References
        private GameState gameState;
        private UnitSystem unitSystem;
        private ProvinceSelector provinceSelector;
        private bool isInitialized;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> unitEntries = new List<VisualElement>();

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, UnitSystem unitSystemRef, ProvinceSelector provinceSelectorRef)
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
            createUnitButton.text = $"+ {LocalizationManager.Get("UI_CREATE_UNIT")} ({infantryName})";
            createUnitButton.style.marginTop = 4f;
            createUnitButton.style.paddingTop = 6f;
            createUnitButton.style.paddingBottom = 6f;
            createUnitButton.style.paddingLeft = 10f;
            createUnitButton.style.paddingRight = 10f;
            panelContainer.Add(createUnitButton);

            rootElement.Add(panelContainer);
        }

        private void HandleProvinceClicked(ushort provinceID)
        {
            if (provinceID == 0)
            {
                HandleSelectionCleared();
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

            // Create infantry at selected province
            ushort unitId = unitSystem.CreateUnit(selectedProvinceID, "infantry");

            if (unitId == 0)
            {
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

            if (unitIds.Count == 0)
            {
                noUnitsLabel.style.display = DisplayStyle.Flex;
                return;
            }

            noUnitsLabel.style.display = DisplayStyle.None;

            // Create entry for each unit
            foreach (var unitId in unitIds)
            {
                var unitState = unitSystem.GetUnit(unitId);
                var unitType = unitSystem.GetUnitType(unitState.unitTypeID);

                var entry = CreateUnitEntry(unitId, unitState, unitType);
                unitsListContainer.Add(entry);
                unitEntries.Add(entry);
            }
        }

        private VisualElement CreateUnitEntry(ushort unitId, UnitState state, UnitType unitType)
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

            // Unit info container
            var infoContainer = new VisualElement();

            // Unit name/type - try localization first
            string typeName = unitType != null
                ? LocalizationManager.Get($"UNIT_{unitType.StringID.ToUpperInvariant()}")
                : $"Unit #{unitId}";
            if (typeName.StartsWith("UNIT_") && unitType != null) typeName = unitType.Name; // Fallback
            var nameLabel = new Label(typeName);
            nameLabel.style.fontSize = fontSize - 1;
            nameLabel.style.color = textColor;
            infoContainer.Add(nameLabel);

            // Strength/morale
            var statsLabel = new Label($"{LocalizationManager.Get("UI_STRENGTH")}: {state.strength}% | {LocalizationManager.Get("UI_MORALE")}: {state.morale}%");
            statsLabel.style.fontSize = fontSize - 3;
            statsLabel.style.color = labelColor;
            infoContainer.Add(statsLabel);

            entry.Add(infoContainer);

            // Disband button
            var disbandButton = new Button(() => OnDisbandClicked(unitId));
            disbandButton.text = "X";
            disbandButton.style.width = 20f;
            disbandButton.style.height = 20f;
            disbandButton.style.fontSize = 10;
            disbandButton.style.paddingTop = 0f;
            disbandButton.style.paddingBottom = 0f;
            disbandButton.style.paddingLeft = 0f;
            disbandButton.style.paddingRight = 0f;
            entry.Add(disbandButton);

            return entry;
        }

        private void OnDisbandClicked(ushort unitId)
        {
            unitSystem.DisbandUnit(unitId);
            // List will auto-refresh via event
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
    }
}
