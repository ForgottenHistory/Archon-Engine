using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.Events;
using Core.Systems;
using Core.Units;
using Core.UI;
using System.Collections.Generic;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Ledger panel showing data for all countries.
    /// Displays country name, provinces, units, gold in a sortable table.
    /// Toggle with L key or button.
    /// </summary>
    public class LedgerUI : StarterKitPanel
    {
        [Header("Hotkey")]
        [SerializeField] private KeyCode toggleKey = KeyCode.L;

        // UI Elements
        private VisualElement headerRow;
        private ScrollView tableScrollView;
        private Button closeButton;

        // References
        private EconomySystem economySystem;
        private UnitSystem unitSystem;
        private PlayerState playerState;

        // State
        private List<VisualElement> rowElements = new List<VisualElement>();

        // Sorting
        private enum SortColumn { Name, Provinces, Units, Gold, Income }
        private SortColumn currentSortColumn = SortColumn.Provinces;
        private bool sortDescending = true;

        // Column widths
        private const float ColWidthName = 180f;
        private const float ColWidthData = 80f;

        public void Initialize(GameState gameStateRef, EconomySystem economySystemRef, UnitSystem unitSystemRef, PlayerState playerStateRef)
        {
            economySystem = economySystemRef;
            unitSystem = unitSystemRef;
            playerState = playerStateRef;

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to events via EventBus (auto-disposed on OnDestroy)
            Subscribe<GoldChangedEvent>(HandleGoldChanged);
            Subscribe<ProvinceOwnershipChangedEvent>(HandleOwnershipChanged);
            Subscribe<UnitCreatedEvent>(HandleUnitCreated);
            Subscribe<UnitDestroyedEvent>(HandleUnitDestroyed);

            Hide();

            ArchonLogger.Log("LedgerUI: Initialized", "starter_kit");
        }

        void Update()
        {
            if (!IsInitialized) return;

            // Toggle with hotkey
            if (Input.GetKeyDown(toggleKey))
            {
                Toggle();
            }
        }

        protected override void OnShow()
        {
            RefreshData();
        }

        // Event handlers - only refresh if visible
        private void HandleGoldChanged(GoldChangedEvent evt)
        {
            if (IsVisible) RefreshData();
        }

        private void HandleOwnershipChanged(ProvinceOwnershipChangedEvent evt)
        {
            if (IsVisible) RefreshData();
        }

        private void HandleUnitCreated(UnitCreatedEvent evt)
        {
            if (IsVisible) RefreshData();
        }

        private void HandleUnitDestroyed(UnitDestroyedEvent evt)
        {
            if (IsVisible) RefreshData();
        }

        protected override void CreateUI()
        {
            // Create panel container - centered overlay
            panelContainer = CreateStyledPanel("ledger-panel");
            panelContainer.style.width = 600f;
            panelContainer.style.maxHeight = 500f;
            UIHelper.SetBorderRadius(panelContainer, RadiusLg);
            CenterPanel();

            // Title bar with close button
            var titleBar = CreateRow(Justify.SpaceBetween);
            titleBar.style.marginBottom = SpacingMd;

            var titleLabel = CreateHeader("Ledger");
            titleBar.Add(titleLabel);

            closeButton = new Button(() => Hide());
            closeButton.text = "X";
            closeButton.AddToClassList("button-close");
            UIHelper.SetSize(closeButton, 24f, 24f);
            closeButton.style.fontSize = FontSizeNormal;
            closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            UIHelper.SetPadding(closeButton, 0);
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
            var hintLabel = CreateLabelText($"Press '{toggleKey}' to toggle");
            hintLabel.style.marginTop = SpacingMd;
            UIHelper.SetTextAlign(hintLabel, TextAnchor.MiddleCenter);
            panelContainer.Add(hintLabel);
        }

        private VisualElement CreateHeaderRow()
        {
            var row = CreateRow();
            row.style.backgroundColor = BackgroundHeader;
            UIHelper.SetPadding(row, SpacingSm, SpacingMd);
            row.style.marginBottom = 2f;

            AddHeaderCell(row, "Country", ColWidthName, SortColumn.Name);
            AddHeaderCell(row, "Provinces", ColWidthData, SortColumn.Provinces);
            AddHeaderCell(row, "Units", ColWidthData, SortColumn.Units);
            AddHeaderCell(row, "Gold", ColWidthData, SortColumn.Gold);
            AddHeaderCell(row, "Income", ColWidthData, SortColumn.Income);

            return row;
        }

        private void AddHeaderCell(VisualElement row, string text, float width, SortColumn column)
        {
            var cell = new Button(() => OnHeaderClicked(column));
            cell.text = text + (currentSortColumn == column ? (sortDescending ? " ▼" : " ▲") : "");
            cell.style.width = width;
            cell.style.fontSize = FontSizeNormal;
            cell.style.color = TextPrimary;
            cell.style.unityFontStyleAndWeight = FontStyle.Bold;
            cell.style.unityTextAlign = TextAnchor.MiddleLeft;
            cell.style.backgroundColor = Color.clear;
            UIHelper.RemoveBorders(cell);
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
                        Gold = economySystem?.GetCountryGoldInt(countryId) ?? 0,
                        Income = economySystem?.GetMonthlyIncomeInt(countryId) ?? 0
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

            var row = CreateRow();
            row.style.backgroundColor = isPlayer ? BackgroundRowPlayer : (alternate ? BackgroundRowAlt : Color.clear);
            UIHelper.SetPadding(row, SpacingXs, SpacingMd);

            // Country color indicator + name
            var nameContainer = CreateRow();
            nameContainer.style.width = ColWidthName;

            var colorIndicator = CreateColorIndicator(GetCountryColor(data.CountryId), 12f);
            nameContainer.Add(colorIndicator);

            var nameLabel = CreateText(data.Name + (isPlayer ? " (You)" : ""));
            nameContainer.Add(nameLabel);

            row.Add(nameContainer);

            AddDataCell(row, data.Provinces.ToString());
            AddDataCell(row, data.Units.ToString());
            AddDataCell(row, data.Gold.ToString());
            AddDataCell(row, $"+{data.Income}/mo");

            return row;
        }

        private void AddDataCell(VisualElement row, string text)
        {
            var cell = CreateText(text);
            cell.style.width = ColWidthData;
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
