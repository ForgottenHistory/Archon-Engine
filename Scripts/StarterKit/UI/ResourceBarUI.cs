using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.UI;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Simple resource bar.
    /// Shows gold at top of screen, hidden until country selected.
    /// </summary>
    public class ResourceBarUI : StarterKitPanel
    {
        // UI Elements
        private Label goldValueLabel;
        private Label incomeLabel;

        // References
        private EconomySystem economySystem;
        private PlayerState playerState;

        public void Initialize(EconomySystem economySystemRef, PlayerState playerStateRef, GameState gameStateRef)
        {
            economySystem = economySystemRef;
            playerState = playerStateRef;

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to gold changes via EventBus (Pattern 3)
            Subscribe<GoldChangedEvent>(OnGoldChanged);

            // Subscribe to player country selection
            Subscribe<PlayerCountrySelectedEvent>(OnPlayerCountrySelected);

            // Hide until country selected
            Hide();

            ArchonLogger.Log("ResourceBarUI: Initialized", "starter_kit");
        }

        protected override void OnDestroy()
        {
            // EventBus subscriptions auto-disposed by base class
            base.OnDestroy();
        }

        protected override void CreateUI()
        {
            // Bar container at top center
            panelContainer = CreateStyledPanel("resource-bar");
            UIHelper.SetFlexRow(panelContainer, Justify.FlexStart, Align.Center);
            UIHelper.SetPadding(panelContainer, SpacingMd, SpacingLg);

            // Position at top center
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.top = 10f;
            panelContainer.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            panelContainer.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0, 0);

            // Gold icon (colored box)
            var goldIcon = CreateColorIndicator(TextGold, 20f);
            goldIcon.style.marginRight = SpacingMd;
            panelContainer.Add(goldIcon);

            // Gold label
            var goldLabel = CreateLabelText("GOLD");
            goldLabel.style.marginRight = SpacingMd;
            panelContainer.Add(goldLabel);

            // Gold value
            goldValueLabel = CreateTitle("0");
            goldValueLabel.name = "gold-value";
            goldValueLabel.style.marginRight = SpacingMd;
            panelContainer.Add(goldValueLabel);

            // Income label
            incomeLabel = new Label("(+0/month)");
            incomeLabel.name = "income-label";
            incomeLabel.style.fontSize = FontSizeLarge - 2;
            incomeLabel.style.color = TextIncome;
            panelContainer.Add(incomeLabel);
        }

        private void OnPlayerCountrySelected(PlayerCountrySelectedEvent evt)
        {
            Show();
        }

        protected override void OnShow()
        {
            UpdateDisplay();
        }

        private void OnGoldChanged(GoldChangedEvent evt)
        {
            // Only update if it's the player's gold that changed
            if (playerState != null && evt.CountryId == playerState.PlayerCountryId)
            {
                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (!IsInitialized || economySystem == null)
                return;

            if (goldValueLabel != null)
            {
                goldValueLabel.text = economySystem.Gold.ToString();
            }

            if (incomeLabel != null && playerState != null && playerState.HasPlayerCountry)
            {
                int income = economySystem.GetMonthlyIncomeInt(playerState.PlayerCountryId);
                incomeLabel.text = $"(+{income}/month)";
            }
        }

        /// <summary>
        /// Force refresh the display (called after load)
        /// </summary>
        public void RefreshDisplay()
        {
            UpdateDisplay();
        }
    }
}
