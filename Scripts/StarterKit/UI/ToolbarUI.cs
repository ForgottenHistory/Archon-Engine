using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.SaveLoad;
using Core.UI;
using Map.MapModes;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Simple toolbar with common actions.
    /// Positioned in top right corner. Shows: Ledger, Map Mode, Save, Load buttons.
    /// Hidden until player selects a country.
    ///
    /// Demonstrates: UI integration with map mode system (ENGINE mechanism, GAME policy)
    /// </summary>
    public class ToolbarUI : StarterKitPanel
    {
        // UI Elements
        private Button ledgerButton;
        private Button mapModeButton;
        private Button saveButton;
        private Button loadButton;

        // References
        private LedgerUI ledgerUI;
        private SaveManager saveManager;

        // Map mode state
        private bool showingFarmDensity = false;

        public void Initialize(GameState gameStateRef, LedgerUI ledgerUIRef, SaveManager saveManagerRef)
        {
            ledgerUI = ledgerUIRef;
            saveManager = saveManagerRef;

            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Subscribe to player country selection to show toolbar
            Subscribe<PlayerCountrySelectedEvent>(OnPlayerCountrySelected);

            // Hide until country selected
            Hide();

            ArchonLogger.Log("ToolbarUI: Initialized", "starter_kit");
        }

        protected override void CreateUI()
        {
            // Create toolbar container - top right position, horizontal layout
            panelContainer = CreateStyledPanel("toolbar");
            UIHelper.SetFlexRow(panelContainer, Justify.FlexStart, Align.Center);
            UIHelper.SetPadding(panelContainer, SpacingSm, SpacingMd);
            PositionPanel(top: 10f, right: 10f);

            // Ledger button
            ledgerButton = CreateToolbarButton("Ledger (L)", OnLedgerClicked);
            panelContainer.Add(ledgerButton);

            // Map Mode button - toggle between Political and Farm Density
            mapModeButton = CreateToolbarButton("Map: Political (M)", OnMapModeClicked);
            panelContainer.Add(mapModeButton);

            // Save button
            saveButton = CreateToolbarButton("Save (F6)", OnSaveClicked);
            panelContainer.Add(saveButton);

            // Load button
            loadButton = CreateToolbarButton("Load (F7)", OnLoadClicked);
            panelContainer.Add(loadButton);
        }

        private Button CreateToolbarButton(string text, System.Action onClick)
        {
            var button = CreateStyledButton(text, onClick);
            UIHelper.SetPadding(button, SpacingSm, SpacingMd);
            UIHelper.SetMargin(button, 0, SpacingXs);
            return button;
        }

        private void OnPlayerCountrySelected(PlayerCountrySelectedEvent evt)
        {
            Show();
        }

        private void OnLedgerClicked()
        {
            ledgerUI?.Toggle();
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

        private void OnMapModeClicked()
        {
            var initializer = Initializer.Instance;
            if (initializer == null)
            {
                ArchonLogger.LogWarning("ToolbarUI: Initializer not available for map mode switch", "starter_kit");
                return;
            }

            // Toggle between Political and Farm Density (Economic) modes
            showingFarmDensity = !showingFarmDensity;

            if (showingFarmDensity)
            {
                initializer.SetMapMode(MapMode.Economic); // Farm Density is registered as Economic
                mapModeButton.text = "Map: Farms (M)";
                ArchonLogger.Log("ToolbarUI: Switched to Farm Density map mode", "starter_kit");
            }
            else
            {
                initializer.SetMapMode(MapMode.Political);
                mapModeButton.text = "Map: Political (M)";
                ArchonLogger.Log("ToolbarUI: Switched to Political map mode", "starter_kit");
            }
        }

        private void Update()
        {
            // Keyboard shortcut: M to toggle map mode
            if (Input.GetKeyDown(KeyCode.M) && isVisible)
            {
                OnMapModeClicked();
            }
        }
    }
}
