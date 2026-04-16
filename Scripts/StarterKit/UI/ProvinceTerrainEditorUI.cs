using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Core;
using Core.Registries;
using Core.Systems;
using Core.UI;
using Map.Core;
using Map.Interaction;
using Map.MapModes;
using Engine;
using TerrainData = Core.Registries.TerrainData;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Runtime terrain editor panel.
    /// Launched from main menu. Lets you paint terrain types onto provinces
    /// and save changes back to province history json5 files.
    ///
    /// Workflow:
    /// 1. Click "Map Editor" from main menu
    /// 2. Select a terrain from the palette on the left
    /// 3. Click provinces on the map to assign terrain
    /// 4. Click "Save to Disk" to write changes to json5 files
    /// 5. Click "Back" to return to main menu
    /// </summary>
    public class ProvinceTerrainEditorUI : StarterKitPanel
    {
        // Engine references
        private ProvinceSelector provinceSelector;
        private ProvinceSystem provinceSystem;
        private ProvinceRegistry provinceRegistry;
        private Registry<TerrainData> terrainRegistry;
        private MapSystemCoordinator mapCoordinator;
        private MapModeDataTextures dataTextures;

        // UI elements
        private Label titleLabel;
        private Label selectedProvinceLabel;
        private Label selectedTerrainLabel;
        private Label pendingCountLabel;
        private VisualElement paletteContainer;
        private VisualElement provinceInfoContainer;
        private ScrollView provinceListScroll;
        private Button saveButton;
        private Button backButton;
        private Button mapModeToggleButton;

        // Color editor UI
        private VisualElement colorEditorContainer;
        private VisualElement colorPreview;
        private SliderInt sliderR;
        private SliderInt sliderG;
        private SliderInt sliderB;
        private Label colorEditorLabel;

        // State
        private string selectedTerrainKey;
        private byte selectedTerrainId;
        private ushort lastClickedProvince;
        private Dictionary<ushort, string> pendingChanges = new();
        private List<TerrainEntry> terrainEntries = new();
        private bool showingTerrain;
        private MapMode previousMapMode;

        // Pending color changes: terrainKey -> (oldColor, newColor)
        private Dictionary<string, (Color32 oldColor, Color32 newColor)> pendingColorChanges = new();

        /// <summary>Fired when user clicks Back to return to main menu.</summary>
        public event System.Action OnBackClicked;

        private struct TerrainEntry
        {
            public string Key;
            public string Name;
            public byte TerrainId;
            public Color Color;
            public bool IsWater;
        }

        public void Initialize(GameState gameStateRef)
        {
            if (!base.Initialize(gameStateRef))
                return;

            provinceSystem = gameStateRef.Provinces;
            terrainRegistry = gameStateRef.Registries?.Terrains;
            provinceRegistry = gameStateRef.Registries?.Provinces;
            provinceSelector = FindFirstObjectByType<ProvinceSelector>();
            mapCoordinator = ArchonEngine.Instance?.MapSystemCoordinator;
            dataTextures = ArchonEngine.Instance?.MapModeManager?.DataTextures;

            // Build terrain entries from live registry
            if (terrainRegistry != null)
            {
                foreach (var terrain in terrainRegistry.GetAll())
                {
                    terrainEntries.Add(new TerrainEntry
                    {
                        Key = terrain.Key,
                        Name = terrain.Name,
                        TerrainId = terrain.TerrainId,
                        Color = new Color(terrain.ColorR / 255f, terrain.ColorG / 255f, terrain.ColorB / 255f, 1f),
                        IsWater = terrain.IsWater
                    });
                }
            }

            Hide();
            ArchonLogger.Log("ProvinceTerrainEditorUI: Initialized", "starter_kit");
        }

        protected override void CreateUI()
        {
            // Left-side panel, full height
            panelContainer = new VisualElement();
            panelContainer.name = "terrain-editor-container";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 20f;
            panelContainer.style.top = 20f;
            panelContainer.style.bottom = 20f;
            panelContainer.style.width = 280f;
            panelContainer.style.alignItems = Align.Stretch;

            var contentBox = CreateStyledPanel("terrain-editor-content");
            UIHelper.SetBorderRadius(contentBox, RadiusLg);
            UIHelper.SetPadding(contentBox, SpacingMd, SpacingLg);
            contentBox.style.alignItems = Align.Stretch;
            contentBox.style.flexGrow = 1;

            // Title + back button row
            var headerRow = CreateRow(Justify.SpaceBetween);
            titleLabel = CreateHeader("Terrain Editor");
            headerRow.Add(titleLabel);

            backButton = CreateStyledButton("Back", HandleBackClicked);
            UIHelper.SetPadding(backButton, SpacingXs, SpacingMd);
            headerRow.Add(backButton);
            contentBox.Add(headerRow);

            // Pending changes + save row
            var actionRow = CreateRow(Justify.SpaceBetween);
            actionRow.style.marginTop = SpacingMd;
            actionRow.style.marginBottom = SpacingMd;

            pendingCountLabel = CreateSecondaryText("No changes");
            actionRow.Add(pendingCountLabel);

            saveButton = CreateStyledButton("Save to Disk", HandleSaveClicked);
            saveButton.SetEnabled(false);
            actionRow.Add(saveButton);
            contentBox.Add(actionRow);

            // Map mode toggle
            mapModeToggleButton = CreateStyledButton("Show Terrain", HandleMapModeToggle);
            mapModeToggleButton.style.marginBottom = SpacingSm;
            contentBox.Add(mapModeToggleButton);

            // Reset overrides button
            var resetButton = CreateStyledButton("Reset to Original", HandleResetOverrides);
            resetButton.style.marginBottom = SpacingMd;
            contentBox.Add(resetButton);

            // Divider
            contentBox.Add(CreateDivider());

            // Selected province info
            var infoHeader = CreateTitle("Province");
            infoHeader.style.marginTop = SpacingMd;
            contentBox.Add(infoHeader);

            provinceInfoContainer = new VisualElement();
            provinceInfoContainer.style.marginBottom = SpacingMd;
            selectedProvinceLabel = CreateSecondaryText("Click a province on the map");
            provinceInfoContainer.Add(selectedProvinceLabel);
            contentBox.Add(provinceInfoContainer);

            // Divider
            contentBox.Add(CreateDivider());

            // Terrain palette
            var paletteHeader = CreateTitle("Terrain Palette");
            paletteHeader.style.marginTop = SpacingMd;
            contentBox.Add(paletteHeader);

            selectedTerrainLabel = CreateGoldText("None selected");
            selectedTerrainLabel.style.marginBottom = SpacingSm;
            contentBox.Add(selectedTerrainLabel);

            var paletteScroll = new ScrollView(ScrollViewMode.Vertical);
            paletteScroll.style.flexGrow = 1;
            paletteScroll.style.maxHeight = 400f;

            paletteContainer = new VisualElement();
            paletteContainer.style.alignItems = Align.Stretch;
            paletteScroll.Add(paletteContainer);
            contentBox.Add(paletteScroll);

            // Divider
            contentBox.Add(CreateDivider());

            // Color editor section
            colorEditorLabel = CreateTitle("Edit Color");
            colorEditorLabel.style.marginTop = SpacingSm;
            contentBox.Add(colorEditorLabel);

            colorEditorContainer = new VisualElement();
            colorEditorContainer.style.alignItems = Align.Stretch;

            // Color preview swatch
            colorPreview = new VisualElement();
            colorPreview.style.height = 24f;
            colorPreview.style.marginBottom = SpacingSm;
            UIHelper.SetBorderRadius(colorPreview, RadiusSm);
            colorEditorContainer.Add(colorPreview);

            // RGB sliders
            sliderR = CreateColorSlider("R");
            sliderG = CreateColorSlider("G");
            sliderB = CreateColorSlider("B");
            colorEditorContainer.Add(sliderR);
            colorEditorContainer.Add(sliderG);
            colorEditorContainer.Add(sliderB);

            // Apply button
            var applyColorButton = CreateStyledButton("Apply Color", HandleApplyColor);
            applyColorButton.style.marginTop = SpacingSm;
            colorEditorContainer.Add(applyColorButton);

            contentBox.Add(colorEditorContainer);
            colorEditorContainer.style.display = DisplayStyle.None;

            panelContainer.Add(contentBox);
        }

        protected override void OnShow()
        {
            // Enable province clicking
            if (provinceSelector != null)
            {
                provinceSelector.SelectionEnabled = true;
                provinceSelector.OnProvinceClicked += HandleProvinceClicked;
            }

            // Rebuild terrain palette buttons (in case terrainEntries were populated after CreateUI)
            RebuildPalette();
            UpdatePendingLabel();
        }

        protected override void OnHide()
        {
            if (provinceSelector != null)
            {
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                provinceSelector.SelectionEnabled = false;
            }

            // Restore previous map mode when leaving editor
            if (showingTerrain)
            {
                Initializer.Instance?.SetMapMode(previousMapMode);
                showingTerrain = false;
                if (mapModeToggleButton != null)
                    mapModeToggleButton.text = "Show Terrain";
            }
        }

        protected override void OnDestroy()
        {
            if (provinceSelector != null)
                provinceSelector.OnProvinceClicked -= HandleProvinceClicked;

            // Flush deferred terrain.png patches now that play mode is ending
            if (TerrainColorPatcher.HasDeferredPatches)
            {
                TerrainColorPatcher.FlushDeferredImagePatches();
            }

            base.OnDestroy();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || dataTextures?.TerrainColorPalette == null || terrainRegistry == null)
                return;

            // Rebuild GPU terrain palette from in-memory TerrainData.
            // Unity's asset reimport on focus-regain can invalidate textures,
            // so we restore the palette to match our live state.
            foreach (var terrain in terrainRegistry.GetAll())
            {
                if (terrain.TerrainId < 32)
                {
                    dataTextures.TerrainColorPalette.SetPixel(
                        terrain.TerrainId, 0,
                        new Color32(terrain.ColorR, terrain.ColorG, terrain.ColorB, 255));
                }
            }
            dataTextures.TerrainColorPalette.Apply(false);
        }

        private void RebuildPalette()
        {
            paletteContainer.Clear();

            foreach (var entry in terrainEntries)
            {
                var row = CreateRow(Justify.FlexStart);
                row.style.marginBottom = SpacingXs;
                row.style.paddingTop = 3f;
                row.style.paddingBottom = 3f;
                row.style.paddingLeft = SpacingSm;
                row.style.paddingRight = SpacingSm;
                UIHelper.SetBorderRadius(row, RadiusSm);

                // Highlight if selected
                bool isSelected = entry.Key == selectedTerrainKey;
                row.style.backgroundColor = isSelected
                    ? new Color(0.3f, 0.4f, 0.5f, 1f)
                    : new Color(0, 0, 0, 0);

                // Color swatch
                var swatch = CreateColorIndicator(entry.Color, 14f);
                row.Add(swatch);

                // Name
                var nameLabel = isSelected ? CreateGoldText(entry.Name) : CreateText(entry.Name);
                nameLabel.style.flexGrow = 1;
                row.Add(nameLabel);

                // Water indicator
                if (entry.IsWater)
                {
                    var waterLabel = CreateLabelText("water");
                    row.Add(waterLabel);
                }

                // Click handler
                string key = entry.Key;
                byte terrainId = entry.TerrainId;
                string name = entry.Name;
                var capturedEntry = entry;
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    selectedTerrainKey = key;
                    selectedTerrainId = terrainId;
                    selectedTerrainLabel.text = name;
                    ShowColorEditor(capturedEntry);
                    RebuildPalette();
                });

                paletteContainer.Add(row);
            }
        }

        private void HandleProvinceClicked(ushort provinceId)
        {
            if (provinceId == 0) return;

            lastClickedProvince = provinceId;

            // If a terrain is selected, paint it
            if (!string.IsNullOrEmpty(selectedTerrainKey))
            {
                // Update simulation state
                provinceSystem.SetProvinceTerrain(provinceId, selectedTerrainId);

                // Update GPU terrain buffer + regenerate blend maps for visual refresh
                if (mapCoordinator != null)
                {
                    mapCoordinator.UpdateProvinceTerrain(provinceId, selectedTerrainId);
                    mapCoordinator.RegenerateTerrainBlendMaps();
                }

                pendingChanges[provinceId] = selectedTerrainKey;
                UpdatePendingLabel();
            }

            // Update province info display
            UpdateProvinceInfo(provinceId);
        }

        private void UpdateProvinceInfo(ushort provinceId)
        {
            provinceInfoContainer.Clear();

            // Province name and ID
            string name = $"Province {provinceId}";
            if (provinceRegistry != null)
            {
                var data = provinceRegistry.GetByDefinition(provinceId);
                if (data != null && !string.IsNullOrEmpty(data.Name))
                    name = data.Name;
            }

            var nameLabel = CreateText($"{name} (ID: {provinceId})");
            provinceInfoContainer.Add(nameLabel);

            // Current terrain
            ushort currentTerrainId = provinceSystem.GetProvinceTerrain(provinceId);
            string terrainName = "Unknown";
            foreach (var entry in terrainEntries)
            {
                if (entry.TerrainId == currentTerrainId)
                {
                    terrainName = $"{entry.Name} ({entry.Key})";
                    break;
                }
            }
            var terrainLabel = CreateSecondaryText($"Terrain: {terrainName}");
            provinceInfoContainer.Add(terrainLabel);

            // Owner
            ushort ownerId = provinceSystem.GetProvinceOwner(provinceId);
            string ownerText = ownerId == 0 ? "Unowned" : (gameState.CountryQueries?.GetTag(ownerId) ?? $"Country {ownerId}");
            var ownerLabel = CreateSecondaryText($"Owner: {ownerText}");
            provinceInfoContainer.Add(ownerLabel);

            // Pending change
            if (pendingChanges.TryGetValue(provinceId, out string pending))
            {
                var pendingLabel = CreateGoldText($"Pending: {pending}");
                provinceInfoContainer.Add(pendingLabel);
            }
        }

        private void UpdatePendingLabel()
        {
            int total = pendingChanges.Count + pendingColorChanges.Count;
            if (total == 0)
            {
                pendingCountLabel.text = "No changes";
                saveButton.SetEnabled(false);
            }
            else
            {
                pendingCountLabel.text = $"{total} unsaved";
                saveButton.SetEnabled(true);
            }
        }

        private void HandleResetOverrides()
        {
            if (!Core.Modding.DataFileResolver.IsInitialized) return;

            string overrideDir = Core.Modding.DataFileResolver.OverrideDirectory;
            if (System.IO.Directory.Exists(overrideDir))
            {
                System.IO.Directory.Delete(overrideDir, true);
                ArchonLogger.Log($"ProvinceTerrainEditorUI: Deleted override directory: {overrideDir}", "starter_kit");
            }

            // Clear pending changes
            pendingChanges.Clear();
            pendingColorChanges.Clear();

            // Reload terrain colors from base terrain.json5 into registry + GPU palette
            ReloadBaseTerrainColors();

            // Rebuild the UI palette to reflect original colors
            RebuildTerrainEntries();
            RebuildPalette();
            UpdatePendingLabel();

            ArchonLogger.Log("ProvinceTerrainEditorUI: Reset to original data (terrain colors reloaded live, province terrains need restart)", "starter_kit");
        }

        private void ReloadBaseTerrainColors()
        {
            // Read base terrain.json5 directly
            string basePath = System.IO.Path.Combine(
                Core.Modding.DataFileResolver.BaseDirectory, "map", "terrain.json5");

            if (!System.IO.File.Exists(basePath) || terrainRegistry == null) return;

            try
            {
                var json = Core.Loaders.Json5Loader.LoadJson5File(basePath);
                var categories = json?["categories"] as Newtonsoft.Json.Linq.JObject;
                if (categories == null) return;

                foreach (var property in categories.Properties())
                {
                    string key = property.Name;
                    var terrainObj = property.Value as Newtonsoft.Json.Linq.JObject;
                    if (terrainObj == null) continue;

                    var colorArray = terrainObj["color"] as Newtonsoft.Json.Linq.JArray;
                    if (colorArray == null || colorArray.Count < 3) continue;

                    byte r = (byte)(int)colorArray[0];
                    byte g = (byte)(int)colorArray[1];
                    byte b = (byte)(int)colorArray[2];

                    // Find and update the TerrainData in registry
                    foreach (var terrain in terrainRegistry.GetAll())
                    {
                        if (terrain.Key == key)
                        {
                            terrain.ColorR = r;
                            terrain.ColorG = g;
                            terrain.ColorB = b;

                            // Update GPU palette
                            if (dataTextures?.TerrainColorPalette != null && terrain.TerrainId < 32)
                            {
                                dataTextures.TerrainColorPalette.SetPixel(
                                    terrain.TerrainId, 0, new Color32(r, g, b, 255));
                            }
                            break;
                        }
                    }
                }

                dataTextures?.TerrainColorPalette?.Apply(false);
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"ProvinceTerrainEditorUI: Failed to reload base terrain colors: {e.Message}", "starter_kit");
            }
        }

        private void RebuildTerrainEntries()
        {
            terrainEntries.Clear();
            if (terrainRegistry == null) return;

            foreach (var terrain in terrainRegistry.GetAll())
            {
                terrainEntries.Add(new TerrainEntry
                {
                    Key = terrain.Key,
                    Name = terrain.Name,
                    TerrainId = terrain.TerrainId,
                    Color = new Color(terrain.ColorR / 255f, terrain.ColorG / 255f, terrain.ColorB / 255f, 1f),
                    IsWater = terrain.IsWater
                });
            }
        }

        private void HandleMapModeToggle()
        {
            var initializer = Initializer.Instance;
            if (initializer == null) return;

            if (!showingTerrain)
            {
                previousMapMode = initializer.GetCurrentMapMode();
                initializer.SetMapMode(MapMode.Terrain);
                showingTerrain = true;
                mapModeToggleButton.text = "Show Political";
            }
            else
            {
                initializer.SetMapMode(previousMapMode);
                showingTerrain = false;
                mapModeToggleButton.text = "Show Terrain";
            }
        }

        private void HandleSaveClicked()
        {
            if (!Core.Modding.DataFileResolver.IsInitialized)
            {
                ArchonLogger.LogError("ProvinceTerrainEditorUI: DataFileResolver not initialized", "starter_kit");
                return;
            }

            // Save province terrain changes to override directory (StreamingAssets/Data/)
            string overrideDir = Core.Modding.DataFileResolver.OverrideDirectory;
            int saved = ProvinceTerrainFilePatcher.SaveAll(pendingChanges, overrideDir);
            ArchonLogger.Log($"ProvinceTerrainEditorUI: Saved {saved}/{pendingChanges.Count} terrain changes to disk", "starter_kit");
            if (saved == pendingChanges.Count)
                pendingChanges.Clear();

            // Save color changes (json5 + png)
            int colorsSaved = 0;
            foreach (var kvp in pendingColorChanges)
            {
                TerrainData terrain = null;
                foreach (var t in terrainRegistry.GetAll())
                {
                    if (t.Key == kvp.Key) { terrain = t; break; }
                }
                if (terrain != null && TerrainColorPatcher.SaveColorToDisk(terrain, kvp.Value.oldColor, kvp.Value.newColor, overrideDir))
                    colorsSaved++;
            }
            if (colorsSaved > 0)
                ArchonLogger.Log($"ProvinceTerrainEditorUI: Saved {colorsSaved} terrain color changes to disk", "starter_kit");
            pendingColorChanges.Clear();

            UpdatePendingLabel();
        }

        private void HandleBackClicked()
        {
            if (pendingChanges.Count > 0)
            {
                ArchonLogger.LogWarning(
                    $"ProvinceTerrainEditorUI: Leaving with {pendingChanges.Count} unsaved changes",
                    "starter_kit");
            }

            Hide();
            OnBackClicked?.Invoke();
        }

        private SliderInt CreateColorSlider(string label)
        {
            var slider = new SliderInt(label, 0, 255);
            slider.style.marginBottom = SpacingXs;
            slider.RegisterValueChangedCallback(evt => UpdateColorPreview());
            return slider;
        }

        private void UpdateColorPreview()
        {
            if (colorPreview == null) return;
            var c = new Color(sliderR.value / 255f, sliderG.value / 255f, sliderB.value / 255f, 1f);
            colorPreview.style.backgroundColor = c;
        }

        private void ShowColorEditor(TerrainEntry entry)
        {
            colorEditorContainer.style.display = DisplayStyle.Flex;
            colorEditorLabel.text = $"Edit Color: {entry.Name}";
            sliderR.SetValueWithoutNotify((int)(entry.Color.r * 255f));
            sliderG.SetValueWithoutNotify((int)(entry.Color.g * 255f));
            sliderB.SetValueWithoutNotify((int)(entry.Color.b * 255f));
            UpdateColorPreview();
        }

        private void HandleApplyColor()
        {
            if (string.IsNullOrEmpty(selectedTerrainKey) || terrainRegistry == null) return;

            // Find the TerrainData in registry
            TerrainData terrain = null;
            foreach (var t in terrainRegistry.GetAll())
            {
                if (t.Key == selectedTerrainKey)
                {
                    terrain = t;
                    break;
                }
            }
            if (terrain == null) return;

            var newColor = new Color32((byte)sliderR.value, (byte)sliderG.value, (byte)sliderB.value, 255);
            var oldColor = new Color32(terrain.ColorR, terrain.ColorG, terrain.ColorB, 255);

            // Live preview only — no disk writes
            bool success = TerrainColorPatcher.ApplyColorLive(terrain, newColor, dataTextures);

            if (success)
            {
                // Track for save-to-disk
                pendingColorChanges[terrain.Key] = (oldColor, newColor);

                // Update local terrain entry list
                for (int i = 0; i < terrainEntries.Count; i++)
                {
                    if (terrainEntries[i].Key == selectedTerrainKey)
                    {
                        var updated = terrainEntries[i];
                        updated.Color = new Color(newColor.r / 255f, newColor.g / 255f, newColor.b / 255f, 1f);
                        terrainEntries[i] = updated;
                        break;
                    }
                }

                // Rebuild palette UI to show new color swatch
                RebuildPalette();
                UpdatePendingLabel();
            }
        }

        private VisualElement CreateDivider()
        {
            var divider = new VisualElement();
            divider.style.height = 1f;
            divider.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            divider.style.marginTop = SpacingSm;
            divider.style.marginBottom = SpacingSm;
            return divider;
        }
    }
}
