using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Localization;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Ledger panel showing data for all countries.
    /// Displays country name, provinces, units, gold in a sortable table.
    /// Toggle with L key or button.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LedgerUI : MonoBehaviour
    {
        [Header("Styling")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        [SerializeField] private Color headerColor = new Color(0.2f, 0.2f, 0.3f, 1f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color playerRowColor = new Color(0.2f, 0.3f, 0.2f, 1f);
        [SerializeField] private Color alternateRowColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        [SerializeField] private int fontSize = 14;

        [Header("Hotkey")]
        [SerializeField] private KeyCode toggleKey = KeyCode.L;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement panelContainer;
        private VisualElement headerRow;
        private ScrollView tableScrollView;
        private Button closeButton;

        // References
        private GameState gameState;
        private EconomySystem economySystem;
        private UnitSystem unitSystem;
        private PlayerState playerState;
        private bool isInitialized;

        // State
        private bool isVisible;
        private List<VisualElement> rowElements = new List<VisualElement>();

        // Sorting
        private enum SortColumn { Name, Provinces, Units, Gold, Income }
        private SortColumn currentSortColumn = SortColumn.Provinces;
        private bool sortDescending = true;

        public bool IsInitialized => isInitialized;
        public bool IsVisible => isVisible;

        public void Initialize(GameState gameStateRef, EconomySystem economySystemRef, UnitSystem unitSystemRef, PlayerState playerStateRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("LedgerUI: Already initialized!", "starter_kit");
                return;
            }

            if (gameStateRef == null)
            {
                ArchonLogger.LogError("LedgerUI: Cannot initialize with null GameState!", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            economySystem = economySystemRef;
            unitSystem = unitSystemRef;
            playerState = playerStateRef;

            InitializeUI();

            isInitialized = true;
            isVisible = false;
            HidePanel();

            ArchonLogger.Log("LedgerUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!isInitialized) return;

            // Toggle with hotkey
            if (Input.GetKeyDown(toggleKey))
            {
                TogglePanel();
            }
        }

        public void TogglePanel()
        {
            if (isVisible)
                HidePanel();
            else
                ShowPanel();
        }

        public void ShowPanel()
        {
            if (panelContainer == null) return;

            RefreshData();
            panelContainer.style.display = DisplayStyle.Flex;
            isVisible = true;
        }

        public void HidePanel()
        {
            if (panelContainer == null) return;

            panelContainer.style.display = DisplayStyle.None;
            isVisible = false;
        }

        private void InitializeUI()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                ArchonLogger.LogError("LedgerUI: UIDocument not found!", "starter_kit");
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                ArchonLogger.LogError("LedgerUI: Root VisualElement is null!", "starter_kit");
                return;
            }

            // Create panel container - centered overlay
            panelContainer = new VisualElement();
            panelContainer.name = "ledger-panel";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = new Length(50, LengthUnit.Percent);
            panelContainer.style.top = new Length(50, LengthUnit.Percent);
            panelContainer.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            panelContainer.style.width = 600f;
            panelContainer.style.maxHeight = 500f;
            panelContainer.style.backgroundColor = backgroundColor;
            panelContainer.style.borderTopLeftRadius = 8f;
            panelContainer.style.borderTopRightRadius = 8f;
            panelContainer.style.borderBottomLeftRadius = 8f;
            panelContainer.style.borderBottomRightRadius = 8f;
            panelContainer.style.paddingTop = 10f;
            panelContainer.style.paddingBottom = 10f;
            panelContainer.style.paddingLeft = 15f;
            panelContainer.style.paddingRight = 15f;

            // Title bar with close button
            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.justifyContent = Justify.SpaceBetween;
            titleBar.style.alignItems = Align.Center;
            titleBar.style.marginBottom = 10f;

            var titleLabel = new Label("Ledger");
            titleLabel.style.fontSize = fontSize + 4;
            titleLabel.style.color = textColor;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleBar.Add(titleLabel);

            closeButton = new Button(() => HidePanel());
            closeButton.text = "X";
            closeButton.style.width = 24f;
            closeButton.style.height = 24f;
            closeButton.style.fontSize = fontSize;
            titleBar.Add(closeButton);

            panelContainer.Add(titleBar);

            // Header row
            headerRow = CreateHeaderRow();
            panelContainer.Add(headerRow);

            // Scrollable table content
            tableScrollView = new ScrollView(ScrollViewMode.Vertical);
            tableScrollView.style.flexGrow = 1f;
            tableScrollView.style.maxHeight = 400f;
            panelContainer.Add(tableScrollView);

            // Hotkey hint
            var hintLabel = new Label($"Press '{toggleKey}' to toggle");
            hintLabel.style.fontSize = fontSize - 2;
            hintLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            hintLabel.style.marginTop = 8f;
            hintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            panelContainer.Add(hintLabel);

            rootElement.Add(panelContainer);
        }

        private VisualElement CreateHeaderRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.backgroundColor = headerColor;
            row.style.paddingTop = 6f;
            row.style.paddingBottom = 6f;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;
            row.style.marginBottom = 2f;

            AddHeaderCell(row, "Country", 180f, SortColumn.Name);
            AddHeaderCell(row, "Provinces", 80f, SortColumn.Provinces);
            AddHeaderCell(row, "Units", 80f, SortColumn.Units);
            AddHeaderCell(row, "Gold", 80f, SortColumn.Gold);
            AddHeaderCell(row, "Income", 80f, SortColumn.Income);

            return row;
        }

        private void AddHeaderCell(VisualElement row, string text, float width, SortColumn column)
        {
            var cell = new Button(() => OnHeaderClicked(column));
            cell.text = text + (currentSortColumn == column ? (sortDescending ? " ▼" : " ▲") : "");
            cell.style.width = width;
            cell.style.fontSize = fontSize;
            cell.style.color = textColor;
            cell.style.unityFontStyleAndWeight = FontStyle.Bold;
            cell.style.unityTextAlign = TextAnchor.MiddleLeft;
            cell.style.backgroundColor = Color.clear;
            cell.style.borderLeftWidth = 0;
            cell.style.borderRightWidth = 0;
            cell.style.borderTopWidth = 0;
            cell.style.borderBottomWidth = 0;
            row.Add(cell);
        }

        private void OnHeaderClicked(SortColumn column)
        {
            if (currentSortColumn == column)
            {
                sortDescending = !sortDescending;
            }
            else
            {
                currentSortColumn = column;
                sortDescending = true;
            }

            // Rebuild header to show sort indicator
            panelContainer.Remove(headerRow);
            headerRow = CreateHeaderRow();
            panelContainer.Insert(1, headerRow); // After title bar

            RefreshData();
        }

        private void RefreshData()
        {
            // Clear existing rows
            tableScrollView.Clear();
            rowElements.Clear();

            if (gameState?.Countries == null) return;

            // Collect country data
            var countryDataList = new List<CountryRowData>();

            var countries = gameState.Countries.GetAllCountryIds();
            try
            {
                foreach (ushort countryId in countries)
                {
                    int provinceCount = gameState.CountryQueries?.GetProvinceCount(countryId) ?? 0;
                    if (provinceCount == 0) continue; // Skip countries without provinces

                    var rowData = new CountryRowData
                    {
                        CountryId = countryId,
                        Name = GetCountryName(countryId),
                        Provinces = provinceCount,
                        Units = GetUnitCount(countryId),
                        Gold = economySystem?.GetCountryGold(countryId) ?? 0,
                        Income = economySystem?.GetMonthlyIncome(countryId) ?? 0
                    };
                    countryDataList.Add(rowData);
                }
            }
            finally
            {
                countries.Dispose();
            }

            // Sort
            SortData(countryDataList);

            // Create rows
            bool alternate = false;
            foreach (var data in countryDataList)
            {
                var row = CreateDataRow(data, alternate);
                tableScrollView.Add(row);
                rowElements.Add(row);
                alternate = !alternate;
            }
        }

        private void SortData(List<CountryRowData> data)
        {
            switch (currentSortColumn)
            {
                case SortColumn.Name:
                    data.Sort((a, b) => sortDescending ? b.Name.CompareTo(a.Name) : a.Name.CompareTo(b.Name));
                    break;
                case SortColumn.Provinces:
                    data.Sort((a, b) => sortDescending ? b.Provinces.CompareTo(a.Provinces) : a.Provinces.CompareTo(b.Provinces));
                    break;
                case SortColumn.Units:
                    data.Sort((a, b) => sortDescending ? b.Units.CompareTo(a.Units) : a.Units.CompareTo(b.Units));
                    break;
                case SortColumn.Gold:
                    data.Sort((a, b) => sortDescending ? b.Gold.CompareTo(a.Gold) : a.Gold.CompareTo(b.Gold));
                    break;
                case SortColumn.Income:
                    data.Sort((a, b) => sortDescending ? b.Income.CompareTo(a.Income) : a.Income.CompareTo(b.Income));
                    break;
            }
        }

        private VisualElement CreateDataRow(CountryRowData data, bool alternate)
        {
            bool isPlayer = playerState != null && data.CountryId == playerState.PlayerCountryId;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.backgroundColor = isPlayer ? playerRowColor : (alternate ? alternateRowColor : Color.clear);
            row.style.paddingTop = 4f;
            row.style.paddingBottom = 4f;
            row.style.paddingLeft = 8f;
            row.style.paddingRight = 8f;

            // Country color indicator + name
            var nameContainer = new VisualElement();
            nameContainer.style.flexDirection = FlexDirection.Row;
            nameContainer.style.alignItems = Align.Center;
            nameContainer.style.width = 180f;

            var colorIndicator = new VisualElement();
            colorIndicator.style.width = 12f;
            colorIndicator.style.height = 12f;
            colorIndicator.style.marginRight = 6f;
            colorIndicator.style.backgroundColor = GetCountryColor(data.CountryId);
            colorIndicator.style.borderTopLeftRadius = 2f;
            colorIndicator.style.borderTopRightRadius = 2f;
            colorIndicator.style.borderBottomLeftRadius = 2f;
            colorIndicator.style.borderBottomRightRadius = 2f;
            nameContainer.Add(colorIndicator);

            var nameLabel = new Label(data.Name + (isPlayer ? " (You)" : ""));
            nameLabel.style.fontSize = fontSize;
            nameLabel.style.color = textColor;
            nameContainer.Add(nameLabel);

            row.Add(nameContainer);

            AddDataCell(row, data.Provinces.ToString(), 80f);
            AddDataCell(row, data.Units.ToString(), 80f);
            AddDataCell(row, data.Gold.ToString(), 80f);
            AddDataCell(row, $"+{data.Income}/mo", 80f);

            return row;
        }

        private void AddDataCell(VisualElement row, string text, float width)
        {
            var cell = new Label(text);
            cell.style.width = width;
            cell.style.fontSize = fontSize;
            cell.style.color = textColor;
            cell.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(cell);
        }

        private string GetCountryName(ushort countryId)
        {
            var coldData = gameState.CountryQueries?.GetColdData(countryId);
            if (coldData != null && !string.IsNullOrEmpty(coldData.displayName))
                return coldData.displayName;

            var tag = gameState.CountryQueries?.GetTag(countryId);
            return tag ?? $"Country {countryId}";
        }

        private Color GetCountryColor(ushort countryId)
        {
            var color32 = gameState.CountryQueries?.GetColor(countryId) ?? new Color32(128, 128, 128, 255);
            return new Color(color32.r / 255f, color32.g / 255f, color32.b / 255f, 1f);
        }

        private int GetUnitCount(ushort countryId)
        {
            // Access Core's UnitSystem via GameState
            var coreUnitSystem = gameState?.Units;
            if (coreUnitSystem == null) return 0;

            var units = coreUnitSystem.GetCountryUnits(countryId);
            return units?.Count ?? 0;
        }

        private struct CountryRowData
        {
            public ushort CountryId;
            public string Name;
            public int Provinces;
            public int Units;
            public int Gold;
            public int Income;
        }
    }
}
