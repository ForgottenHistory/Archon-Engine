using UnityEngine;
using Core;
using Core.Systems;

namespace Map.DebugTools
{
    /// <summary>
    /// ENGINE LAYER: Simple debug UI for map showcase scenes
    /// Displays current date and provides time controls
    /// Uses OnGUI for zero dependencies (no UI Toolkit/Canvas required)
    /// </summary>
    public class EngineDebugUI : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showUI = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        [Header("Position")]
        [SerializeField] private float marginLeft = 10f;
        [SerializeField] private float marginTop = 10f;

        // References
        private GameState gameState;
        private TimeManager timeManager;

        // Speed presets
        private readonly int[] speedMultipliers = { 0, 1, 2, 5, 10, 50 };
        private readonly string[] speedLabels = { "||", ">", ">>", ">>>", ">>>>", ">>>>>" };
        private int currentSpeedIndex = 1;

        // Styles
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle dateStyle;
        private bool stylesInitialized = false;

        void Start()
        {
            // Lazy init - GameState may not be ready yet
            TryGetReferences();
        }

        private void TryGetReferences()
        {
            if (gameState == null)
            {
                gameState = FindFirstObjectByType<GameState>();
            }
            if (gameState != null && timeManager == null)
            {
                timeManager = gameState.Time;
            }
        }

        void Update()
        {
            // Keep trying to get references until we have them
            TryGetReferences();

            if (Input.GetKeyDown(toggleKey))
            {
                showUI = !showUI;
            }

            // Keyboard shortcuts for speed
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePause();
            }
            if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
            {
                IncreaseSpeed();
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                DecreaseSpeed();
            }
        }

        void InitStyles()
        {
            if (stylesInitialized) return;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            boxStyle.padding = new RectOffset(10, 10, 10, 10);

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.normal.textColor = Color.white;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 12;
            buttonStyle.fixedWidth = 45;
            buttonStyle.fixedHeight = 28;

            dateStyle = new GUIStyle(GUI.skin.label);
            dateStyle.fontSize = 18;
            dateStyle.fontStyle = FontStyle.Bold;
            dateStyle.normal.textColor = new Color(1f, 0.9f, 0.7f);

            stylesInitialized = true;
        }

        void OnGUI()
        {
            if (!showUI) return;

            InitStyles();

            float panelWidth = 340f;
            float panelHeight = 130f;

            Rect panelRect = new Rect(marginLeft, marginTop, panelWidth, panelHeight);

            GUI.Box(panelRect, "", boxStyle);

            GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 8, panelRect.width - 20, panelRect.height - 16));

            // Date display
            string dateString = GetDateString();
            GUILayout.Label(dateString, dateStyle);

            GUILayout.Space(5);

            // Speed controls
            GUILayout.BeginHorizontal();

            for (int i = 0; i < speedLabels.Length; i++)
            {
                bool isActive = (i == currentSpeedIndex);
                GUI.backgroundColor = isActive ? new Color(0.3f, 0.6f, 0.3f) : Color.gray;

                if (GUILayout.Button(speedLabels[i], buttonStyle))
                {
                    SetSpeed(i);
                }
            }

            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            // Speed label
            string speedText = currentSpeedIndex == 0 ? "Paused" : $"Speed: x{speedMultipliers[currentSpeedIndex]}";
            GUILayout.Label(speedText, labelStyle);

            GUILayout.EndArea();

            // Help text at bottom
            GUI.Label(new Rect(marginLeft, marginTop + panelHeight + 5, 300, 20),
                "F1: Toggle | Space: Pause | +/-: Speed", labelStyle);
        }

        private string GetDateString()
        {
            if (timeManager == null) return "No TimeManager";

            // Format: "15 March 1444" or similar
            string[] monthNames = { "January", "February", "March", "April", "May", "June",
                                    "July", "August", "September", "October", "November", "December" };

            int monthIndex = Mathf.Clamp(timeManager.CurrentMonth - 1, 0, 11);
            return $"{timeManager.CurrentDay} {monthNames[monthIndex]} {timeManager.CurrentYear}";
        }

        private void TogglePause()
        {
            if (currentSpeedIndex == 0)
            {
                SetSpeed(1); // Unpause to normal speed
            }
            else
            {
                SetSpeed(0); // Pause
            }
        }

        private void IncreaseSpeed()
        {
            if (currentSpeedIndex < speedMultipliers.Length - 1)
            {
                SetSpeed(currentSpeedIndex + 1);
            }
        }

        private void DecreaseSpeed()
        {
            if (currentSpeedIndex > 0)
            {
                SetSpeed(currentSpeedIndex - 1);
            }
        }

        private void SetSpeed(int index)
        {
            currentSpeedIndex = Mathf.Clamp(index, 0, speedMultipliers.Length - 1);

            if (timeManager != null)
            {
                // TimeManager uses speed levels 0-8, where 0 is paused
                timeManager.SetGameSpeed(currentSpeedIndex);
                ArchonLogger.Log($"EngineDebugUI: Set speed to {currentSpeedIndex}, TimeManager.IsPaused={timeManager.IsPaused}, IsInitialized={timeManager.IsInitialized}", "map_rendering");
            }
            else
            {
                ArchonLogger.LogWarning("EngineDebugUI: TimeManager is null!", "map_rendering");
            }
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
