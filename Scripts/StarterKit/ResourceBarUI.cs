using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;

namespace StarterKit
{
    /// <summary>
    /// Simple resource bar for StarterKit.
    /// Shows gold at top of screen, hidden until country selected.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ResourceBarUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f, 1f);
        [SerializeField] private Color incomeColor = new Color(0.5f, 1f, 0.5f, 1f);
        [SerializeField] private int fontSize = 18;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement barContainer;
        private Label goldValueLabel;
        private Label incomeLabel;

        // References
        private EconomySystem economySystem;
        private PlayerState playerState;
        private GameState gameState;
        private CompositeDisposable subscriptions;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;

        public void Initialize(EconomySystem economySystemRef, PlayerState playerStateRef, GameState gameStateRef = null)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("ResourceBarUI: Already initialized!", "starter_kit");
                return;
            }

            economySystem = economySystemRef;
            playerState = playerStateRef;
            gameState = gameStateRef ?? GameState.Instance;

            InitializeUI();

            // Subscribe to gold changes (C# event - manual cleanup)
            if (economySystem != null)
            {
                economySystem.OnGoldChanged += OnGoldChanged;
            }

            // Subscribe to player country selection (token auto-disposed on OnDestroy)
            subscriptions = new CompositeDisposable();
            if (gameState?.EventBus != null)
            {
                subscriptions.Add(gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(OnPlayerCountrySelected));
            }

            isInitialized = true;

            // Hide until country selected
            HideBar();

            ArchonLogger.Log("ResourceBarUI: Initialized", "starter_kit");
        }

        void OnDestroy()
        {
            // C# event - manual cleanup
            if (economySystem != null)
            {
                economySystem.OnGoldChanged -= OnGoldChanged;
            }

            // EventBus subscriptions - auto-disposed
            subscriptions?.Dispose();
        }

        private void OnPlayerCountrySelected(PlayerCountrySelectedEvent evt)
        {
            ShowBar();
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("ResourceBarUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("ResourceBarUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Bar container at top center
            barContainer = new VisualElement();
            barContainer.name = "resource-bar";
            barContainer.style.position = Position.Absolute;
            barContainer.style.top = 10f;
            barContainer.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            barContainer.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0, 0);
            barContainer.style.backgroundColor = backgroundColor;
            barContainer.style.paddingTop = 10f;
            barContainer.style.paddingBottom = 10f;
            barContainer.style.paddingLeft = 20f;
            barContainer.style.paddingRight = 20f;
            barContainer.style.borderTopLeftRadius = 6f;
            barContainer.style.borderTopRightRadius = 6f;
            barContainer.style.borderBottomLeftRadius = 6f;
            barContainer.style.borderBottomRightRadius = 6f;
            barContainer.style.flexDirection = FlexDirection.Row;
            barContainer.style.alignItems = Align.Center;

            // Gold icon (colored box)
            var goldIcon = new VisualElement();
            goldIcon.name = "gold-icon";
            goldIcon.style.width = 20f;
            goldIcon.style.height = 20f;
            goldIcon.style.backgroundColor = goldColor;
            goldIcon.style.borderTopLeftRadius = 3f;
            goldIcon.style.borderTopRightRadius = 3f;
            goldIcon.style.borderBottomLeftRadius = 3f;
            goldIcon.style.borderBottomRightRadius = 3f;
            goldIcon.style.marginRight = 10f;
            barContainer.Add(goldIcon);

            // Gold label
            var goldLabel = new Label("GOLD");
            goldLabel.style.fontSize = fontSize - 4;
            goldLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            goldLabel.style.marginRight = 10f;
            barContainer.Add(goldLabel);

            // Gold value
            goldValueLabel = new Label("0");
            goldValueLabel.name = "gold-value";
            goldValueLabel.style.fontSize = fontSize;
            goldValueLabel.style.color = textColor;
            goldValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            goldValueLabel.style.marginRight = 10f;
            barContainer.Add(goldValueLabel);

            // Income label
            incomeLabel = new Label("(+0/month)");
            incomeLabel.name = "income-label";
            incomeLabel.style.fontSize = fontSize - 2;
            incomeLabel.style.color = incomeColor;
            barContainer.Add(incomeLabel);

            rootElement.Add(barContainer);
        }

        private void OnGoldChanged(int oldValue, int newValue)
        {
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (!isInitialized || economySystem == null)
                return;

            if (goldValueLabel != null)
            {
                goldValueLabel.text = economySystem.Gold.ToString();
            }

            if (incomeLabel != null)
            {
                int income = economySystem.GetMonthlyIncome();
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

        public void ShowBar()
        {
            if (barContainer != null)
            {
                barContainer.style.display = DisplayStyle.Flex;
                UpdateDisplay();
            }
        }

        public void HideBar()
        {
            if (barContainer != null)
            {
                barContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
