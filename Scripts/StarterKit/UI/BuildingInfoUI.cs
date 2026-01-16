using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Localization;
using Map.Interaction;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Building information panel
    /// Shows buildings in the selected province with option to construct new buildings.
    /// Pattern: UI Presenter Pattern - View Component
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BuildingInfoUI : MonoBehaviour
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
        private Label goldBonusLabel;
        private VisualElement buildingsListContainer;
        private VisualElement buildButtonsContainer;
        private Label noBuildingsLabel;

        // References
        private GameState gameState;
        private BuildingSystem buildingSystem;
        private ProvinceSelector provinceSelector;
        private CompositeDisposable subscriptions;
        private bool isInitialized;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> buildingEntries = new List<VisualElement>();
        private List<Button> buildButtons = new List<Button>();

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, BuildingSystem buildingSystemRef, ProvinceSelector provinceSelectorRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("BuildingInfoUI: Already initialized!", "starter_kit");
                return;
            }

            if (gameStateRef == null || buildingSystemRef == null || provinceSelectorRef == null)
            {
                ArchonLogger.LogError("BuildingInfoUI: Cannot initialize with null references!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            buildingSystem = buildingSystemRef;
            provinceSelector = provinceSelectorRef;

            // Initialize UI
            InitializeUI();

            // Subscribe to province selection events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleProvinceDeselected;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to building events for auto-refresh
            buildingSystem.OnBuildingConstructed += HandleBuildingConstructed;

            // Subscribe to gold changes via EventBus (auto-disposed on OnDestroy)
            subscriptions = new CompositeDisposable();
            if (gameState?.EventBus != null)
            {
                subscriptions.Add(gameState.EventBus.Subscribe<GoldChangedEvent>(HandleGoldChanged));
            }

            isInitialized = true;

            // Hide until province selected
            HidePanel();

            ArchonLogger.Log("BuildingInfoUI: Initialized", "starter_kit");
        }

        void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                provinceSelector.OnProvinceRightClicked -= HandleProvinceDeselected;
                provinceSelector.OnSelectionCleared -= HandleSelectionCleared;
            }

            if (buildingSystem != null)
            {
                buildingSystem.OnBuildingConstructed -= HandleBuildingConstructed;
            }

            // EventBus subscriptions - auto-disposed
            subscriptions?.Dispose();
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("BuildingInfoUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("BuildingInfoUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Create panel container - positioned above units panel (bottom left)
            panelContainer = new VisualElement();
            panelContainer.name = "building-info-panel";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 10f;
            panelContainer.style.bottom = 360f; // Above units panel
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
            headerLabel = new Label(LocalizationManager.Get("UI_BUILDINGS"));
            headerLabel.style.fontSize = fontSize;
            headerLabel.style.color = textColor;
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.marginBottom = 4f;
            panelContainer.Add(headerLabel);

            // Gold bonus label
            goldBonusLabel = new Label($"{LocalizationManager.Get("UI_GOLD_BONUS")}: +0");
            goldBonusLabel.style.fontSize = fontSize - 2;
            goldBonusLabel.style.color = new Color(1f, 0.85f, 0.3f, 1f); // Gold color
            goldBonusLabel.style.marginBottom = 8f;
            panelContainer.Add(goldBonusLabel);

            // Buildings list container
            buildingsListContainer = new VisualElement();
            buildingsListContainer.name = "buildings-list";
            buildingsListContainer.style.marginBottom = 8f;
            panelContainer.Add(buildingsListContainer);

            // No buildings label
            noBuildingsLabel = new Label(LocalizationManager.Get("UI_NO_BUILDINGS"));
            noBuildingsLabel.style.fontSize = fontSize - 2;
            noBuildingsLabel.style.color = labelColor;
            noBuildingsLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            buildingsListContainer.Add(noBuildingsLabel);

            // Build buttons container
            buildButtonsContainer = new VisualElement();
            buildButtonsContainer.name = "build-buttons";
            panelContainer.Add(buildButtonsContainer);

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
            RefreshBuildingsList();
            RefreshBuildButtons();
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

        private void HandleBuildingConstructed(ushort provinceId, ushort buildingTypeId)
        {
            if (provinceId == selectedProvinceID)
            {
                RefreshBuildingsList();
                RefreshBuildButtons();
            }
        }

        private void HandleGoldChanged(GoldChangedEvent evt)
        {
            // Only refresh if panel is visible and player's gold changed
            if (selectedProvinceID == 0) return;
            if (buildingSystem == null) return;

            // Check if this is for the player's country
            var playerState = buildingSystem.PlayerState;
            if (playerState == null || evt.CountryId != playerState.PlayerCountryId) return;

            // Refresh build buttons to update enabled/disabled state
            RefreshBuildButtons();
        }

        private void RefreshBuildingsList()
        {
            if (selectedProvinceID == 0 || buildingSystem == null)
                return;

            // Clear existing entries
            foreach (var entry in buildingEntries)
            {
                buildingsListContainer.Remove(entry);
            }
            buildingEntries.Clear();

            // Update gold bonus
            int goldBonus = buildingSystem.GetProvinceGoldBonus(selectedProvinceID);
            goldBonusLabel.text = $"{LocalizationManager.Get("UI_GOLD_BONUS")}: +{goldBonus}";

            // Get buildings in province
            var buildings = buildingSystem.GetProvinceBuildings(selectedProvinceID);

            if (buildings.Count == 0)
            {
                noBuildingsLabel.style.display = DisplayStyle.Flex;
                return;
            }

            noBuildingsLabel.style.display = DisplayStyle.None;

            // Create entry for each building type
            foreach (var kvp in buildings)
            {
                var buildingType = buildingSystem.GetBuildingType(kvp.Key);
                if (buildingType == null) continue;

                var entry = CreateBuildingEntry(buildingType, kvp.Value);
                buildingsListContainer.Add(entry);
                buildingEntries.Add(entry);
            }
        }

        private VisualElement CreateBuildingEntry(BuildingType buildingType, int count)
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

            // Building name with count
            var nameLabel = new Label($"{buildingType.Name} x{count}");
            nameLabel.style.fontSize = fontSize - 1;
            nameLabel.style.color = textColor;
            entry.Add(nameLabel);

            // Effect info
            var effectLabel = new Label($"+{buildingType.GoldOutput * count} gold");
            effectLabel.style.fontSize = fontSize - 2;
            effectLabel.style.color = new Color(1f, 0.85f, 0.3f, 1f);
            entry.Add(effectLabel);

            return entry;
        }

        private void RefreshBuildButtons()
        {
            if (selectedProvinceID == 0 || buildingSystem == null)
                return;

            // Clear existing buttons
            foreach (var button in buildButtons)
            {
                buildButtonsContainer.Remove(button);
            }
            buildButtons.Clear();

            // Show/hide buttons based on province ownership
            bool isOwnedByPlayer = buildingSystem.IsProvinceOwnedByPlayer(selectedProvinceID);
            buildButtonsContainer.style.display = isOwnedByPlayer ? DisplayStyle.Flex : DisplayStyle.None;

            if (!isOwnedByPlayer)
                return;

            // Create a button for each building type
            foreach (var buildingType in buildingSystem.GetAllBuildingTypes())
            {
                var button = CreateBuildButton(buildingType);
                buildButtonsContainer.Add(button);
                buildButtons.Add(button);
            }
        }

        private Button CreateBuildButton(BuildingType buildingType)
        {
            bool canBuild = buildingSystem.CanConstruct(selectedProvinceID, buildingType.StringID, out var reason);

            var button = new Button(() => OnBuildClicked(buildingType.StringID));
            // Try to get localized building name, fallback to type name
            string buildingName = LocalizationManager.Get($"BUILDING_{buildingType.StringID.ToUpperInvariant()}");
            if (buildingName.StartsWith("BUILDING_")) buildingName = buildingType.Name; // Fallback
            button.text = $"+ {LocalizationManager.Get("UI_BUILD")} {buildingName} ({buildingType.Cost}g)";
            button.style.marginTop = 4f;
            button.style.paddingTop = 6f;
            button.style.paddingBottom = 6f;
            button.style.paddingLeft = 10f;
            button.style.paddingRight = 10f;

            // Disable if can't build
            button.SetEnabled(canBuild);
            if (!canBuild)
            {
                button.tooltip = reason;
            }

            return button;
        }

        private void OnBuildClicked(string buildingTypeId)
        {
            if (selectedProvinceID == 0)
            {
                ArchonLogger.LogWarning("BuildingInfoUI: No province selected", "starter_kit");
                return;
            }

            bool success = buildingSystem.Construct(selectedProvinceID, buildingTypeId);

            if (!success)
            {
                ArchonLogger.LogWarning("BuildingInfoUI: Failed to construct building", "starter_kit");
            }
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
