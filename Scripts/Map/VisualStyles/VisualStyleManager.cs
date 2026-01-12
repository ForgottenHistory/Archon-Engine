using UnityEngine;
using Map.Rendering;
using Map.Rendering.Border;
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

            // Set border rendering mode on the material
            // Shader values: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
            int renderingModeValue = GetShaderModeValue(borders.renderingMode);
            runtimeMaterial.SetInt("_BorderRenderingMode", renderingModeValue);

            // Set border colors and strengths on the material
            runtimeMaterial.SetFloat("_CountryBorderStrength", borders.countryBorderStrength);
            runtimeMaterial.SetColor("_CountryBorderColor", borders.countryBorderColor);
            runtimeMaterial.SetFloat("_ProvinceBorderStrength", borders.provinceBorderStrength);
            runtimeMaterial.SetColor("_ProvinceBorderColor", borders.provinceBorderColor);

            // Set distance field parameters (used by ShaderDistanceField mode)
            runtimeMaterial.SetFloat("_EdgeWidth", borders.edgeWidth);
            runtimeMaterial.SetFloat("_GradientWidth", borders.gradientWidth);
            runtimeMaterial.SetFloat("_EdgeSmoothness", borders.edgeSmoothness);
            runtimeMaterial.SetFloat("_EdgeColorMul", borders.edgeColorMultiplier);
            runtimeMaterial.SetFloat("_GradientColorMul", borders.gradientColorMultiplier);
            runtimeMaterial.SetFloat("_EdgeAlpha", borders.edgeAlpha);
            runtimeMaterial.SetFloat("_GradientAlphaInside", borders.gradientAlphaInside);
            runtimeMaterial.SetFloat("_GradientAlphaOutside", borders.gradientAlphaOutside);

            if (logStyleApplication)
            {
                ArchonLogger.Log($"VisualStyleManager: Border style applied - Rendering: {borders.renderingMode} (shader mode={renderingModeValue}), Country: {borders.countryBorderStrength:P0}, Province: {borders.provinceBorderStrength:P0}", "map_rendering");
            }

            // Note: Border compute shader settings applied in ApplyBorderConfiguration()
        }

        /// <summary>
        /// Convert BorderRenderingMode enum to shader integer value
        /// Shader values: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
        /// </summary>
        private int GetShaderModeValue(BorderRenderingMode mode)
        {
            switch (mode)
            {
                case BorderRenderingMode.None: return 0;
                case BorderRenderingMode.ShaderDistanceField: return 1;
                case BorderRenderingMode.ShaderPixelPerfect: return 2;
                case BorderRenderingMode.MeshGeometry: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Apply border configuration from style (called after map initialization when BorderDispatcher exists)
        /// Sets rendering mode, border mode, and triggers border generation.
        /// Supports custom renderer IDs from MapRendererRegistry.
        /// </summary>
        public void ApplyBorderConfiguration(VisualStyleConfiguration style)
        {
            if (borderDispatcher == null)
            {
                borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();
            }

            if (borderDispatcher != null)
            {
                // Get the effective renderer ID (custom or from enum)
                string effectiveRendererId = style.borders.GetEffectiveRendererId();

                // Check if using a custom renderer via registry
                var registry = MapRendererRegistry.Instance;
                bool usingCustomRenderer = registry != null &&
                                          !string.IsNullOrEmpty(style.borders.customRendererId) &&
                                          registry.HasBorderRenderer(style.borders.customRendererId);

                if (usingCustomRenderer)
                {
                    // Use registry-based renderer selection
                    var gameState = FindFirstObjectByType<GameState>();
                    borderDispatcher.SetActiveBorderRenderer(effectiveRendererId, gameState?.ProvinceQueries);

                    // Apply style params to the renderer
                    var renderer = registry.GetBorderRenderer(effectiveRendererId);
                    if (renderer != null && runtimeMaterial != null)
                    {
                        var styleParams = BuildBorderStyleParams(style.borders);
                        renderer.ApplyToMaterial(runtimeMaterial, styleParams);
                    }

                    if (logStyleApplication)
                    {
                        ArchonLogger.Log($"VisualStyleManager: Using custom renderer '{effectiveRendererId}' from registry", "map_rendering");
                    }
                }
                else
                {
                    // Use legacy enum-based rendering mode
                    borderDispatcher.SetBorderRenderingMode(style.borders.renderingMode);
                }

                // Set border mode (which borders to show)
                var engineBorderMode = ConvertBorderMode(style.borders.borderMode);
                borderDispatcher.SetBorderMode(engineBorderMode);

                // Set pixel-perfect parameters from VisualStyleConfiguration
                borderDispatcher.SetPixelPerfectParameters(
                    style.borders.pixelPerfectCountryThickness,
                    style.borders.pixelPerfectProvinceThickness,
                    style.borders.pixelPerfectAntiAliasing
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
                        ArchonLogger.Log($"VisualStyleManager: Applied borders - Renderer: {effectiveRendererId}, Mode: {style.borders.borderMode}", "map_rendering");
                    }
                }
            }
            else
            {
                ArchonLogger.LogWarning("VisualStyleManager: BorderDispatcher not found - cannot apply border configuration", "map_rendering");
            }
        }

        /// <summary>
        /// Build BorderStyleParams from VisualStyleConfiguration.BorderStyle
        /// </summary>
        private BorderStyleParams BuildBorderStyleParams(VisualStyleConfiguration.BorderStyle borders)
        {
            return new BorderStyleParams
            {
                CountryBorderColor = borders.countryBorderColor,
                ProvinceBorderColor = borders.provinceBorderColor,
                CountryBorderStrength = borders.countryBorderStrength,
                ProvinceBorderStrength = borders.provinceBorderStrength,
                PixelPerfectCountryThickness = borders.pixelPerfectCountryThickness,
                PixelPerfectProvinceThickness = borders.pixelPerfectProvinceThickness,
                PixelPerfectAntiAliasing = borders.pixelPerfectAntiAliasing,
                EdgeWidth = borders.edgeWidth,
                GradientWidth = borders.gradientWidth,
                EdgeSmoothness = borders.edgeSmoothness
            };
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
        /// Convert VisualStyleConfiguration BorderModeType enum to ENGINE BorderMode enum
        /// </summary>
        private BorderMode ConvertBorderMode(VisualStyleConfiguration.BorderStyle.BorderModeType mode)
        {
            switch (mode)
            {
                case VisualStyleConfiguration.BorderStyle.BorderModeType.Province:
                    return BorderMode.Province;
                case VisualStyleConfiguration.BorderStyle.BorderModeType.Country:
                    return BorderMode.Country;
                case VisualStyleConfiguration.BorderStyle.BorderModeType.Thick:
                    return BorderMode.Thick;
                case VisualStyleConfiguration.BorderStyle.BorderModeType.Dual:
                    return BorderMode.Dual;
                case VisualStyleConfiguration.BorderStyle.BorderModeType.None:
                    return BorderMode.None;
                default:
                    return BorderMode.Dual;
            }
        }

        /// <summary>
        /// Runtime style switching (for settings menu, etc.)
        /// Supports custom renderer IDs from MapRendererRegistry.
        /// </summary>
        public void SwitchStyle(VisualStyleConfiguration newStyle)
        {
            if (newStyle == null)
            {
                ArchonLogger.LogError("VisualStyleManager: Cannot switch to null style", "map_rendering");
                return;
            }

            ApplyStyle(newStyle);

            // Apply border configuration from VisualStyles
            if (borderDispatcher != null)
            {
                string effectiveRendererId = newStyle.borders.GetEffectiveRendererId();
                var registry = MapRendererRegistry.Instance;
                bool usingCustomRenderer = registry != null &&
                                          !string.IsNullOrEmpty(newStyle.borders.customRendererId) &&
                                          registry.HasBorderRenderer(newStyle.borders.customRendererId);

                if (usingCustomRenderer)
                {
                    var gameState = FindFirstObjectByType<GameState>();
                    borderDispatcher.SetActiveBorderRenderer(effectiveRendererId, gameState?.ProvinceQueries);

                    var renderer = registry.GetBorderRenderer(effectiveRendererId);
                    if (renderer != null && runtimeMaterial != null)
                    {
                        var styleParams = BuildBorderStyleParams(newStyle.borders);
                        renderer.ApplyToMaterial(runtimeMaterial, styleParams);
                    }
                }
                else
                {
                    borderDispatcher.SetBorderRenderingMode(newStyle.borders.renderingMode);
                }

                var engineBorderMode = ConvertBorderMode(newStyle.borders.borderMode);
                borderDispatcher.SetBorderMode(engineBorderMode);
                borderDispatcher.SetPixelPerfectParameters(
                    newStyle.borders.pixelPerfectCountryThickness,
                    newStyle.borders.pixelPerfectProvinceThickness,
                    newStyle.borders.pixelPerfectAntiAliasing
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

            // Apply border rendering mode and regenerate borders from ScriptableObject
            if (borderDispatcher != null)
            {
                string effectiveRendererId = style.borders.GetEffectiveRendererId();
                var registry = MapRendererRegistry.Instance;
                bool usingCustomRenderer = registry != null &&
                                          !string.IsNullOrEmpty(style.borders.customRendererId) &&
                                          registry.HasBorderRenderer(style.borders.customRendererId);

                if (usingCustomRenderer)
                {
                    var gameState = FindFirstObjectByType<GameState>();
                    borderDispatcher.SetActiveBorderRenderer(effectiveRendererId, gameState?.ProvinceQueries);

                    var renderer = registry.GetBorderRenderer(effectiveRendererId);
                    if (renderer != null && runtimeMaterial != null)
                    {
                        var styleParams = BuildBorderStyleParams(style.borders);
                        renderer.ApplyToMaterial(runtimeMaterial, styleParams);
                    }
                }
                else
                {
                    borderDispatcher.SetBorderRenderingMode(style.borders.renderingMode);
                }

                var engineBorderMode = ConvertBorderMode(style.borders.borderMode);
                borderDispatcher.SetBorderMode(engineBorderMode);
                borderDispatcher.SetPixelPerfectParameters(
                    style.borders.pixelPerfectCountryThickness,
                    style.borders.pixelPerfectProvinceThickness,
                    style.borders.pixelPerfectAntiAliasing
                );

                ArchonLogger.Log($"VisualStyleManager.ReloadMaterialFromAsset: Applied borders - Renderer: {effectiveRendererId}, Mode: {style.borders.borderMode}", "map_rendering");
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
