using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Core;

namespace Archon.Engine.Map
{
    /// <summary>
    /// ENGINE: Manages and applies visual styles to the rendering system
    /// Bridge between visual style configuration (ScriptableObject) and rendering mechanism (MapTextureManager, shaders)
    /// </summary>
    public class VisualStyleManager : MonoBehaviour
    {
        [Header("Visual Style")]
        [Tooltip("Active visual style configuration")]
        [SerializeField] private VisualStyleConfiguration activeStyle;

        [Header("References")]
        [SerializeField] private MeshRenderer mapMeshRenderer;
        [SerializeField] private bool logStyleApplication = true;

        private MapTextureManager textureManager;
        private BorderComputeDispatcher borderDispatcher;
        private Material runtimeMaterial;  // Instance of the style's material

        void Start()
        {
            // Find ENGINE components
            textureManager = FindFirstObjectByType<MapTextureManager>();
            borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager.Start: textureManager={textureManager != null}, borderDispatcher={borderDispatcher != null}", "map_rendering");
            }

            // Find map mesh renderer if not assigned
            if (mapMeshRenderer == null)
            {
                mapMeshRenderer = FindFirstObjectByType<MeshRenderer>();
            }

            // NOTE: Do NOT apply style here!
            // VisualStyleManager is now passive - initialization layer controls when to apply
            // This ensures style is applied BEFORE map loads, so textures bind to correct material
        }

        /// <summary>
        /// Apply a visual style to the map rendering system
        /// </summary>
        public void ApplyStyle(VisualStyleConfiguration style)
        {
            if (style == null)
            {
                ArchonLogger.LogError("VisualStyleManager: Cannot apply null style", "map_rendering");
                return;
            }

            if (style.mapMaterial == null)
            {
                ArchonLogger.LogError($"VisualStyleManager: Visual style '{style.styleName}' has no material assigned!", "map_rendering");
                return;
            }

            if (mapMeshRenderer == null)
            {
                ArchonLogger.LogError("VisualStyleManager: No MeshRenderer found - cannot apply visual style", "map_rendering");
                return;
            }

            activeStyle = style;

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager: Applying visual style '{style.styleName}'", "map_rendering");
                style.LogConfiguration();
            }

            // CRITICAL: Swap the entire material (complete shader)
            mapMeshRenderer.material = style.mapMaterial;
            runtimeMaterial = mapMeshRenderer.material; // Get runtime instance

            // Re-bind ENGINE textures to the new material (they were bound to old material)
            if (textureManager != null)
            {
                textureManager.BindTexturesToMaterial(runtimeMaterial);
            }

            // CRITICAL: Rebind map mode data textures (CountryColorPalette, etc.) after material swap
            var mapModeManager = FindFirstObjectByType<MapModeManager>();
            if (mapModeManager != null)
            {
                mapModeManager.UpdateMaterial(runtimeMaterial);
                ArchonLogger.Log("VisualStyleManager: Rebound map mode textures after material swap", "map_rendering");
            }

            // Apply parameter tweaks to the material
            ApplyBorderStyle(style.borders);
            ApplyMapModeColors(style.mapModes);
            ApplyAdvancedEffects(style.advancedEffects);
            ApplyFogOfWarSettings(style.fogOfWar);
            ApplyTessellationSettings(style.tessellation);

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager: ✓ Visual style '{style.styleName}' applied successfully", "map_rendering");
                ArchonLogger.Log($"  Material: {runtimeMaterial.name}", "map_rendering");
                ArchonLogger.Log($"  Shader: {runtimeMaterial.shader.name}", "map_rendering");
            }
        }

        /// <summary>
        /// Apply border visual style parameters to the material
        /// </summary>
        private void ApplyBorderStyle(VisualStyleConfiguration.BorderStyle borders)
        {
            if (runtimeMaterial == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: Cannot apply border style - no runtime material", "map_rendering");
                return;
            }

            // Set border parameters on the material
            runtimeMaterial.SetFloat("_CountryBorderStrength", borders.countryBorderStrength);
            runtimeMaterial.SetColor("_CountryBorderColor", borders.countryBorderColor);
            runtimeMaterial.SetFloat("_ProvinceBorderStrength", borders.provinceBorderStrength);
            runtimeMaterial.SetColor("_ProvinceBorderColor", borders.provinceBorderColor);

            // DEBUG: Verify the values were actually set
            float actualCountryStrength = runtimeMaterial.GetFloat("_CountryBorderStrength");
            float actualProvinceStrength = runtimeMaterial.GetFloat("_ProvinceBorderStrength");
            ArchonLogger.Log($"VisualStyleManager: Border strengths set - Country: {actualCountryStrength}, Province: {actualProvinceStrength}", "map_rendering");

            // Note: Border mode application deferred until ENGINE components exist
            // Use ApplyBorderConfiguration() after map initialization
        }

        /// <summary>
        /// Apply border configuration from style (called after map initialization when BorderDispatcher exists)
        /// </summary>
        public void ApplyBorderConfiguration(VisualStyleConfiguration style)
        {
            if (borderDispatcher == null)
            {
                borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();
            }

            if (borderDispatcher != null)
            {
                var engineBorderMode = ConvertBorderMode(style.borders.defaultBorderMode);

                // Set all border parameters at once to avoid redundant DetectBorders() calls
                borderDispatcher.SetBorderParameters(
                    engineBorderMode,
                    style.borders.countryBorderThickness,
                    style.borders.provinceBorderThickness,
                    style.borders.borderAntiAliasing,
                    updateBorders: false  // Don't update yet - we'll do it after populating owner texture
                );

                if (style.borders.enableBordersOnStartup)
                {
                    // CRITICAL: Populate owner texture BEFORE detecting borders
                    // Border detection needs owner data to distinguish country vs province borders
                    var ownerDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                    var gameState = FindFirstObjectByType<GameState>();
                    if (ownerDispatcher != null && gameState != null && gameState.ProvinceQueries != null)
                    {
                        ownerDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);

                        // CRITICAL: Force GPU synchronization to ensure owner texture is fully written
                        // before border detection reads it (async compute shader race condition)
                        var texManager = textureManager ?? FindFirstObjectByType<MapTextureManager>();
                        if (texManager != null && texManager.ProvinceOwnerTexture != null)
                        {
                            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(texManager.ProvinceOwnerTexture);
                            syncRequest.WaitForCompletion();
                        }

                        if (logStyleApplication)
                        {
                            ArchonLogger.Log("VisualStyleManager: Populated owner texture for border detection (GPU synced)", "map_rendering");
                        }
                    }

                    borderDispatcher.DetectBorders();
                    if (logStyleApplication)
                    {
                        ArchonLogger.Log($"VisualStyleManager: Applied {style.borders.defaultBorderMode} border mode (country: {style.borders.countryBorderThickness}px, province: {style.borders.provinceBorderThickness}px, AA: {style.borders.borderAntiAliasing:F1}) from visual style", "map_rendering");
                    }
                }
            }
            else
            {
                ArchonLogger.LogWarning("VisualStyleManager: BorderDispatcher not found - cannot apply border configuration", "map_rendering");
            }
        }


        /// <summary>
        /// Apply map mode color parameters to the material
        /// </summary>
        private void ApplyMapModeColors(VisualStyleConfiguration.MapModeColors colors)
        {
            if (runtimeMaterial == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: Cannot apply map mode colors - no runtime material", "map_rendering");
                return;
            }

            // Set ocean and unowned colors
            runtimeMaterial.SetColor("_OceanColor", colors.oceanColor);
            runtimeMaterial.SetColor("_UnownedLandColor", colors.unownedLandColor);

            // Set terrain adjustments
            runtimeMaterial.SetFloat("_TerrainBrightness", colors.terrainBrightness);
            runtimeMaterial.SetFloat("_TerrainSaturation", colors.terrainSaturation);

            // Set terrain detail mapping
            runtimeMaterial.SetFloat("_DetailTiling", colors.detailTiling);
            runtimeMaterial.SetFloat("_DetailStrength", colors.detailStrength);

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager: Detail mapping - Tiling: {colors.detailTiling}, Strength: {colors.detailStrength}", "map_rendering");
            }

            // Set normal map lighting parameters
            runtimeMaterial.SetFloat("_NormalMapStrength", colors.normalMapStrength);
            runtimeMaterial.SetFloat("_NormalMapAmbient", colors.normalMapAmbient);
            runtimeMaterial.SetFloat("_NormalMapHighlight", colors.normalMapHighlight);
        }

        /// <summary>
        /// Apply advanced visual effects (overlay texture, color adjustments)
        /// </summary>
        private void ApplyAdvancedEffects(VisualStyleConfiguration.AdvancedEffects effects)
        {
            if (runtimeMaterial == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: Cannot apply advanced effects - no runtime material", "map_rendering");
                return;
            }

            // Apply overlay texture (parchment, paper, canvas, etc.)
            if (effects.overlayTexture != null)
            {
                runtimeMaterial.SetTexture("_OverlayTexture", effects.overlayTexture);
            }
            runtimeMaterial.SetFloat("_OverlayStrength", effects.overlayStrength);

            // Apply color saturation
            runtimeMaterial.SetFloat("_CountryColorSaturation", effects.countryColorSaturation);
        }

        /// <summary>
        /// Apply fog of war visual settings to the material
        /// </summary>
        private void ApplyFogOfWarSettings(VisualStyleConfiguration.FogOfWarSettings fogOfWar)
        {
            if (runtimeMaterial == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: Cannot apply fog of war settings - no runtime material", "map_rendering");
                return;
            }

            // Set fog of war enabled/disabled
            runtimeMaterial.SetFloat("_FogOfWarEnabled", fogOfWar.enabled ? 1f : 0f);

            // Set fog of war colors
            runtimeMaterial.SetColor("_FogUnexploredColor", fogOfWar.unexploredColor);
            runtimeMaterial.SetColor("_FogExploredColor", fogOfWar.exploredTint);
            runtimeMaterial.SetFloat("_FogExploredDesaturation", fogOfWar.exploredDesaturation);

            // Set fog of war noise
            runtimeMaterial.SetColor("_FogNoiseColor", fogOfWar.noiseColor);
            runtimeMaterial.SetFloat("_FogNoiseScale", fogOfWar.noiseScale);
            runtimeMaterial.SetFloat("_FogNoiseStrength", fogOfWar.noiseStrength);
            runtimeMaterial.SetFloat("_FogNoiseSpeed", fogOfWar.noiseSpeed);

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager: Fog of war {(fogOfWar.enabled ? "enabled" : "disabled")}", "map_rendering");
            }
        }

        /// <summary>
        /// Apply tessellation (3D terrain) settings to the material
        /// </summary>
        private void ApplyTessellationSettings(VisualStyleConfiguration.TessellationSettings tessellation)
        {
            if (runtimeMaterial == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: Cannot apply tessellation settings - no runtime material", "map_rendering");
                return;
            }

            // Only apply if material has tessellation properties (check if shader supports it)
            if (runtimeMaterial.HasProperty("_HeightScale"))
            {
                runtimeMaterial.SetFloat("_HeightScale", tessellation.heightScale);
                runtimeMaterial.SetFloat("_TessellationFactor", tessellation.tessellationFactor);
                runtimeMaterial.SetFloat("_TessellationMinDistance", tessellation.tessellationMinDistance);
                runtimeMaterial.SetFloat("_TessellationMaxDistance", tessellation.tessellationMaxDistance);

                if (logStyleApplication)
                {
                    ArchonLogger.Log($"VisualStyleManager: Tessellation settings applied (height: {tessellation.heightScale}, factor: {tessellation.tessellationFactor}, range: {tessellation.tessellationMinDistance}-{tessellation.tessellationMaxDistance})", "map_rendering");
                }
            }
            else if (logStyleApplication)
            {
                ArchonLogger.Log("VisualStyleManager: Tessellation settings skipped (shader does not support tessellation)", "map_rendering");
            }
        }

        /// <summary>
        /// Convert VisualStyleConfiguration BorderMode enum to ENGINE BorderMode enum
        /// </summary>
        private BorderMode ConvertBorderMode(VisualStyleConfiguration.BorderStyle.BorderMode mode)
        {
            switch (mode)
            {
                case VisualStyleConfiguration.BorderStyle.BorderMode.Province:
                    return BorderMode.Province;
                case VisualStyleConfiguration.BorderStyle.BorderMode.Country:
                    return BorderMode.Country;
                case VisualStyleConfiguration.BorderStyle.BorderMode.Thick:
                    return BorderMode.Thick;
                case VisualStyleConfiguration.BorderStyle.BorderMode.Dual:
                    return BorderMode.Dual;
                case VisualStyleConfiguration.BorderStyle.BorderMode.None:
                    return BorderMode.None;
                default:
                    return BorderMode.Dual;
            }
        }

        /// <summary>
        /// Runtime style switching (for settings menu, etc.)
        /// </summary>
        public void SwitchStyle(VisualStyleConfiguration newStyle)
        {
            if (newStyle == null)
            {
                ArchonLogger.LogError("VisualStyleManager: Cannot switch to null style", "map_rendering");
                return;
            }

            ApplyStyle(newStyle);

            // Apply border configuration with new thickness and anti-aliasing
            if (borderDispatcher != null)
            {
                var engineBorderMode = ConvertBorderMode(newStyle.borders.defaultBorderMode);
                borderDispatcher.SetBorderParameters(
                    engineBorderMode,
                    newStyle.borders.countryBorderThickness,
                    newStyle.borders.provinceBorderThickness,
                    newStyle.borders.borderAntiAliasing,
                    updateBorders: true  // Update immediately
                );
            }
        }

        /// <summary>
        /// Get currently active style
        /// </summary>
        public VisualStyleConfiguration GetActiveStyle()
        {
            return activeStyle;
        }

        /// <summary>
        /// Reload material - applies ScriptableObject settings and refreshes rendering
        /// Use this for F5 reloading during development (picks up ScriptableObject changes)
        /// </summary>
        public void ReloadMaterialFromAsset(VisualStyleConfiguration style)
        {
            if (style == null)
            {
                ArchonLogger.LogError("VisualStyleManager: Cannot reload null style", "map_rendering");
                return;
            }

            // Re-find components if they weren't available at Start() time
            if (textureManager == null)
            {
                textureManager = FindFirstObjectByType<MapTextureManager>();
            }

            if (borderDispatcher == null)
            {
                borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();
            }

            ArchonLogger.Log($"VisualStyleManager.ReloadMaterialFromAsset: textureManager={textureManager != null}, borderDispatcher={borderDispatcher != null}", "map_rendering");

            // Apply style settings from ScriptableObject (replaces material instance with fresh copy)
            ApplyStyle(style);

            ArchonLogger.Log($"VisualStyleManager.ReloadMaterialFromAsset: Style applied from ScriptableObject", "map_rendering");

            // Notify MapModeManager to refresh
            var mapModeManager = FindFirstObjectByType<MapModeManager>();
            if (mapModeManager != null && mapModeManager.IsInitialized)
            {
                ArchonLogger.Log("VisualStyleManager.ReloadMaterialFromAsset: Calling MapModeManager.UpdateMaterial", "map_rendering");
                mapModeManager.UpdateMaterial(runtimeMaterial);
            }

            // Regenerate borders with thickness and anti-aliasing from ScriptableObject
            if (borderDispatcher != null)
            {
                var engineBorderMode = ConvertBorderMode(style.borders.defaultBorderMode);
                ArchonLogger.Log($"VisualStyleManager.ReloadMaterialFromAsset: Calling BorderComputeDispatcher.SetBorderParameters (mode: {engineBorderMode}, country: {style.borders.countryBorderThickness}px, province: {style.borders.provinceBorderThickness}px, AA: {style.borders.borderAntiAliasing:F1})", "map_rendering");
                borderDispatcher.SetBorderParameters(
                    engineBorderMode,
                    style.borders.countryBorderThickness,
                    style.borders.provinceBorderThickness,
                    style.borders.borderAntiAliasing,
                    updateBorders: true  // Update immediately
                );
            }

            ArchonLogger.Log("VisualStyleManager.ReloadMaterialFromAsset: Complete - style reloaded from ScriptableObject", "map_rendering");
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Context menu: Reapply current style (useful for testing)
        /// </summary>
        [ContextMenu("Reapply Current Style")]
        private void ReapplyCurrentStyle()
        {
            if (activeStyle != null)
            {
                ApplyStyle(activeStyle);
            }
            else
            {
                ArchonLogger.LogWarning("VisualStyleManager: No active style to reapply", "map_rendering");
            }
        }

        /// <summary>
        /// Context menu: Apply default style for testing
        /// </summary>
        [ContextMenu("Test: Apply Default Style")]
        private void TestApplyDefault()
        {
            var defaultStyle = VisualStyleConfiguration.CreateDefault();
            ApplyStyle(defaultStyle);
        }
        #endif
    }
}
