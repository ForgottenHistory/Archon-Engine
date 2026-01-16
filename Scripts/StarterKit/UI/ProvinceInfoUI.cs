using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Systems;
using Core.UI;
using Map.Core;
using Map.Interaction;
using Map.Rendering.Terrain;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Province information panel (Pure View)
    /// Pattern: UI Presenter Pattern - View Component
    ///
    /// Architecture:
    /// - ProvinceInfoUI (this file) - Pure view (UI creation, show/hide)
    /// - ProvinceInfoPresenter - Presentation logic (data formatting)
    /// </summary>
    public class ProvinceInfoUI : StarterKitPanel
    {
        [Header("Highlight")]
        [SerializeField] private Color hoverHighlightColor = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private Color selectionHighlightColor = new Color(1f, 0.84f, 0f, 0.5f);

        // UI Elements
        private Label provinceNameLabel;
        private Button closeButton;
        private Label provinceIDLabel;
        private VisualElement ownerColorIndicator;
        private Label ownerLabel;
        private Label terrainLabel;

        // Colonization UI
        private VisualElement colonizeContainer;
        private Button colonizeButton;
        private const int COLONIZE_COST = 20;

        // References
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private EconomySystem economySystem;
        private PlayerState playerState;
        private TerrainRGBLookup terrainLookup;

        // State
        private ushort currentProvinceID;

        public void Initialize(GameState gameStateRef, ProvinceSelector provinceSelectorRef, ProvinceHighlighter highlighterRef = null,
            EconomySystem economySystemRef = null, PlayerState playerStateRef = null)
        {
            provinceSelector = provinceSelectorRef;
            provinceHighlighter = highlighterRef;
            economySystem = economySystemRef;
            playerState = playerStateRef;

            if (provinceSelectorRef == null)
            {
                ArchonLogger.LogError("ProvinceInfoUI: Cannot initialize with null ProvinceSelector!", "starter_kit");
                return;
            }

            // Initialize terrain lookup for ownable checks
            var mapInitializer = Object.FindFirstObjectByType<MapInitializer>();
            string dataDirectory = mapInitializer?.DataDirectory;
            terrainLookup = new TerrainRGBLookup();
            terrainLookup.Initialize(dataDirectory, false);

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to ProvinceSelector events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleRightClick;
            provinceSelector.OnProvinceHovered += HandleProvinceHovered;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to EventBus events
            Subscribe<GoldChangedEvent>(HandleGoldChanged);
            Subscribe<ProvinceOwnershipChangedEvent>(HandleOwnershipChanged);

            // Hide until province selected
            Hide();

            ArchonLogger.Log("ProvinceInfoUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!IsInitialized)
                return;

            // Escape to close
            if (Input.GetKeyDown(KeyCode.Escape) && currentProvinceID != 0)
            {
                currentProvinceID = 0;
                provinceHighlighter?.ClearHighlight();
                Hide();
            }
        }

        protected override void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                provinceSelector.OnProvinceRightClicked -= HandleRightClick;
                provinceSelector.OnProvinceHovered -= HandleProvinceHovered;
                provinceSelector.OnSelectionCleared -= HandleSelectionCleared;
            }

            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Create panel container - bottom left position
            panelContainer = CreateStyledPanel("province-info-panel", minWidth: 200f);
            PositionPanel(bottom: 10f, left: 10f);

            // Header with name and close button
            var headerContainer = CreateRow(Justify.SpaceBetween);
            headerContainer.name = "header";
            headerContainer.style.marginBottom = SpacingXs;

            provinceNameLabel = CreateHeader("Province Name");
            provinceNameLabel.name = "province-name";
            provinceNameLabel.style.flexGrow = 1f;

            closeButton = new Button(OnCloseClicked);
            closeButton.text = "X";
            UIHelper.SetSize(closeButton, 24f, 24f);
            closeButton.style.fontSize = FontSizeNormal;
            closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeButton.style.marginLeft = SpacingMd;
            UIHelper.SetPadding(closeButton, 0);

            headerContainer.Add(provinceNameLabel);
            headerContainer.Add(closeButton);
            panelContainer.Add(headerContainer);

            // Province ID
            provinceIDLabel = CreateLabelText("ID: 0");
            provinceIDLabel.name = "province-id";
            provinceIDLabel.style.marginBottom = SpacingMd;
            panelContainer.Add(provinceIDLabel);

            // Owner section (horizontal)
            var ownerContainer = CreateRow();
            ownerContainer.name = "owner-container";

            ownerColorIndicator = CreateColorIndicator(Color.gray);
            ownerColorIndicator.name = "owner-color";

            ownerLabel = CreateText("Owner");
            ownerLabel.name = "owner-label";

            ownerContainer.Add(ownerColorIndicator);
            ownerContainer.Add(ownerLabel);
            panelContainer.Add(ownerContainer);

            // Terrain type
            terrainLabel = CreateSecondaryText("Terrain: Unknown");
            terrainLabel.name = "terrain-label";
            terrainLabel.style.marginTop = SpacingXs;
            panelContainer.Add(terrainLabel);

            // Colonization section (shown only for unowned provinces)
            colonizeContainer = new VisualElement();
            colonizeContainer.name = "colonize-container";
            colonizeContainer.style.marginTop = SpacingMd;
            colonizeContainer.style.display = DisplayStyle.None;

            colonizeButton = CreateStyledButton($"Buy Land ({COLONIZE_COST} gold)", OnColonizeClicked);

            colonizeContainer.Add(colonizeButton);
            panelContainer.Add(colonizeContainer);
        }

        private void HandleProvinceClicked(ushort provinceID)
        {
            if (provinceID == 0)
            {
                Hide();
                return;
            }

            currentProvinceID = provinceID;

            // Highlight selected province
            provinceHighlighter?.HighlightProvince(provinceID, selectionHighlightColor);

            UpdatePanel();
            Show();
        }

        private void HandleProvinceHovered(ushort provinceID)
        {
            if (provinceHighlighter == null)
                return;

            // Don't override selection highlight with hover
            if (currentProvinceID != 0 && provinceID != currentProvinceID)
                return;

            if (provinceID == 0)
            {
                if (currentProvinceID == 0)
                    provinceHighlighter.ClearHighlight();
            }
            else if (currentProvinceID == 0)
            {
                provinceHighlighter.HighlightProvince(provinceID, hoverHighlightColor);
            }
        }

        private void HandleRightClick(ushort provinceID)
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            Hide();
        }

        private void HandleSelectionCleared()
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            Hide();
        }

        private void HandleGoldChanged(GoldChangedEvent evt)
        {
            if (currentProvinceID == 0) return;
            if (playerState == null || evt.CountryId != playerState.PlayerCountryId) return;
            UpdateColonizeButton();
        }

        private void HandleOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            if (currentProvinceID == 0 || evt.ProvinceId != currentProvinceID) return;
            UpdatePanel();
        }

        private void OnCloseClicked()
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            Hide();
        }

        private void OnColonizeClicked()
        {
            if (currentProvinceID == 0 || economySystem == null || playerState == null)
                return;

            if (!playerState.HasPlayerCountry)
                return;

            if (!gameState.ProvinceQueries.Exists(currentProvinceID))
            {
                ArchonLogger.LogWarning($"ProvinceInfoUI: Province {currentProvinceID} not in definition.csv", "starter_kit");
                return;
            }

            if (economySystem.Gold < COLONIZE_COST)
            {
                ArchonLogger.LogWarning($"ProvinceInfoUI: Not enough gold (need {COLONIZE_COST}, have {economySystem.Gold})", "starter_kit");
                return;
            }

            ushort owner = gameState.ProvinceQueries.GetOwner(currentProvinceID);
            if (owner != 0)
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Province is no longer unowned", "starter_kit");
                UpdatePanel();
                return;
            }

            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Cannot colonize unownable terrain", "starter_kit");
                UpdatePanel();
                return;
            }

            if (!IsAdjacentToPlayerTerritory(currentProvinceID))
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Province must border your territory", "starter_kit");
                UpdatePanel();
                return;
            }

            economySystem.RemoveGold(COLONIZE_COST);
            gameState.Provinces.SetProvinceOwner(currentProvinceID, playerState.PlayerCountryId);

            ArchonLogger.Log($"ProvinceInfoUI: Colonized province {currentProvinceID} for {COLONIZE_COST} gold", "starter_kit");
            UpdatePanel();
        }

        private void UpdatePanel()
        {
            if (!IsInitialized || currentProvinceID == 0)
                return;

            ProvinceInfoPresenter.UpdatePanelData(
                currentProvinceID,
                gameState,
                provinceNameLabel,
                provinceIDLabel,
                ownerColorIndicator,
                ownerLabel);

            UpdateTerrainLabel();
            UpdateColonizeButton();
        }

        private void UpdateTerrainLabel()
        {
            if (terrainLabel == null || terrainLookup == null)
                return;

            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            string terrainName = terrainLookup.GetTerrainTypeName(terrainType);
            bool ownable = terrainLookup.IsTerrainOwnable(terrainType);

            string ownableStr = ownable ? "" : " (unownable)";
            terrainLabel.text = $"Terrain: {terrainName} [T{terrainType}]{ownableStr}";
        }

        private void UpdateColonizeButton()
        {
            if (colonizeContainer == null)
                return;

            if (economySystem == null || playerState == null || !playerState.HasPlayerCountry)
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            if (!gameState.ProvinceQueries.Exists(currentProvinceID))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            ushort owner = gameState.ProvinceQueries.GetOwner(currentProvinceID);
            if (owner != 0)
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            if (!IsAdjacentToPlayerTerritory(currentProvinceID))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            colonizeContainer.style.display = DisplayStyle.Flex;

            bool canAfford = economySystem.Gold >= COLONIZE_COST;
            colonizeButton.SetEnabled(canAfford);
            colonizeButton.text = canAfford
                ? $"Buy Land ({COLONIZE_COST} gold)"
                : $"Buy Land ({COLONIZE_COST} gold) - Not enough gold";
        }

        private bool IsAdjacentToPlayerTerritory(ushort provinceId)
        {
            if (!playerState.HasPlayerCountry)
                return false;

            ushort playerId = playerState.PlayerCountryId;

            using (var neighbors = gameState.Adjacencies.GetNeighbors(provinceId))
            {
                for (int i = 0; i < neighbors.Length; i++)
                {
                    ushort neighborOwner = gameState.ProvinceQueries.GetOwner(neighbors[i]);
                    if (neighborOwner == playerId)
                        return true;
                }
            }

            return false;
        }
    }
}
