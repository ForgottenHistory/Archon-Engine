using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Localization;
using Core.Systems;
using Core.UI;
using Map.Interaction;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Building information panel
    /// Shows buildings in the selected province with option to construct new buildings.
    /// Pattern: UI Presenter Pattern - View Component
    /// </summary>
    public class BuildingInfoUI : StarterKitPanel
    {
        // UI Elements
        private Label headerLabel;
        private Label goldBonusLabel;
        private VisualElement buildingsListContainer;
        private VisualElement buildButtonsContainer;
        private Label noBuildingsLabel;

        // References
        private BuildingSystem buildingSystem;
        private ProvinceSelector provinceSelector;

        // State
        private ushort selectedProvinceID;
        private List<VisualElement> buildingEntries = new List<VisualElement>();
        private List<Button> buildButtons = new List<Button>();

        public void Initialize(GameState gameStateRef, BuildingSystem buildingSystemRef, ProvinceSelector provinceSelectorRef)
        {
            buildingSystem = buildingSystemRef;
            provinceSelector = provinceSelectorRef;

            if (buildingSystemRef == null || provinceSelectorRef == null)
            {
                ArchonLogger.LogError("BuildingInfoUI: Cannot initialize with null references!", "starter_kit");
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

            // Subscribe to building events for auto-refresh
            buildingSystem.OnBuildingConstructed += HandleBuildingConstructed;

            // Subscribe to gold changes via EventBus
            Subscribe<GoldChangedEvent>(HandleGoldChanged);

            // Hide until province selected
            Hide();

            ArchonLogger.Log("BuildingInfoUI: Initialized", "starter_kit");
        }

        protected override void OnDestroy()
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

            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Create panel container - positioned above units panel (bottom left)
            panelContainer = CreateStyledPanel("building-info-panel", minWidth: 180f);
            PositionPanel(bottom: 360f, left: 10f);

            // Header
            headerLabel = CreateTitle(LocalizationManager.Get("UI_BUILDINGS"));
            headerLabel.style.marginBottom = SpacingXs;
            panelContainer.Add(headerLabel);

            // Gold bonus label
            goldBonusLabel = CreateGoldText($"{LocalizationManager.Get("UI_GOLD_BONUS")}: +0");
            goldBonusLabel.style.fontSize = FontSizeSmall;
            goldBonusLabel.style.marginBottom = SpacingMd;
            panelContainer.Add(goldBonusLabel);

            // Buildings list container
            buildingsListContainer = new VisualElement();
            buildingsListContainer.name = "buildings-list";
            buildingsListContainer.style.marginBottom = SpacingMd;
            panelContainer.Add(buildingsListContainer);

            // No buildings label
            noBuildingsLabel = CreateLabelText(LocalizationManager.Get("UI_NO_BUILDINGS"));
            noBuildingsLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            buildingsListContainer.Add(noBuildingsLabel);

            // Build buttons container
            buildButtonsContainer = new VisualElement();
            buildButtonsContainer.name = "build-buttons";
            panelContainer.Add(buildButtonsContainer);
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
            Show();
        }

        private void HandleProvinceDeselected(ushort provinceID)
        {
            HandleSelectionCleared();
        }

        private void HandleSelectionCleared()
        {
            selectedProvinceID = 0;
            Hide();
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
            if (selectedProvinceID == 0) return;
            if (buildingSystem == null) return;

            var playerState = buildingSystem.PlayerState;
            if (playerState == null || evt.CountryId != playerState.PlayerCountryId) return;

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
            var entry = CreateRowEntry();

            // Building name with count
            var nameLabel = CreateText($"{buildingType.Name} x{count}");
            nameLabel.style.fontSize = FontSizeNormal - 1;
            entry.Add(nameLabel);

            // Effect info
            var effectLabel = CreateGoldText($"+{buildingType.GoldOutput * count} gold");
            effectLabel.style.fontSize = FontSizeSmall;
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

            string buildingName = LocalizationManager.Get($"BUILDING_{buildingType.StringID.ToUpperInvariant()}");
            if (buildingName.StartsWith("BUILDING_")) buildingName = buildingType.Name;

            var button = CreateStyledButton(
                $"+ {LocalizationManager.Get("UI_BUILD")} {buildingName} ({buildingType.Cost}g)",
                () => OnBuildClicked(buildingType.StringID));
            button.style.marginTop = SpacingXs;

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
    }
}
