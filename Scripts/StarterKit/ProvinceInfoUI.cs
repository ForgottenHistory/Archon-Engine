using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Map.Interaction;

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

        // References
        private GameState gameState;
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private bool isInitialized;

        // State
        private ushort currentProvinceID;

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, ProvinceSelector provinceSelectorRef, ProvinceHighlighter highlighterRef = null)
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

            // Initialize UI
            InitializeUI();

            // Subscribe to ProvinceSelector events
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            provinceSelector.OnProvinceRightClicked += HandleRightClick;
            provinceSelector.OnProvinceHovered += HandleProvinceHovered;
            provinceSelector.OnSelectionCleared += HandleSelectionCleared;

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

        private void OnCloseClicked()
        {
            currentProvinceID = 0;
            provinceHighlighter?.ClearHighlight();
            HidePanel();
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
