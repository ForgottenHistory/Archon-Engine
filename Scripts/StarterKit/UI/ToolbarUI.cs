using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.SaveLoad;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Simple toolbar with common actions.
    /// Positioned in top right corner. Shows: Ledger, Save, Load buttons.
    /// Hidden until player selects a country.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ToolbarUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color buttonColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color buttonHoverColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int fontSize = 14;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement toolbarContainer;
        private Button ledgerButton;
        private Button saveButton;
        private Button loadButton;

        // References
        private GameState gameState;
        private LedgerUI ledgerUI;
        private SaveManager saveManager;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, LedgerUI ledgerUIRef, SaveManager saveManagerRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("ToolbarUI: Already initialized!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            ledgerUI = ledgerUIRef;
            saveManager = saveManagerRef;

            InitializeUI();

            // Subscribe to player country selection to show toolbar
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(OnPlayerCountrySelected);
            }

            isInitialized = true;

            // Hide until country selected
            HideToolbar();

            ArchonLogger.Log("ToolbarUI: Initialized", "starter_kit");
        }

        private void OnPlayerCountrySelected(PlayerCountrySelectedEvent evt)
        {
            ShowToolbar();
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("ToolbarUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("ToolbarUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Create toolbar container - top right position
            toolbarContainer = new VisualElement();
            toolbarContainer.name = "toolbar";
            toolbarContainer.style.position = Position.Absolute;
            toolbarContainer.style.right = 10f;
            toolbarContainer.style.top = 10f;
            toolbarContainer.style.flexDirection = FlexDirection.Row;
            toolbarContainer.style.backgroundColor = backgroundColor;
            toolbarContainer.style.paddingTop = 6f;
            toolbarContainer.style.paddingBottom = 6f;
            toolbarContainer.style.paddingLeft = 8f;
            toolbarContainer.style.paddingRight = 8f;
            toolbarContainer.style.borderTopLeftRadius = 6f;
            toolbarContainer.style.borderTopRightRadius = 6f;
            toolbarContainer.style.borderBottomLeftRadius = 6f;
            toolbarContainer.style.borderBottomRightRadius = 6f;

            // Ledger button
            ledgerButton = CreateToolbarButton("Ledger (L)", OnLedgerClicked);
            toolbarContainer.Add(ledgerButton);

            // Save button
            saveButton = CreateToolbarButton("Save (F6)", OnSaveClicked);
            toolbarContainer.Add(saveButton);

            // Load button
            loadButton = CreateToolbarButton("Load (F7)", OnLoadClicked);
            toolbarContainer.Add(loadButton);

            rootElement.Add(toolbarContainer);
        }

        private Button CreateToolbarButton(string text, System.Action onClick)
        {
            var button = new Button(onClick);
            button.text = text;
            button.style.fontSize = fontSize;
            button.style.color = textColor;
            button.style.backgroundColor = buttonColor;
            button.style.paddingTop = 6f;
            button.style.paddingBottom = 6f;
            button.style.paddingLeft = 12f;
            button.style.paddingRight = 12f;
            button.style.marginLeft = 4f;
            button.style.marginRight = 4f;
            button.style.borderTopLeftRadius = 4f;
            button.style.borderTopRightRadius = 4f;
            button.style.borderBottomLeftRadius = 4f;
            button.style.borderBottomRightRadius = 4f;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;

            // Hover effect
            button.RegisterCallback<MouseEnterEvent>(evt => button.style.backgroundColor = buttonHoverColor);
            button.RegisterCallback<MouseLeaveEvent>(evt => button.style.backgroundColor = buttonColor);

            return button;
        }

        private void OnLedgerClicked()
        {
            ledgerUI?.TogglePanel();
        }

        private void OnSaveClicked()
        {
            if (saveManager != null)
            {
                bool success = saveManager.QuickSave();
                ArchonLogger.Log($"ToolbarUI: Quick save {(success ? "succeeded" : "failed")}", "starter_kit");
            }
            else
            {
                ArchonLogger.LogWarning("ToolbarUI: SaveManager not available", "starter_kit");
            }
        }

        private void OnLoadClicked()
        {
            if (saveManager != null)
            {
                bool success = saveManager.QuickLoad();
                ArchonLogger.Log($"ToolbarUI: Quick load {(success ? "succeeded" : "failed")}", "starter_kit");
            }
            else
            {
                ArchonLogger.LogWarning("ToolbarUI: SaveManager not available", "starter_kit");
            }
        }

        public void ShowToolbar()
        {
            if (toolbarContainer != null)
            {
                toolbarContainer.style.display = DisplayStyle.Flex;
            }
        }

        public void HideToolbar()
        {
            if (toolbarContainer != null)
            {
                toolbarContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
