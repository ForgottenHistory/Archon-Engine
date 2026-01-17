using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Localization;
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

        // History UI (Pattern 4: Hot/Cold Data Separation demo)
        private VisualElement historyContainer;
        private Label historyHeaderLabel;
        private Label historyContentLabel;

        // Diplomacy UI
        private VisualElement diplomacyContainer;
        private Button diplomacyButton;
        private DiplomacyPanel diplomacyPanel;

        // References
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private EconomySystem economySystem;
        private PlayerState playerState;
        private TerrainRGBLookup terrainLookup;
        private ProvinceHistorySystem historySystem;

        // State
        private ushort currentProvinceID;

        public void Initialize(GameState gameStateRef, ProvinceSelector provinceSelectorRef, ProvinceHighlighter highlighterRef = null,
            EconomySystem economySystemRef = null, PlayerState playerStateRef = null, ProvinceHistorySystem historySystemRef = null,
            DiplomacyPanel diplomacyPanelRef = null)
        {
            provinceSelector = provinceSelectorRef;
            provinceHighlighter = highlighterRef;
            economySystem = economySystemRef;
            playerState = playerStateRef;
            historySystem = historySystemRef;
            diplomacyPanel = diplomacyPanelRef;

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

            provinceNameLabel = CreateHeader(LocalizationManager.Get("UI_PROVINCE_NAME"));
            provinceNameLabel.name = "province-name";
            provinceNameLabel.style.flexGrow = 1f;

            closeButton = CreateStyledButton("✕", OnCloseClicked);
            UIHelper.SetSize(closeButton, 24f, 24f);
            closeButton.style.marginLeft = SpacingMd;
            UIHelper.SetPadding(closeButton, 2f);

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

            ownerLabel = CreateText(LocalizationManager.Get("UI_OWNER"));
            ownerLabel.name = "owner-label";

            ownerContainer.Add(ownerColorIndicator);
            ownerContainer.Add(ownerLabel);
            panelContainer.Add(ownerContainer);

            // Terrain type
            terrainLabel = CreateSecondaryText($"{LocalizationManager.Get("UI_TERRAIN")}: {LocalizationManager.Get("UI_UNKNOWN")}");
            terrainLabel.name = "terrain-label";
            terrainLabel.style.marginTop = SpacingXs;
            panelContainer.Add(terrainLabel);

            // Colonization section (shown only for unowned provinces)
            colonizeContainer = new VisualElement();
            colonizeContainer.name = "colonize-container";
            colonizeContainer.style.marginTop = SpacingMd;
            colonizeContainer.style.display = DisplayStyle.None;

            string buyLandText = $"{LocalizationManager.Get("UI_BUY_LAND")} ({COLONIZE_COST} {LocalizationManager.Get("UI_GOLD").ToLower()})";
            colonizeButton = CreateStyledButton(buyLandText, OnColonizeClicked);

            colonizeContainer.Add(colonizeButton);
            panelContainer.Add(colonizeContainer);

            // History section (Pattern 4: Hot/Cold Data Separation)
            // This data is loaded on-demand when viewing a province, NOT every frame
            historyContainer = new VisualElement();
            historyContainer.name = "history-container";
            historyContainer.style.marginTop = SpacingMd;
            historyContainer.style.display = DisplayStyle.None;

            historyHeaderLabel = CreateSecondaryText(LocalizationManager.Get("UI_HISTORY"));
            historyHeaderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            historyHeaderLabel.style.marginBottom = SpacingXs;
            historyContainer.Add(historyHeaderLabel);

            historyContentLabel = CreateSecondaryText("");
            historyContentLabel.style.whiteSpace = WhiteSpace.Normal;
            historyContainer.Add(historyContentLabel);

            panelContainer.Add(historyContainer);

            // Diplomacy section (shown for provinces owned by other countries)
            diplomacyContainer = new VisualElement();
            diplomacyContainer.name = "diplomacy-container";
            diplomacyContainer.style.marginTop = SpacingMd;
            diplomacyContainer.style.display = DisplayStyle.None;

            diplomacyButton = CreateStyledButton(LocalizationManager.Get("UI_DIPLOMACY"), OnDiplomacyClicked);
            diplomacyContainer.Add(diplomacyButton);

            panelContainer.Add(diplomacyContainer);
        }

        private void OnDiplomacyClicked()
        {
            if (currentProvinceID == 0 || diplomacyPanel == null)
                return;

            ushort ownerID = gameState.ProvinceQueries.GetOwner(currentProvinceID);
            if (ownerID == 0 || ownerID == playerState?.PlayerCountryId)
                return;

            diplomacyPanel.ShowForCountry(ownerID);
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
            UpdateHistorySection();
            UpdateDiplomacyButton();
        }

        private void UpdateTerrainLabel()
        {
            if (terrainLabel == null || terrainLookup == null)
                return;

            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            string terrainName = terrainLookup.GetTerrainTypeName(terrainType);
            bool ownable = terrainLookup.IsTerrainOwnable(terrainType);

            string ownableStr = ownable ? "" : " (unownable)";
            terrainLabel.text = $"{LocalizationManager.Get("UI_TERRAIN")}: {terrainName} [T{terrainType}]{ownableStr}";
        }

        private void UpdateHistorySection()
        {
            if (historyContainer == null)
                return;

            // Pattern 4: Hot/Cold Data Separation Demo
            // This is COLD DATA - only loaded when viewing province, not every frame
            if (historySystem == null || !historySystem.HasHistory(currentProvinceID))
            {
                historyContainer.style.display = DisplayStyle.None;
                return;
            }

            var historyData = historySystem.GetProvinceHistory(currentProvinceID);
            if (historyData == null)
            {
                historyContainer.style.display = DisplayStyle.None;
                return;
            }

            var history = historyData.GetHistory();
            if (history.Count == 0)
            {
                historyContainer.style.display = DisplayStyle.None;
                return;
            }

            historyContainer.style.display = DisplayStyle.Flex;

            // Format history entries (most recent first shown at top)
            var sb = new System.Text.StringBuilder();
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var record = history[i];
                string ownerName = GetCountryNameForHistory(record.CountryId);

                if (record.IsCurrent)
                {
                    sb.AppendLine($"Day {record.StartDay}: {ownerName} (current)");
                }
                else
                {
                    sb.AppendLine($"Day {record.StartDay}-{record.EndDay}: {ownerName}");
                }
            }

            historyContentLabel.text = sb.ToString().TrimEnd();
        }

        private string GetCountryNameForHistory(ushort countryId)
        {
            if (countryId == 0)
                return LocalizationManager.Get("UI_UNOWNED");

            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem != null)
            {
                var coldData = countrySystem.GetCountryColdData(countryId);
                return coldData?.displayName ?? $"Country {countryId}";
            }
            return $"Country {countryId}";
        }

        private void UpdateDiplomacyButton()
        {
            if (diplomacyContainer == null)
                return;

            // Only show for provinces owned by other countries
            if (playerState == null || !playerState.HasPlayerCountry || diplomacyPanel == null)
            {
                diplomacyContainer.style.display = DisplayStyle.None;
                return;
            }

            ushort owner = gameState.ProvinceQueries.GetOwner(currentProvinceID);

            // Show diplomacy button if province is owned by someone else
            if (owner != 0 && owner != playerState.PlayerCountryId)
            {
                diplomacyContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                diplomacyContainer.style.display = DisplayStyle.None;
            }
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
            string buyLand = LocalizationManager.Get("UI_BUY_LAND");
            string gold = LocalizationManager.Get("UI_GOLD").ToLower();
            string notEnough = LocalizationManager.Get("UI_NOT_ENOUGH_GOLD");
            colonizeButton.text = canAfford
                ? $"{buyLand} ({COLONIZE_COST} {gold})"
                : $"{buyLand} ({COLONIZE_COST} {gold}) - {notEnough}";
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
