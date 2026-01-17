using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Data;
using Core.Diplomacy;
using Core.Localization;
using Core.Systems;
using Core.UI;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Diplomacy panel showing relations with a selected country.
    ///
    /// Demonstrates ENGINE diplomacy system:
    /// - Opinion display with color gradient
    /// - War/peace status
    /// - Treaty status (alliance, NAP, etc.)
    /// - Declare War / Make Peace actions
    ///
    /// Architecture:
    /// - ENGINE: DiplomacySystem provides mechanism (war state, opinion calc)
    /// - GAME: StarterKit provides policy (UI, when to allow actions)
    /// </summary>
    public class DiplomacyPanel : StarterKitPanel
    {
        // Colors for opinion gradient
        private static readonly Color OpinionHostile = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color OpinionNeutral = new Color(0.7f, 0.7f, 0.3f, 1f);
        private static readonly Color OpinionFriendly = new Color(0.2f, 0.8f, 0.2f, 1f);
        private static readonly Color WarColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color PeaceColor = new Color(0.3f, 0.7f, 0.3f, 1f);

        // UI Elements
        private Label headerLabel;
        private Label opinionLabel;
        private VisualElement opinionBar;
        private VisualElement opinionFill;
        private Label statusLabel;
        private Label treatiesLabel;
        private Button actionButton;
        private Button allianceButton;
        private Button napButton;
        private Button guaranteeButton;
        private Button militaryAccessButton;
        private Button sendGiftButton;
        private Button closeButton;

        // Constants
        private const int GiftCost = 10;
        private const int GiftOpinionBonus = 50;
        private const int GiftDecayPerMonth = 1; // Decays 1 opinion per month

        // State
        private ushort targetCountryId;
        private PlayerState playerState;
        private EconomySystem economySystem;

        public void Initialize(GameState gameStateRef, PlayerState playerStateRef, EconomySystem economySystemRef = null)
        {
            playerState = playerStateRef;
            economySystem = economySystemRef;

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to diplomacy events
            Subscribe<DiplomacyWarDeclaredEvent>(OnWarDeclared);
            Subscribe<DiplomacyPeaceMadeEvent>(OnPeaceMade);
            Subscribe<AllianceFormedEvent>(OnAllianceChanged);
            Subscribe<AllianceBrokenEvent>(OnAllianceChanged);

            // Subscribe to gold changes for gift button
            Subscribe<GoldChangedEvent>(OnGoldChanged);

            // Hide until opened
            Hide();

            ArchonLogger.Log("DiplomacyPanel: Initialized", "starter_kit");
        }

        protected override void CreateUI()
        {
            // Main container
            panelContainer = CreateStyledPanel("diplomacy-panel", 280f);
            UIHelper.SetFlexColumn(panelContainer, Justify.FlexStart, Align.Stretch);
            PositionPanel(top: 100f, right: 10f);

            // Header
            headerLabel = CreateHeader(LocalizationManager.Get("UI_DIPLOMACY"));
            headerLabel.style.marginBottom = SpacingMd;
            panelContainer.Add(headerLabel);

            // Opinion section
            var opinionSection = CreateColumn();
            opinionSection.style.marginBottom = SpacingMd;

            var opinionRow = CreateRow(Justify.SpaceBetween);
            var opinionLabelText = CreateLabelText(LocalizationManager.Get("UI_OPINION"));
            opinionLabel = CreateText("+0");
            opinionRow.Add(opinionLabelText);
            opinionRow.Add(opinionLabel);
            opinionSection.Add(opinionRow);

            // Opinion bar
            opinionBar = new VisualElement();
            opinionBar.style.height = 8f;
            opinionBar.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            UIHelper.SetBorderRadius(opinionBar, RadiusSm);
            opinionBar.style.marginTop = SpacingXs;

            opinionFill = new VisualElement();
            opinionFill.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            opinionFill.style.width = new StyleLength(new Length(50, LengthUnit.Percent));
            opinionFill.style.backgroundColor = OpinionNeutral;
            UIHelper.SetBorderRadius(opinionFill, RadiusSm);
            opinionBar.Add(opinionFill);

            opinionSection.Add(opinionBar);
            panelContainer.Add(opinionSection);

            // Status section
            var statusRow = CreateRow(Justify.SpaceBetween);
            statusRow.style.marginBottom = SpacingSm;
            var statusLabelText = CreateLabelText(LocalizationManager.Get("UI_STATUS"));
            statusLabel = CreateText(LocalizationManager.Get("UI_AT_PEACE"));
            statusRow.Add(statusLabelText);
            statusRow.Add(statusLabel);
            panelContainer.Add(statusRow);

            // Treaties section
            var treatiesRow = CreateRow(Justify.SpaceBetween);
            treatiesRow.style.marginBottom = SpacingMd;
            var treatiesLabelText = CreateLabelText(LocalizationManager.Get("UI_TREATIES"));
            treatiesLabel = CreateSecondaryText(LocalizationManager.Get("UI_NONE"));
            treatiesRow.Add(treatiesLabelText);
            treatiesRow.Add(treatiesLabel);
            panelContainer.Add(treatiesRow);

            // Separator
            var separator = new VisualElement();
            separator.style.height = 1f;
            separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            separator.style.marginBottom = SpacingMd;
            panelContainer.Add(separator);

            // Treaty actions section
            var treatyActionsLabel = CreateLabelText(LocalizationManager.Get("UI_ACTIONS"));
            treatyActionsLabel.style.marginBottom = SpacingSm;
            panelContainer.Add(treatyActionsLabel);

            // War/Peace button row
            var warRow = CreateRow(Justify.FlexStart);
            warRow.style.marginBottom = SpacingSm;
            actionButton = CreateStyledButton(LocalizationManager.Get("UI_DECLARE_WAR"), OnActionClicked);
            actionButton.style.flexGrow = 1f;
            warRow.Add(actionButton);
            panelContainer.Add(warRow);

            // Treaty buttons row 1: Alliance and NAP
            var treatyRow1 = CreateRow(Justify.SpaceBetween);
            treatyRow1.style.marginBottom = SpacingSm;

            allianceButton = CreateStyledButton(LocalizationManager.Get("UI_FORM_ALLIANCE"), OnAllianceClicked);
            allianceButton.style.flexGrow = 1f;
            allianceButton.style.marginRight = SpacingSm;
            treatyRow1.Add(allianceButton);

            napButton = CreateStyledButton(LocalizationManager.Get("UI_FORM_NAP"), OnNapClicked);
            napButton.style.flexGrow = 1f;
            treatyRow1.Add(napButton);

            panelContainer.Add(treatyRow1);

            // Treaty buttons row 2: Guarantee and Military Access
            var treatyRow2 = CreateRow(Justify.SpaceBetween);
            treatyRow2.style.marginBottom = SpacingMd;

            guaranteeButton = CreateStyledButton(LocalizationManager.Get("UI_GUARANTEE"), OnGuaranteeClicked);
            guaranteeButton.style.flexGrow = 1f;
            guaranteeButton.style.marginRight = SpacingSm;
            treatyRow2.Add(guaranteeButton);

            militaryAccessButton = CreateStyledButton(LocalizationManager.Get("UI_GRANT_ACCESS"), OnMilitaryAccessClicked);
            militaryAccessButton.style.flexGrow = 1f;
            treatyRow2.Add(militaryAccessButton);

            panelContainer.Add(treatyRow2);

            // Gift row
            var giftRow = CreateRow(Justify.FlexStart);
            giftRow.style.marginBottom = SpacingMd;
            sendGiftButton = CreateStyledButton(LocalizationManager.Get("UI_SEND_GIFT"), OnSendGiftClicked);
            sendGiftButton.style.flexGrow = 1f;
            giftRow.Add(sendGiftButton);
            panelContainer.Add(giftRow);

            // Close button
            var closeRow = CreateRow(Justify.FlexEnd);
            closeButton = CreateStyledButton(LocalizationManager.Get("UI_CLOSE"), Hide);
            closeRow.Add(closeButton);
            panelContainer.Add(closeRow);
        }

        /// <summary>
        /// Show the panel for a specific country.
        /// </summary>
        public void ShowForCountry(ushort countryId)
        {
            if (countryId == 0 || !playerState.HasPlayerCountry)
            {
                Hide();
                return;
            }

            // Don't show diplomacy with yourself
            if (countryId == playerState.PlayerCountryId)
            {
                Hide();
                return;
            }

            targetCountryId = countryId;
            UpdateDisplay();
            Show();
        }

        private void UpdateDisplay()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null)
                return;

            var diplomacy = gameState.Diplomacy;
            var countrySystem = gameState.Countries;
            ushort playerId = playerState.PlayerCountryId;

            // Get country name - use localization like ProvinceInfoPresenter
            string ownerTag = gameState.CountryQueries.GetTag(targetCountryId);
            string countryName = LocalizationManager.Get(ownerTag);
            string relationsWith = LocalizationManager.Get("UI_RELATIONS_WITH");
            headerLabel.text = $"{relationsWith} {countryName}";

            // Get current tick for opinion calculation
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            // Get opinion
            var opinion = diplomacy.GetOpinion(playerId, targetCountryId, currentTick);
            int opinionInt = opinion.ToInt();

            // Update opinion display
            string opinionSign = opinionInt >= 0 ? "+" : "";
            opinionLabel.text = $"{opinionSign}{opinionInt}";
            opinionLabel.style.color = GetOpinionColor(opinionInt);

            // Update opinion bar (0-100% where -100 = 0%, 0 = 50%, +100 = 100%)
            float fillPercent = Mathf.Clamp01((opinionInt + 100f) / 200f) * 100f;
            opinionFill.style.width = new StyleLength(new Length(fillPercent, LengthUnit.Percent));
            opinionFill.style.backgroundColor = GetOpinionColor(opinionInt);

            // Check treaties first (needed for war button logic)
            bool isAllied = diplomacy.AreAllied(playerId, targetCountryId);
            bool hasNap = diplomacy.HasNonAggressionPact(playerId, targetCountryId);

            // Check war status
            bool atWar = diplomacy.IsAtWar(playerId, targetCountryId);
            if (atWar)
            {
                statusLabel.text = LocalizationManager.Get("UI_AT_WAR");
                statusLabel.style.color = WarColor;
                actionButton.text = LocalizationManager.Get("UI_MAKE_PEACE");
                actionButton.SetEnabled(true);
            }
            else
            {
                statusLabel.text = LocalizationManager.Get("UI_AT_PEACE");
                statusLabel.style.color = PeaceColor;
                actionButton.text = LocalizationManager.Get("UI_DECLARE_WAR");
                // Can't declare war on allies or countries with NAP
                actionButton.SetEnabled(!isAllied && !hasNap);
            }

            // Check treaties and update display + button text
            var treaties = new System.Collections.Generic.List<string>();

            if (isAllied)
                treaties.Add(LocalizationManager.Get("UI_ALLIED"));
            allianceButton.text = isAllied
                ? LocalizationManager.Get("UI_BREAK_ALLIANCE")
                : LocalizationManager.Get("UI_FORM_ALLIANCE");
            // Can't form alliance while at war
            allianceButton.SetEnabled(!atWar || isAllied);

            if (hasNap)
                treaties.Add(LocalizationManager.Get("UI_NAP"));
            napButton.text = hasNap
                ? LocalizationManager.Get("UI_BREAK_NAP")
                : LocalizationManager.Get("UI_FORM_NAP");
            // Can't form NAP while at war
            napButton.SetEnabled(!atWar || hasNap);

            bool isGuaranteeing = diplomacy.IsGuaranteeing(playerId, targetCountryId);
            if (isGuaranteeing)
                treaties.Add(LocalizationManager.Get("UI_GUARANTEEING"));
            guaranteeButton.text = isGuaranteeing
                ? LocalizationManager.Get("UI_REVOKE_GUARANTEE")
                : LocalizationManager.Get("UI_GUARANTEE");
            // Can't guarantee while at war
            guaranteeButton.SetEnabled(!atWar || isGuaranteeing);

            if (diplomacy.IsGuaranteeing(targetCountryId, playerId))
                treaties.Add(LocalizationManager.Get("UI_GUARANTEED_BY"));

            bool hasGrantedAccess = diplomacy.HasMilitaryAccess(playerId, targetCountryId);
            if (hasGrantedAccess)
                treaties.Add(LocalizationManager.Get("UI_GRANTING_ACCESS"));
            if (diplomacy.HasMilitaryAccess(targetCountryId, playerId))
                treaties.Add(LocalizationManager.Get("UI_MIL_ACCESS"));
            militaryAccessButton.text = hasGrantedAccess
                ? LocalizationManager.Get("UI_REVOKE_ACCESS")
                : LocalizationManager.Get("UI_GRANT_ACCESS");
            // Can't grant access while at war
            militaryAccessButton.SetEnabled(!atWar || hasGrantedAccess);

            if (treaties.Count > 0)
            {
                treatiesLabel.text = string.Join(", ", treaties);
                treatiesLabel.style.color = TextPrimary;
            }
            else
            {
                treatiesLabel.text = LocalizationManager.Get("UI_NONE");
                treatiesLabel.style.color = TextSecondary;
            }

            // Update gift button state
            UpdateGiftButton();
        }

        private Color GetOpinionColor(int opinion)
        {
            // Lerp between hostile (-100) -> neutral (0) -> friendly (+100)
            if (opinion < 0)
            {
                float t = (opinion + 100f) / 100f; // 0 at -100, 1 at 0
                return Color.Lerp(OpinionHostile, OpinionNeutral, t);
            }
            else
            {
                float t = opinion / 100f; // 0 at 0, 1 at +100
                return Color.Lerp(OpinionNeutral, OpinionFriendly, t);
            }
        }

        private void OnActionClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            bool atWar = diplomacy.IsAtWar(playerId, targetCountryId);

            if (atWar)
            {
                // Make peace
                diplomacy.MakePeace(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player made peace with country {targetCountryId}", "starter_kit");
            }
            else
            {
                // Declare war - also breaks guarantee and military access
                if (diplomacy.IsGuaranteeing(playerId, targetCountryId))
                {
                    diplomacy.RevokeGuarantee(playerId, targetCountryId, currentTick);
                }
                if (diplomacy.HasMilitaryAccess(playerId, targetCountryId))
                {
                    diplomacy.RevokeMilitaryAccess(playerId, targetCountryId, currentTick);
                }

                diplomacy.DeclareWar(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player declared war on country {targetCountryId}", "starter_kit");
            }

            UpdateDisplay();
        }

        // Event handlers - update display when diplomacy changes
        private void OnWarDeclared(DiplomacyWarDeclaredEvent evt)
        {
            if (evt.attackerID == playerState.PlayerCountryId || evt.defenderID == playerState.PlayerCountryId)
            {
                if (evt.attackerID == targetCountryId || evt.defenderID == targetCountryId)
                {
                    UpdateDisplay();
                }
            }
        }

        private void OnPeaceMade(DiplomacyPeaceMadeEvent evt)
        {
            if (evt.country1 == playerState.PlayerCountryId || evt.country2 == playerState.PlayerCountryId)
            {
                if (evt.country1 == targetCountryId || evt.country2 == targetCountryId)
                {
                    UpdateDisplay();
                }
            }
        }

        private void OnAllianceChanged(AllianceFormedEvent evt)
        {
            if ((evt.country1 == playerState.PlayerCountryId && evt.country2 == targetCountryId) ||
                (evt.country2 == playerState.PlayerCountryId && evt.country1 == targetCountryId))
            {
                UpdateDisplay();
            }
        }

        private void OnAllianceChanged(AllianceBrokenEvent evt)
        {
            if ((evt.country1 == playerState.PlayerCountryId && evt.country2 == targetCountryId) ||
                (evt.country2 == playerState.PlayerCountryId && evt.country1 == targetCountryId))
            {
                UpdateDisplay();
            }
        }

        private void OnGoldChanged(GoldChangedEvent evt)
        {
            // Only update if panel is visible and it's the player's gold
            if (!IsVisible || playerState == null || !playerState.HasPlayerCountry)
                return;

            if (evt.CountryId == playerState.PlayerCountryId)
            {
                UpdateGiftButton();
            }
        }

        // Treaty action handlers
        private void OnAllianceClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            if (diplomacy.AreAllied(playerId, targetCountryId))
            {
                diplomacy.BreakAlliance(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player broke alliance with country {targetCountryId}", "starter_kit");
            }
            else
            {
                diplomacy.FormAlliance(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player formed alliance with country {targetCountryId}", "starter_kit");
            }

            UpdateDisplay();
        }

        private void OnNapClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            if (diplomacy.HasNonAggressionPact(playerId, targetCountryId))
            {
                diplomacy.BreakNonAggressionPact(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player broke NAP with country {targetCountryId}", "starter_kit");
            }
            else
            {
                diplomacy.FormNonAggressionPact(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player formed NAP with country {targetCountryId}", "starter_kit");
            }

            UpdateDisplay();
        }

        private void OnGuaranteeClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            if (diplomacy.IsGuaranteeing(playerId, targetCountryId))
            {
                diplomacy.RevokeGuarantee(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player revoked guarantee of country {targetCountryId}", "starter_kit");
            }
            else
            {
                diplomacy.GuaranteeIndependence(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player guaranteed independence of country {targetCountryId}", "starter_kit");
            }

            UpdateDisplay();
        }

        private void OnMilitaryAccessClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            // Player grants access to target country
            if (diplomacy.HasMilitaryAccess(playerId, targetCountryId))
            {
                diplomacy.RevokeMilitaryAccess(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player revoked military access from country {targetCountryId}", "starter_kit");
            }
            else
            {
                diplomacy.GrantMilitaryAccess(playerId, targetCountryId, currentTick);
                ArchonLogger.Log($"DiplomacyPanel: Player granted military access to country {targetCountryId}", "starter_kit");
            }

            UpdateDisplay();
        }

        private void OnSendGiftClicked()
        {
            if (targetCountryId == 0 || gameState?.Diplomacy == null || !playerState.HasPlayerCountry)
                return;

            // Check if player can afford the gift
            if (economySystem == null || economySystem.Gold < GiftCost)
            {
                ArchonLogger.Log($"DiplomacyPanel: Not enough gold to send gift (need {GiftCost})", "starter_kit");
                return;
            }

            var diplomacy = gameState.Diplomacy;
            ushort playerId = playerState.PlayerCountryId;
            int currentTick = (int)(gameState.GetComponent<TimeManager>()?.CurrentTick ?? 0);

            // Remove gold from player
            economySystem.RemoveGold(GiftCost);

            // Add opinion modifier that decays over time
            // Decay rate: 1 per month = 30 ticks per point, so full decay = 25 * 30 = 750 ticks
            var giftModifier = new OpinionModifier
            {
                modifierTypeID = 1, // Generic "gift" modifier type
                value = FixedPoint64.FromInt(GiftOpinionBonus),
                appliedTick = currentTick,
                decayRate = GiftOpinionBonus * 30 // Decays ~1 point per month (30 days)
            };

            diplomacy.AddOpinionModifier(targetCountryId, playerId, giftModifier, currentTick);

            ArchonLogger.Log($"DiplomacyPanel: Player sent gift to country {targetCountryId} (+{GiftOpinionBonus} opinion, cost {GiftCost} gold)", "starter_kit");

            UpdateDisplay();
        }

        private void UpdateGiftButton()
        {
            if (sendGiftButton == null)
                return;

            bool canAfford = economySystem != null && economySystem.Gold >= GiftCost;
            sendGiftButton.SetEnabled(canAfford);

            string buttonText = LocalizationManager.Get("UI_SEND_GIFT");
            sendGiftButton.text = $"{buttonText} ({GiftCost}g)";
        }
    }
}
