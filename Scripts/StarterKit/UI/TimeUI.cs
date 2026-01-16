using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Systems;
using Core.Localization;
using Core.UI;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Simple time control UI.
    /// Shows date/time, pause button, and speed buttons.
    /// Positioned in top-left corner.
    /// </summary>
    public class TimeUI : StarterKitPanel
    {
        [Header("Speed Settings")]
        [SerializeField] private Color activeSpeedColor = new Color(0.3f, 0.7f, 0.3f, 1f);

        // UI Elements
        private Label dateTimeLabel;
        private Button pauseButton;
        private Button[] speedButtons;

        // Configuration
        private int[] speeds = { 1, 2, 3, 4, 5, 10, 50, 100 };

        // References
        private TimeManager timeManager;

        public void Initialize(GameState gameStateRef, TimeManager timeManagerRef, int[] customSpeeds = null)
        {
            timeManager = timeManagerRef;

            if (timeManager == null)
            {
                ArchonLogger.LogError("TimeUI: Cannot initialize with null TimeManager!", "starter_kit");
                return;
            }

            if (customSpeeds != null && customSpeeds.Length > 0)
                speeds = customSpeeds;

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Hide until country selected
            Hide();

            ArchonLogger.Log("TimeUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!IsInitialized || timeManager == null)
                return;

            UpdateDisplay();
        }

        protected override void CreateUI()
        {
            // Container at top-left
            panelContainer = CreateStyledPanel("time-ui");
            UIHelper.SetFlexColumn(panelContainer, Justify.FlexStart, Align.FlexStart);
            PositionPanel(top: 10f, left: 10f);

            // Date/time label
            dateTimeLabel = CreateHeader("0001.01.01 00:00");
            dateTimeLabel.name = "datetime-label";
            dateTimeLabel.style.marginBottom = SpacingMd;
            panelContainer.Add(dateTimeLabel);

            // Pause button
            pauseButton = CreateStyledButton(LocalizationManager.Get("UI_PAUSE"), OnPauseClicked);
            pauseButton.name = "pause-button";
            pauseButton.style.marginBottom = SpacingMd;
            pauseButton.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            panelContainer.Add(pauseButton);

            // Speed buttons container
            var speedButtonContainer = CreateRow();
            speedButtonContainer.name = "speed-buttons";
            speedButtonContainer.style.flexWrap = Wrap.Wrap;

            // Create speed buttons from speeds array
            speedButtons = new Button[speeds.Length];

            for (int i = 0; i < speeds.Length; i++)
            {
                int speed = speeds[i];

                var btn = CreateStyledButton($"{speed}x", () => OnSpeedClicked(speed));
                btn.style.marginRight = SpacingXs;
                btn.style.marginBottom = SpacingXs;
                btn.style.minWidth = 40f;

                speedButtons[i] = btn;
                speedButtonContainer.Add(btn);
            }

            panelContainer.Add(speedButtonContainer);
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
                    speedButtons[i].style.backgroundColor = (speeds[i] == currentSpeed)
                        ? activeSpeedColor
                        : BackgroundButton;
                }
            }
        }

        private void OnPauseClicked()
        {
            timeManager?.TogglePause();
        }

        private void OnSpeedClicked(int speed)
        {
            timeManager?.SetSpeed(speed);
        }
    }
}
