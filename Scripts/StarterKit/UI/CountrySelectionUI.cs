using UnityEngine;
using UnityEngine.UIElements;
using Map.Interaction;
using Core;
using Core.UI;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Simple country selection UI.
    /// Click map to select country, click Start to begin.
    /// Emits PlayerCountrySelectedEvent when started.
    /// </summary>
    public class CountrySelectionUI : StarterKitPanel
    {
        [Header("Highlight")]
        [SerializeField] private Color countryHighlightColor = new Color(1f, 0.84f, 0f, 0.5f);
        [SerializeField] private Color buttonColor = new Color(0.3f, 0.6f, 0.9f, 1f);

        [Header("Debug")]
        [SerializeField] private bool logSelection = true;

        // UI Elements
        private VisualElement contentBox;
        private Label instructionLabel;
        private Button startButton;

        // References
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private PlayerState playerState;

        // State
        private ushort selectedCountryId;
        private bool hasSelectedCountry;

        public bool HasSelectedCountry => hasSelectedCountry;
        public ushort SelectedCountryId => selectedCountryId;

        public void Initialize(GameState gameStateRef, PlayerState playerStateRef)
        {
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

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            provinceSelector.OnProvinceClicked += HandleProvinceClicked;

            if (logSelection)
            {
                ArchonLogger.Log("CountrySelectionUI: Initialized - waiting for country selection", "starter_kit");
            }
        }

        protected override void OnDestroy()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
            }

            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Container at bottom of screen, full width for centering
            panelContainer = new VisualElement();
            panelContainer.name = "country-selection-container";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 0;
            panelContainer.style.right = 0;
            panelContainer.style.bottom = 20f;
            panelContainer.style.alignItems = Align.Center;
            panelContainer.style.flexDirection = FlexDirection.Column;

            // Content box with styling
            contentBox = CreateStyledPanel("content-box");
            UIHelper.SetBorderRadius(contentBox, RadiusLg);
            UIHelper.SetPadding(contentBox, SpacingLg, 30f);
            contentBox.style.alignItems = Align.Center;

            // Instruction label
            instructionLabel = CreateHeader("Choose a country");
            instructionLabel.name = "instruction-label";
            instructionLabel.style.marginBottom = SpacingLg;
            instructionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            contentBox.Add(instructionLabel);

            // Start button
            startButton = new Button(OnStartClicked);
            startButton.name = "start-button";
            startButton.text = "Start";
            startButton.style.fontSize = FontSizeHeader - 4;
            startButton.style.color = TextPrimary;
            startButton.style.backgroundColor = buttonColor;
            UIHelper.SetPadding(startButton, 12f, 40f);
            UIHelper.SetBorderRadius(startButton, RadiusMd);
            startButton.SetEnabled(false);
            contentBox.Add(startButton);

            panelContainer.Add(contentBox);

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
            Hide();

            if (logSelection)
            {
                var countryTag = gameState.CountryQueries?.GetTag(selectedCountryId) ?? selectedCountryId.ToString();
                ArchonLogger.Log($"CountrySelectionUI: Game started as {countryTag}", "starter_kit");
            }
        }
    }
}
