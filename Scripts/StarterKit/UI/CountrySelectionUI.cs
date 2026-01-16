using UnityEngine;
using UnityEngine.UIElements;
using Map.Interaction;
using Core;

namespace StarterKit
{
    /// <summary>
    /// Simple debug country selection UI for starter kit.
    /// Click map to select country, click Start to begin.
    /// Emits PlayerCountrySelectedEvent when started.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CountrySelectionUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.7f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 24;
        [SerializeField] private Color buttonColor = new Color(0.3f, 0.6f, 0.9f, 1f);

        [Header("Highlight")]
        [SerializeField] private Color countryHighlightColor = new Color(1f, 0.84f, 0f, 0.5f);

        [Header("Debug")]
        [SerializeField] private bool logSelection = true;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement container;
        private Label instructionLabel;
        private Button startButton;

        // References
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private GameState gameState;
        private PlayerState playerState;

        // State
        private ushort selectedCountryId;
        private bool hasSelectedCountry;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public bool HasSelectedCountry => hasSelectedCountry;
        public ushort SelectedCountryId => selectedCountryId;

        public void Initialize(GameState gameStateRef, PlayerState playerStateRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("CountrySelectionUI: Already initialized!", "starter_kit");
                return;
            }

            if (gameStateRef == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: Cannot initialize with null GameState!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            playerState = playerStateRef;

            // Find engine components
            provinceSelector = FindFirstObjectByType<ProvinceSelector>();
            provinceHighlighter = FindFirstObjectByType<ProvinceHighlighter>();

            if (provinceSelector == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: ProvinceSelector not found!", "starter_kit");
                return;
            }

            if (provinceHighlighter == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: ProvinceHighlighter not found!", "starter_kit");
                return;
            }

            InitializeUI();
            provinceSelector.OnProvinceClicked += HandleProvinceClicked;

            isInitialized = true;

            if (logSelection)
            {
                ArchonLogger.Log("CountrySelectionUI: Initialized - waiting for country selection", "starter_kit");
            }
        }

        void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
            }

            if (startButton != null)
            {
                startButton.clicked -= OnStartClicked;
            }
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Container at bottom of screen
            container = new VisualElement();
            container.name = "country-selection-container";
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.right = 0;
            container.style.bottom = 20f;
            container.style.alignItems = Align.Center;
            container.style.flexDirection = FlexDirection.Column;

            // Content box
            var contentBox = new VisualElement();
            contentBox.name = "content-box";
            contentBox.style.backgroundColor = backgroundColor;
            contentBox.style.paddingTop = 15f;
            contentBox.style.paddingBottom = 15f;
            contentBox.style.paddingLeft = 30f;
            contentBox.style.paddingRight = 30f;
            contentBox.style.borderTopLeftRadius = 8f;
            contentBox.style.borderTopRightRadius = 8f;
            contentBox.style.borderBottomLeftRadius = 8f;
            contentBox.style.borderBottomRightRadius = 8f;
            contentBox.style.alignItems = Align.Center;

            // Instruction label
            instructionLabel = new Label("Choose a country");
            instructionLabel.name = "instruction-label";
            instructionLabel.style.fontSize = fontSize;
            instructionLabel.style.color = textColor;
            instructionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            instructionLabel.style.marginBottom = 20f;
            instructionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            contentBox.Add(instructionLabel);

            // Start button
            startButton = new Button(OnStartClicked);
            startButton.name = "start-button";
            startButton.text = "Start";
            startButton.style.fontSize = fontSize - 4;
            startButton.style.color = textColor;
            startButton.style.backgroundColor = buttonColor;
            startButton.style.paddingTop = 12f;
            startButton.style.paddingBottom = 12f;
            startButton.style.paddingLeft = 40f;
            startButton.style.paddingRight = 40f;
            startButton.style.borderTopLeftRadius = 6f;
            startButton.style.borderTopRightRadius = 6f;
            startButton.style.borderBottomLeftRadius = 6f;
            startButton.style.borderBottomRightRadius = 6f;
            startButton.SetEnabled(false);
            contentBox.Add(startButton);

            container.Add(contentBox);
            rootElement.Add(container);

            if (logSelection)
            {
                ArchonLogger.Log("CountrySelectionUI: UI created", "starter_kit");
            }
        }

        private void HandleProvinceClicked(ushort provinceId)
        {
            if (provinceId == 0)
            {
                if (logSelection)
                {
                    ArchonLogger.Log("CountrySelectionUI: Clicked ocean or invalid province", "starter_kit");
                }
                return;
            }

            var provinceQueries = gameState.ProvinceQueries;
            if (provinceQueries == null)
            {
                ArchonLogger.LogError("CountrySelectionUI: ProvinceQueries not available!", "starter_kit");
                return;
            }

            ushort ownerId = provinceQueries.GetOwner(provinceId);

            if (ownerId == 0)
            {
                provinceHighlighter.ClearHighlight();
                if (logSelection)
                {
                    ArchonLogger.Log($"CountrySelectionUI: Province {provinceId} has no owner", "starter_kit");
                }
                return;
            }

            // Highlight the country
            provinceHighlighter.HighlightCountry(ownerId, countryHighlightColor);

            // Update state
            selectedCountryId = ownerId;
            hasSelectedCountry = true;

            // Enable start button
            startButton?.SetEnabled(true);

            // Update label
            var countryQueries = gameState.CountryQueries;
            string countryTag = countryQueries?.GetTag(ownerId) ?? ownerId.ToString();

            if (instructionLabel != null)
            {
                instructionLabel.text = $"Selected: {countryTag}\nClick Start to begin";
            }

            if (logSelection)
            {
                ArchonLogger.Log($"CountrySelectionUI: Selected country {countryTag} (ID: {ownerId})", "starter_kit");
            }
        }

        private void OnStartClicked()
        {
            if (!hasSelectedCountry)
            {
                ArchonLogger.LogWarning("CountrySelectionUI: Start clicked but no country selected!", "starter_kit");
                return;
            }

            // Unsubscribe from clicks
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
            }

            // Clear highlight
            provinceHighlighter?.ClearHighlight();

            // Store in player state
            if (playerState != null)
            {
                playerState.SetPlayerCountry(selectedCountryId);
            }

            // Emit event
            gameState.EventBus.Emit(new PlayerCountrySelectedEvent
            {
                CountryId = selectedCountryId,
                TimeStamp = Time.time
            });

            // Hide UI
            if (container != null)
            {
                container.style.display = DisplayStyle.None;
            }

            if (logSelection)
            {
                var countryTag = gameState.CountryQueries?.GetTag(selectedCountryId) ?? selectedCountryId.ToString();
                ArchonLogger.Log($"CountrySelectionUI: Game started as {countryTag}", "starter_kit");
            }
        }
    }
}
