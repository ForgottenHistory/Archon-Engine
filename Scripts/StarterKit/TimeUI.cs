using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Systems;
using Core.Localization;

namespace StarterKit
{
    /// <summary>
    /// Simple time control UI for StarterKit.
    /// Shows date/time, pause button, and speed buttons.
    /// Positioned in top-left corner.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TimeUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color activeSpeedColor = new Color(0.3f, 0.7f, 0.3f, 1f);
        [SerializeField] private int fontSize = 16;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement container;
        private Label dateTimeLabel;
        private Button pauseButton;
        private VisualElement speedButtonContainer;
        private Button[] speedButtons;

        // Configuration
        private int[] speeds = { 1, 2, 3, 4, 5, 10, 50, 100 }; // Default speeds

        // References
        private TimeManager timeManager;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;

        public void Initialize(TimeManager timeManagerRef, int[] customSpeeds = null)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("TimeUI: Already initialized!", "starter_kit");
                return;
            }

            if (timeManagerRef == null)
            {
                ArchonLogger.LogError("TimeUI: Cannot initialize with null TimeManager!", "starter_kit");
                return;
            }

            timeManager = timeManagerRef;

            if (customSpeeds != null && customSpeeds.Length > 0)
                speeds = customSpeeds;

            InitializeUI();

            isInitialized = true;

            // Hide until country selected
            HideUI();

            ArchonLogger.Log("TimeUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!isInitialized || timeManager == null)
                return;

            UpdateDisplay();
        }

        void OnDestroy()
        {
            // Clean up button callbacks
            if (pauseButton != null)
                pauseButton.clicked -= OnPauseClicked;

            if (speedButtons != null)
            {
                for (int i = 0; i < speedButtons.Length; i++)
                {
                    int level = i + 1;
                    speedButtons[i].clicked -= () => OnSpeedClicked(level);
                }
            }
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("TimeUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("TimeUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Container at top-left
            container = new VisualElement();
            container.name = "time-ui";
            container.style.position = Position.Absolute;
            container.style.top = 10f;
            container.style.left = 10f;
            container.style.backgroundColor = backgroundColor;
            container.style.paddingTop = 10f;
            container.style.paddingBottom = 10f;
            container.style.paddingLeft = 15f;
            container.style.paddingRight = 15f;
            container.style.borderTopLeftRadius = 6f;
            container.style.borderTopRightRadius = 6f;
            container.style.borderBottomLeftRadius = 6f;
            container.style.borderBottomRightRadius = 6f;
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.FlexStart;

            // Date/time label
            dateTimeLabel = new Label("0001.01.01 00:00");
            dateTimeLabel.name = "datetime-label";
            dateTimeLabel.style.fontSize = fontSize + 2;
            dateTimeLabel.style.color = textColor;
            dateTimeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            dateTimeLabel.style.marginBottom = 10f;
            container.Add(dateTimeLabel);

            // Pause button
            pauseButton = new Button(OnPauseClicked);
            pauseButton.name = "pause-button";
            pauseButton.text = LocalizationManager.Get("UI_PAUSE");
            pauseButton.style.marginBottom = 8f;
            pauseButton.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            container.Add(pauseButton);

            // Speed buttons container
            speedButtonContainer = new VisualElement();
            speedButtonContainer.name = "speed-buttons";
            speedButtonContainer.style.flexDirection = FlexDirection.Row;
            speedButtonContainer.style.flexWrap = Wrap.Wrap;

            // Create speed buttons from speeds array
            speedButtons = new Button[speeds.Length];

            for (int i = 0; i < speeds.Length; i++)
            {
                int speed = speeds[i];

                var btn = new Button();
                btn.text = $"{speed}x";
                btn.style.marginRight = 4f;
                btn.style.marginBottom = 4f;
                btn.style.minWidth = 40f;
                btn.clicked += () => OnSpeedClicked(speed);

                speedButtons[i] = btn;
                speedButtonContainer.Add(btn);
            }

            container.Add(speedButtonContainer);
            rootElement.Add(container);
        }

        private void UpdateDisplay()
        {
            // Update date/time
            if (dateTimeLabel != null)
            {
                var gt = timeManager.GetCurrentGameTime();
                dateTimeLabel.text = $"{gt.Year:D4}.{gt.Month:D2}.{gt.Day:D2} {gt.Hour:D2}:00";
            }

            // Update pause button
            if (pauseButton != null)
            {
                pauseButton.text = timeManager.IsPaused
                    ? LocalizationManager.Get("UI_RESUME")
                    : LocalizationManager.Get("UI_PAUSE");
            }

            // Highlight active speed button
            if (speedButtons != null)
            {
                int currentSpeed = timeManager.GameSpeed;
                for (int i = 0; i < speedButtons.Length; i++)
                {
                    if (speeds[i] == currentSpeed)
                    {
                        speedButtons[i].style.backgroundColor = activeSpeedColor;
                    }
                    else
                    {
                        speedButtons[i].style.backgroundColor = StyleKeyword.Null;
                    }
                }
            }
        }

        private void OnPauseClicked()
        {
            if (timeManager != null)
            {
                timeManager.TogglePause();
            }
        }

        private void OnSpeedClicked(int speed)
        {
            if (timeManager != null)
            {
                timeManager.SetSpeed(speed);
            }
        }

        public void ShowUI()
        {
            if (container != null)
            {
                container.style.display = DisplayStyle.Flex;
            }
        }

        public void HideUI()
        {
            if (container != null)
            {
                container.style.display = DisplayStyle.None;
            }
        }
    }
}
