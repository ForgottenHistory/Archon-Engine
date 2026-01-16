using UnityEngine;
using UnityEngine.UIElements;
using Unity.Collections;
using Core;
using Core.Events;
using Core.Systems;
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
    ///
    /// Responsibilities:
    /// - Create UI structure (UI Toolkit)
    /// - Show/hide panel
    /// - Subscribe to ProvinceSelector events
    /// - Delegate display updates to presenter
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ProvinceInfoUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color labelColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        [SerializeField] private int fontSize = 14;
        [SerializeField] private int headerFontSize = 18;

        [Header("Highlight")]
        [SerializeField] private Color hoverHighlightColor = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private Color selectionHighlightColor = new Color(1f, 0.84f, 0f, 0.5f);

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement panelContainer;
        private VisualElement headerContainer;
        private Label provinceNameLabel;
        private Button closeButton;
        private Label provinceIDLabel;
        private VisualElement ownerContainer;
        private VisualElement ownerColorIndicator;
        private Label ownerLabel;
        private Label terrainLabel;

        // Colonization UI
        private VisualElement colonizeContainer;
        private Button colonizeButton;
        private const int COLONIZE_COST = 20;

        // References
        private GameState gameState;
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private EconomySystem economySystem;
        private PlayerState playerState;
        private TerrainRGBLookup terrainLookup;
        private CompositeDisposable subscriptions;
        private bool isInitialized;

        // State
        private ushort currentProvinceID;

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, ProvinceSelector provinceSelectorRef, ProvinceHighlighter highlighterRef = null,
            EconomySystem economySystemRef = null, PlayerState playerStateRef = null)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Already initialized!", "starter_kit");
                return;
            }

            if (gameStateRef == null)
            {
                ArchonLogger.LogError("ProvinceInfoUI: Cannot initialize with null GameState!", "starter_kit");
                return;
            }

            if (provinceSelectorRef == null)
            {
                ArchonLogger.LogError("ProvinceInfoUI: Cannot initialize with null ProvinceSelector!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            provinceSelector = provinceSelectorRef;
            provinceHighlighter = highlighterRef;
            economySystem = economySystemRef;
            playerState = playerStateRef;

            // Initialize terrain lookup for ownable checks (use DataDirectory from GameSettings via MapInitializer)
            var mapInitializer = Object.FindFirstObjectByType<MapInitializer>();
            string dataDirectory = mapInitializer?.DataDirectory;
            terrainLookup = new TerrainRGBLookup();
            terrainLookup.Initialize(dataDirectory, false); // Suppress logging

            // Initialize UI
            InitializeUI();

            // Subscribe to ProvinceSelector events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleRightClick;
            provinceSelector.OnProvinceHovered += HandleProvinceHovered;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

            // Subscribe to events via EventBus (auto-disposed on OnDestroy)
            subscriptions = new CompositeDisposable();
            if (gameState?.EventBus != null)
            {
                subscriptions.Add(gameState.EventBus.Subscribe<GoldChangedEvent>(HandleGoldChanged));
                subscriptions.Add(gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(HandleOwnershipChanged));
            }

            isInitialized = true;

            // Hide until province selected
            HidePanel();

            ArchonLogger.Log("ProvinceInfoUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!isInitialized)
                return;

            // Escape to close
            if (Input.GetKeyDown(KeyCode.Escape) && currentProvinceID != 0)
            {
                currentProvinceID = 0;
                provinceHighlighter?.ClearHighlight();
                HidePanel();
            }
        }

        void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                provinceSelector.OnProvinceRightClicked -= HandleRightClick;
                provinceSelector.OnProvinceHovered -= HandleProvinceHovered;
                provinceSelector.OnSelectionCleared -= HandleSelectionCleared;
            }

            // EventBus subscriptions - auto-disposed
            subscriptions?.Dispose();
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("ProvinceInfoUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("ProvinceInfoUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Create panel container - bottom left position
            panelContainer = new VisualElement();
            panelContainer.name = "province-info-panel";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 10f;
            panelContainer.style.bottom = 10f;
            panelContainer.style.backgroundColor = backgroundColor;
            panelContainer.style.paddingTop = 12f;
            panelContainer.style.paddingBottom = 12f;
            panelContainer.style.paddingLeft = 15f;
            panelContainer.style.paddingRight = 15f;
            panelContainer.style.borderTopLeftRadius = 6f;
            panelContainer.style.borderTopRightRadius = 6f;
            panelContainer.style.borderBottomLeftRadius = 6f;
            panelContainer.style.borderBottomRightRadius = 6f;
            panelContainer.style.minWidth = 200f;

            // Header with name and close button
            headerContainer = new VisualElement();
            headerContainer.name = "header";
            headerContainer.style.flexDirection = FlexDirection.Row;
            headerContainer.style.justifyContent = Justify.SpaceBetween;
            headerContainer.style.alignItems = Align.Center;
            headerContainer.style.marginBottom = 4f;

            provinceNameLabel = new Label("Province Name");
            provinceNameLabel.name = "province-name";
            provinceNameLabel.style.fontSize = headerFontSize;
            provinceNameLabel.style.color = textColor;
            provinceNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            provinceNameLabel.style.flexGrow = 1f;

            closeButton = new Button(OnCloseClicked);
            closeButton.text = "X";
            closeButton.style.width = 24f;
            closeButton.style.height = 24f;
            closeButton.style.fontSize = 14;
            closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeButton.style.marginLeft = 10f;
            closeButton.style.paddingTop = 0f;
            closeButton.style.paddingBottom = 0f;
            closeButton.style.paddingLeft = 0f;
            closeButton.style.paddingRight = 0f;

            headerContainer.Add(provinceNameLabel);
            headerContainer.Add(closeButton);
            panelContainer.Add(headerContainer);

            // Province ID
            provinceIDLabel = new Label("ID: 0");
            provinceIDLabel.name = "province-id";
            provinceIDLabel.style.fontSize = fontSize - 2;
            provinceIDLabel.style.color = labelColor;
            provinceIDLabel.style.marginBottom = 8f;
            panelContainer.Add(provinceIDLabel);

            // Owner section (horizontal)
            ownerContainer = new VisualElement();
            ownerContainer.name = "owner-container";
            ownerContainer.style.flexDirection = FlexDirection.Row;
            ownerContainer.style.alignItems = Align.Center;

            ownerColorIndicator = new VisualElement();
            ownerColorIndicator.name = "owner-color";
            ownerColorIndicator.style.width = 16f;
            ownerColorIndicator.style.height = 16f;
            ownerColorIndicator.style.borderTopLeftRadius = 2f;
            ownerColorIndicator.style.borderTopRightRadius = 2f;
            ownerColorIndicator.style.borderBottomLeftRadius = 2f;
            ownerColorIndicator.style.borderBottomRightRadius = 2f;
            ownerColorIndicator.style.marginRight = 8f;

            ownerLabel = new Label("Owner");
            ownerLabel.name = "owner-label";
            ownerLabel.style.fontSize = fontSize;
            ownerLabel.style.color = textColor;

            ownerContainer.Add(ownerColorIndicator);
            ownerContainer.Add(ownerLabel);
            panelContainer.Add(ownerContainer);

            // Terrain type
            terrainLabel = new Label("Terrain: Unknown");
            terrainLabel.name = "terrain-label";
            terrainLabel.style.fontSize = fontSize;
            terrainLabel.style.color = labelColor;
            terrainLabel.style.marginTop = 4f;
            panelContainer.Add(terrainLabel);

            // Colonization section (shown only for unowned provinces)
            colonizeContainer = new VisualElement();
            colonizeContainer.name = "colonize-container";
            colonizeContainer.style.marginTop = 12f;
            colonizeContainer.style.display = DisplayStyle.None;

            colonizeButton = new Button(OnColonizeClicked);
            colonizeButton.text = $"Buy Land ({COLONIZE_COST} gold)";
            colonizeButton.style.paddingTop = 6f;
            colonizeButton.style.paddingBottom = 6f;
            colonizeButton.style.paddingLeft = 12f;
            colonizeButton.style.paddingRight = 12f;

            colonizeContainer.Add(colonizeButton);
            panelContainer.Add(colonizeContainer);

            rootElement.Add(panelContainer);
        }

        private void HandleProvinceClicked(ushort provinceID)
        {
            if (provinceID == 0)
            {
                HidePanel();
                return;
            }

            currentProvinceID = provinceID;

            // Highlight selected province
            if (provinceHighlighter != null)
            {
                provinceHighlighter.HighlightProvince(provinceID, selectionHighlightColor);
            }

            UpdatePanel();
            ShowPanel();
        }

        private void HandleProvinceHovered(ushort provinceID)
        {
            if (provinceHighlighter == null)
                return;

            // Don't override selection highlight with hover
            if (currentProvinceID != 0 && provinceID != currentProvinceID)
            {
                // Keep selection highlighted, don't show hover
                return;
            }

            if (provinceID == 0)
            {
                // If nothing selected, clear highlight
                if (currentProvinceID == 0)
                {
                    provinceHighlighter.ClearHighlight();
                }
            }
            else if (currentProvinceID == 0)
            {
                // Only show hover if nothing selected
                provinceHighlighter.HighlightProvince(provinceID, hoverHighlightColor);
            }
        }

        private void HandleRightClick(ushort provinceID)
        {
            // Right-click closes the panel and clears highlight
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            HidePanel();
        }

        private void HandleSelectionCleared()
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            HidePanel();
        }

        private void HandleGoldChanged(GoldChangedEvent evt)
        {
            // Only refresh if panel is visible
            if (currentProvinceID == 0) return;

            // Check if this is for the player's country
            if (playerState == null || evt.CountryId != playerState.PlayerCountryId) return;

            // Update colonize button enabled state
            UpdateColonizeButton();
        }

        private void HandleOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            // Only refresh if panel is showing the changed province
            if (currentProvinceID == 0 || evt.ProvinceId != currentProvinceID) return;

            // Refresh the entire panel to show new owner
            UpdatePanel();
        }

        private void OnCloseClicked()
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            HidePanel();
        }

        private void OnColonizeClicked()
        {
            if (currentProvinceID == 0 || economySystem == null || playerState == null)
                return;

            if (!playerState.HasPlayerCountry)
                return;

            // Check if province exists (all provinces should be pre-loaded from definition.csv)
            if (!gameState.ProvinceQueries.Exists(currentProvinceID))
            {
                ArchonLogger.LogWarning($"ProvinceInfoUI: Province {currentProvinceID} not in definition.csv", "starter_kit");
                return;
            }

            // Check if can afford
            if (economySystem.Gold < COLONIZE_COST)
            {
                ArchonLogger.LogWarning($"ProvinceInfoUI: Not enough gold (need {COLONIZE_COST}, have {economySystem.Gold})", "starter_kit");
                return;
            }

            // Check if still unowned
            ushort owner = gameState.ProvinceQueries.GetOwner(currentProvinceID);
            if (owner != 0)
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Province is no longer unowned", "starter_kit");
                UpdatePanel();
                return;
            }

            // Check if province terrain is ownable
            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Cannot colonize unownable terrain", "starter_kit");
                UpdatePanel();
                return;
            }

            // Check if province borders player territory
            if (!IsAdjacentToPlayerTerritory(currentProvinceID))
            {
                ArchonLogger.LogWarning("ProvinceInfoUI: Province must border your territory", "starter_kit");
                UpdatePanel();
                return;
            }

            // Deduct gold and transfer ownership
            economySystem.RemoveGold(COLONIZE_COST);
            gameState.Provinces.SetProvinceOwner(currentProvinceID, playerState.PlayerCountryId);

            ArchonLogger.Log($"ProvinceInfoUI: Colonized province {currentProvinceID} for {COLONIZE_COST} gold", "starter_kit");

            // Update panel to reflect new ownership
            UpdatePanel();
        }

        private void UpdatePanel()
        {
            if (!isInitialized || currentProvinceID == 0)
                return;

            // DELEGATE: Update panel data via presenter
            ProvinceInfoPresenter.UpdatePanelData(
                currentProvinceID,
                gameState,
                provinceNameLabel,
                provinceIDLabel,
                ownerColorIndicator,
                ownerLabel);

            // Update terrain label
            UpdateTerrainLabel();

            // Update colonization button visibility
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

            // Only show if we have economy system and player state
            if (economySystem == null || playerState == null || !playerState.HasPlayerCountry)
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            // Check if province actually exists (not ocean/invalid pixel)
            if (!gameState.ProvinceQueries.Exists(currentProvinceID))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            // Check if province is unowned
            ushort owner = gameState.ProvinceQueries.GetOwner(currentProvinceID);
            if (owner != 0)
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            // Check if province terrain is ownable (e.g., not ocean/water)
            ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
            if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            // Check if province borders player territory
            if (!IsAdjacentToPlayerTerritory(currentProvinceID))
            {
                colonizeContainer.style.display = DisplayStyle.None;
                return;
            }

            // Show colonize button for unowned land provinces adjacent to player
            colonizeContainer.style.display = DisplayStyle.Flex;

            // Update button state based on gold
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

            // Get neighbors of target province and check if any is owned by player
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
