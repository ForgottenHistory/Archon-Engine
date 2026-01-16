using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.UI;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Base class for StarterKit UI panels.
    /// Extends ENGINE's BasePanel with:
    /// - GameState reference
    /// - EventBus subscription management (CompositeDisposable)
    /// - USS stylesheet loading
    /// - Common styling constants
    ///
    /// Usage:
    /// 1. Inherit from StarterKitPanel
    /// 2. Override CreateUI() to build your panel
    /// 3. Call base.Initialize(gameState) to set up
    /// 4. Use Subscribe&lt;T&gt;() for event subscriptions (auto-disposed)
    /// </summary>
    public abstract class StarterKitPanel : BasePanel
    {
        // Common styling - matches starterkit.uss variables
        protected static readonly Color BackgroundPanel = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        protected static readonly Color BackgroundPanelLight = new Color(0.16f, 0.16f, 0.16f, 0.9f);
        protected static readonly Color BackgroundHeader = new Color(0.2f, 0.2f, 0.3f, 1f);
        protected static readonly Color BackgroundRowAlt = new Color(0.15f, 0.15f, 0.15f, 1f);
        protected static readonly Color BackgroundRowPlayer = new Color(0.2f, 0.3f, 0.2f, 1f);
        protected static readonly Color BackgroundButton = new Color(0.25f, 0.25f, 0.25f, 1f);
        protected static readonly Color BackgroundButtonHover = new Color(0.35f, 0.35f, 0.35f, 1f);

        protected static readonly Color TextPrimary = Color.white;
        protected static readonly Color TextSecondary = new Color(0.7f, 0.7f, 0.7f, 1f);
        protected static readonly Color TextLabel = new Color(0.5f, 0.5f, 0.5f, 1f);
        protected static readonly Color TextGold = new Color(1f, 0.85f, 0.3f, 1f);
        protected static readonly Color TextIncome = new Color(0.5f, 1f, 0.5f, 1f);
        protected static readonly Color TextWarning = new Color(1f, 0.8f, 0.2f, 1f);

        protected const int FontSizeNormal = 14;
        protected const int FontSizeSmall = 12;
        protected const int FontSizeLarge = 18;
        protected const int FontSizeHeader = 20;

        protected const float SpacingXs = 4f;
        protected const float SpacingSm = 6f;
        protected const float SpacingMd = 10f;
        protected const float SpacingLg = 15f;

        protected const float RadiusSm = 3f;
        protected const float RadiusMd = 6f;
        protected const float RadiusLg = 8f;

        // References
        protected GameState gameState;
        protected CompositeDisposable subscriptions;

        /// <summary>The GameState reference.</summary>
        public GameState GameState => gameState;

        /// <summary>
        /// Initialize the panel with GameState reference.
        /// Call this from subclass Initialize() method.
        /// </summary>
        protected bool Initialize(GameState gameStateRef)
        {
            if (gameStateRef == null)
            {
                ArchonLogger.LogError($"{GetType().Name}: Cannot initialize with null GameState!", "starter_kit");
                return false;
            }

            gameState = gameStateRef;
            subscriptions = new CompositeDisposable();

            // Initialize base panel (creates UI)
            if (!InitializeBase())
            {
                return false;
            }

            // Apply USS stylesheet if UIDocument has one assigned
            // (panels can also use AddToClassList for USS styling)

            return true;
        }

        /// <summary>
        /// Subscribe to an EventBus event. Subscription is auto-disposed on OnDestroy.
        /// </summary>
        protected void Subscribe<T>(System.Action<T> handler) where T : struct, IGameEvent
        {
            if (gameState?.EventBus == null)
            {
                ArchonLogger.LogWarning($"{GetType().Name}: Cannot subscribe - EventBus is null", "starter_kit");
                return;
            }

            subscriptions.Add(gameState.EventBus.Subscribe<T>(handler));
        }

        /// <summary>
        /// Called when the panel is destroyed. Disposes event subscriptions.
        /// Override to add cleanup, but always call base.OnDestroy().
        /// </summary>
        protected virtual void OnDestroy()
        {
            subscriptions?.Dispose();
        }

        // ====================================================================
        // UI HELPER SHORTCUTS
        // ====================================================================

        /// <summary>Create a styled panel container with default StarterKit styling.</summary>
        protected VisualElement CreateStyledPanel(string name, float? minWidth = null)
        {
            var panel = UIHelper.CreatePanel(name, BackgroundPanel, SpacingMd, RadiusMd);
            UIHelper.SetPadding(panel, SpacingMd, SpacingLg);
            if (minWidth.HasValue)
                UIHelper.SetMinWidth(panel, minWidth.Value);
            return panel;
        }

        /// <summary>Create a header label with default StarterKit styling.</summary>
        protected Label CreateHeader(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeHeader, TextPrimary, FontStyle.Bold);
        }

        /// <summary>Create a title label with default StarterKit styling.</summary>
        protected Label CreateTitle(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeLarge, TextPrimary, FontStyle.Bold);
        }

        /// <summary>Create a normal text label.</summary>
        protected Label CreateText(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeNormal, TextPrimary);
        }

        /// <summary>Create a secondary text label.</summary>
        protected Label CreateSecondaryText(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeNormal, TextSecondary);
        }

        /// <summary>Create a small label text.</summary>
        protected Label CreateLabelText(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeSmall, TextLabel);
        }

        /// <summary>Create a gold-colored label.</summary>
        protected Label CreateGoldText(string text)
        {
            return UIHelper.CreateLabel(text, FontSizeNormal, TextGold);
        }

        /// <summary>Create a styled button with default StarterKit styling.</summary>
        protected Button CreateStyledButton(string text, System.Action onClick)
        {
            var button = UIHelper.CreateButton(text, onClick, FontSizeNormal, BackgroundButton);
            button.style.color = TextPrimary;
            UIHelper.AddHoverEffect(button, BackgroundButton, BackgroundButtonHover);
            return button;
        }

        /// <summary>Create a row container with flex-row layout.</summary>
        protected VisualElement CreateRow(Justify justify = Justify.FlexStart, Align align = Align.Center)
        {
            var row = new VisualElement();
            UIHelper.SetFlexRow(row, justify, align);
            return row;
        }

        /// <summary>Create a column container with flex-column layout.</summary>
        protected VisualElement CreateColumn(Justify justify = Justify.FlexStart, Align align = Align.Stretch)
        {
            var column = new VisualElement();
            UIHelper.SetFlexColumn(column, justify, align);
            return column;
        }

        /// <summary>Create a row entry with alternating background (for lists).</summary>
        protected VisualElement CreateRowEntry(bool alternate = false)
        {
            var entry = new VisualElement();
            entry.style.backgroundColor = alternate ? BackgroundRowAlt : new Color(0.2f, 0.2f, 0.2f, 0.5f);
            UIHelper.SetBorderRadius(entry, RadiusSm);
            UIHelper.SetPadding(entry, SpacingXs, SpacingSm);
            entry.style.marginBottom = SpacingXs;
            UIHelper.SetFlexRow(entry, Justify.SpaceBetween, Align.Center);
            return entry;
        }

        /// <summary>Create a color indicator box.</summary>
        protected VisualElement CreateColorIndicator(Color color, float size = 16f)
        {
            var indicator = UIHelper.CreateColorIndicator(color, size, 2f);
            indicator.style.marginRight = SpacingSm;
            return indicator;
        }
    }
}
