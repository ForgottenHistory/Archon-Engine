using UnityEngine;
using UnityEngine.UI;
using ProvinceSystem.MapModes;
using ProvinceSystem.Countries;
using System.Collections.Generic;

namespace ProvinceSystem.UI
{
    /// <summary>
    /// UI controller for map mode selection and country information display
    /// </summary>
    public class MapModeUI : MonoBehaviour
    {
        [Header("References")]
        public MapModeManager mapModeManager;
        public CountryGenerator countryGenerator;
        public ProvinceManager provinceManager;

        [Header("UI Elements")]
        public Canvas uiCanvas;
        public GameObject buttonPrefab;
        public Transform buttonContainer;
        public Text infoText;
        public Text selectedProvinceText;
        public Text selectedCountryText;

        [Header("Layout Settings")]
        public float buttonSpacing = 5f;
        public Vector2 buttonSize = new Vector2(120, 30);
        public Vector2 panelOffset = new Vector2(10, 10);

        private Dictionary<MapModeManager.MapModeType, Button> modeButtons = new Dictionary<MapModeManager.MapModeType, Button>();

        void Start()
        {
            SetupUI();
        }

        void Update()
        {
            UpdateInfoDisplay();
        }

        private void SetupUI()
        {
            // Create UI canvas if not provided
            if (uiCanvas == null)
            {
                GameObject canvasObj = new GameObject("MapModeCanvas");
                uiCanvas = canvasObj.AddComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Create button container if not provided
            if (buttonContainer == null)
            {
                GameObject panel = new GameObject("MapModePanel");
                panel.transform.SetParent(uiCanvas.transform, false);

                RectTransform rect = panel.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(1, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = -panelOffset;

                Image bg = panel.AddComponent<Image>();
                bg.color = new Color(0, 0, 0, 0.7f);

                VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
                layout.spacing = buttonSpacing;
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;

                buttonContainer = panel.transform;
            }

            // Create mode buttons
            CreateModeButtons();

            // Create info display
            CreateInfoDisplay();
        }

        private void CreateModeButtons()
        {
            if (buttonPrefab == null)
            {
                // Create a default button prefab
                buttonPrefab = new GameObject("ButtonTemplate");
                Button btn = buttonPrefab.AddComponent<Button>();
                Image img = buttonPrefab.AddComponent<Image>();
                img.color = Color.white;

                GameObject textObj = new GameObject("Text");
                textObj.transform.SetParent(buttonPrefab.transform, false);
                Text txt = textObj.AddComponent<Text>();
                txt.text = "Button";
                txt.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
                txt.color = Color.black;
                txt.alignment = TextAnchor.MiddleCenter;

                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                buttonPrefab.SetActive(false);
            }

            // Create buttons for each map mode
            var modeTypes = System.Enum.GetValues(typeof(MapModeManager.MapModeType));

            foreach (MapModeManager.MapModeType modeType in modeTypes)
            {
                GameObject buttonObj = Instantiate(buttonPrefab, buttonContainer);
                buttonObj.SetActive(true);
                buttonObj.name = $"Button_{modeType}";

                Button button = buttonObj.GetComponent<Button>();
                Text buttonText = buttonObj.GetComponentInChildren<Text>();

                if (buttonText != null)
                {
                    buttonText.text = modeType.ToString();
                }

                // Setup button size
                RectTransform rect = buttonObj.GetComponent<RectTransform>();
                rect.sizeDelta = buttonSize;

                // Add click handler
                MapModeManager.MapModeType capturedMode = modeType;
                button.onClick.AddListener(() => OnModeButtonClicked(capturedMode));

                modeButtons[modeType] = button;
            }

            // Add country generation button
            CreateCountryGenerationButton();
        }

        private void CreateCountryGenerationButton()
        {
            GameObject buttonObj = Instantiate(buttonPrefab, buttonContainer);
            buttonObj.SetActive(true);
            buttonObj.name = "Button_GenerateCountries";

            Button button = buttonObj.GetComponent<Button>();
            Text buttonText = buttonObj.GetComponentInChildren<Text>();

            if (buttonText != null)
            {
                buttonText.text = "Generate Countries";
            }

            RectTransform rect = buttonObj.GetComponent<RectTransform>();
            rect.sizeDelta = buttonSize;

            button.onClick.AddListener(OnGenerateCountriesClicked);
        }

        private void CreateInfoDisplay()
        {
            // Create info panel
            GameObject infoPanel = new GameObject("InfoPanel");
            infoPanel.transform.SetParent(uiCanvas.transform, false);

            RectTransform rect = infoPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(panelOffset.x, -panelOffset.y);
            rect.sizeDelta = new Vector2(300, 200);

            Image bg = infoPanel.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.7f);

            VerticalLayoutGroup layout = infoPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 5f;

            // Create text elements
            infoText = CreateTextElement("InfoText", "Map Mode: None", infoPanel.transform);
            selectedProvinceText = CreateTextElement("ProvinceText", "Province: None", infoPanel.transform);
            selectedCountryText = CreateTextElement("CountryText", "Country: None", infoPanel.transform);
        }

        private Text CreateTextElement(string name, string defaultText, Transform parent)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            Text text = textObj.AddComponent<Text>();
            text.text = defaultText;
            text.font = Font.CreateDynamicFontFromOSFont("Arial", 12);
            text.color = Color.white;

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(280, 20);

            return text;
        }

        private void OnModeButtonClicked(MapModeManager.MapModeType modeType)
        {
            if (mapModeManager != null)
            {
                mapModeManager.SetMapMode(modeType);
                UpdateButtonStates();
            }
        }

        private void OnGenerateCountriesClicked()
        {
            var controller = FindObjectOfType<MapSystem.MapSystemController>();
            if (controller != null)
            {
                controller.RegenerateCountries();
            }
        }

        private void UpdateButtonStates()
        {
            if (mapModeManager == null) return;

            foreach (var kvp in modeButtons)
            {
                Image img = kvp.Value.GetComponent<Image>();
                if (img != null)
                {
                    img.color = kvp.Key == mapModeManager.CurrentModeType ?
                        new Color(0.5f, 0.8f, 1f) : Color.white;
                }
            }
        }

        private void UpdateInfoDisplay()
        {
            if (mapModeManager != null && infoText != null)
            {
                infoText.text = $"Map Mode: {mapModeManager.CurrentMode?.ModeName ?? "None"}";
            }

            if (provinceManager != null && selectedProvinceText != null)
            {
                int selectedId = provinceManager.CurrentSelectedProvinceId;
                if (selectedId >= 0)
                {
                    selectedProvinceText.text = $"Province: {selectedId}";

                    // Show country info if in political mode
                    if (countryGenerator != null && countryGenerator.CountryService != null &&
                        selectedCountryText != null)
                    {
                        var country = countryGenerator.CountryService.GetProvinceOwner(selectedId);
                        if (country != null)
                        {
                            selectedCountryText.text = $"Country: {country.name} ({country.provinces.Count} provinces)";
                        }
                        else
                        {
                            selectedCountryText.text = "Country: Unowned";
                        }
                    }
                }
                else
                {
                    selectedProvinceText.text = "Province: None";
                    if (selectedCountryText != null)
                        selectedCountryText.text = "Country: None";
                }
            }

            UpdateButtonStates();
        }
    }
}