using UnityEngine;
using Map.Rendering;
using Map.Rendering.Border;
using Map.MapModes;
using Map.Core;
using Core;

namespace Archon.Engine.Map
{
    /// <summary>
    /// ENGINE: Manages and applies visual styles to the rendering system.
    /// Bridge between visual style configuration (ScriptableObject) and rendering mechanism.
    ///
    /// Configuration is provided via Configure() method from ArchonEngine.
    /// No Inspector assignments needed - all references injected at runtime.
    /// </summary>
    public class VisualStyleManager : MonoBehaviour
    {
        // Configuration (injected via Configure method)
        private VisualStyleConfiguration activeStyle;
        private MeshRenderer mapMeshRenderer;

        private bool LogStyle => GameSettings.Instance?.ShouldLog(LogLevel.Info) ?? false;

        // Cached component references
        private MapTextureManager textureManager;
        private BorderComputeDispatcher borderDispatcher;
        private Material runtimeMaterial;  // Instance of the style's material

        /// <summary>
        /// Configure the manager with required references.
        /// Must be called before ApplyStyle().
        /// </summary>
        public void Configure(MeshRenderer meshRenderer, VisualStyleConfiguration style = null)
        {
            mapMeshRenderer = meshRenderer;
            activeStyle = style;
        }

        /// <summary>
        /// Set component references. Called by ArchonEngine after components are created.
        /// </summary>
        public void SetComponents(MapTextureManager texManager, BorderComputeDispatcher borders)
        {
            textureManager = texManager;
            borderDispatcher = borders;

            if (LogStyle)
            {
                ArchonLogger.Log($"VisualStyleManager: Components set - textureManager={textureManager != null}, borderDispatcher={borderDispatcher != null}", "map_rendering");
            }
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

            if (LogStyle)
            {
                ArchonLogger.Log($"VisualStyleManager: Applying visual style '{style.styleName}'", "map_rendering");
                style.LogConfiguration();
            }

            // CRITICAL: Swap the entire material (complete shader)
            mapMeshRenderer.material = style.mapMaterial;
            runtimeMaterial = mapMeshRenderer.material; // Get runtime instance

            // Textures are shader globals and survive the swap. Republish anyway: some textures
            // are created late in startup and this is the last guaranteed bind point.
            if (textureManager != null)
                textureManager.BindGlobalTextures();

            // Re-activate the current map mode so per-material state (shader keywords) lands on the
            // new material, and republish map mode textures (they share names with core textures).
            var mapModeManager = FindFirstObjectByType<MapModeManager>();
            if (mapModeManager != null && mapModeManager.IsInitialized)
            {
                mapModeManager.UpdateMaterial(runtimeMaterial);
                mapModeManager.RebindTextures();
            }

            // Style values -> shader globals; tessellation -> material
            ApplyBorderStyle(style.borders);
            ApplyMapModeColors(style.mapModes);
            ApplyAdvancedEffects(style.advancedEffects);
            ApplyFogOfWarSettings(style.fogOfWar);
            ApplyTessellationSettings(style.tessellation);
            ApplyCustomShaderParameters(style);

            // Check the shader against the engine/shader contract (logs errors naming shader + property)
            int problems = MapShaderParameterRegistry.ValidateMaterial(runtimeMaterial);
            if (problems > 0)
            {
                ArchonLogger.LogError($"VisualStyleManager: Shader '{runtimeMaterial.shader.name}' has {problems} contract problem(s), see errors above", "map_rendering");
            }

            if (LogStyle)
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
            MapShaderGlobals.ApplyBorderStyle(borders);

            if (LogStyle)
            {
                ArchonLogger.Log($"VisualStyleManager: Border style applied - Rendering: {borders.renderingMode} (shader mode={MapShaderGlobals.GetBorderShaderMode(borders.renderingMode)}), Country: {borders.countryBorderStrength:P0}, Province: {borders.provinceBorderStrength:P0}", "map_rendering");
            }

            // Note: Border compute shader settings applied in ApplyBorderConfiguration()
        }

        /// <summary>
        /// Apply border configuration from style.
        /// Initializes the border system with the correct mode and generates borders.
        /// </summary>
        public void ApplyBorderConfiguration(VisualStyleConfiguration style)
        {
            if (borderDispatcher == null)
            {
                borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();
            }

            if (borderDispatcher == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: BorderDispatcher not found", "map_rendering");
                return;
            }

            // Get required systems for border initialization
            var gameState = FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: GameState not found", "map_rendering");
                return;
            }

            var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
            if (mapSystemCoordinator == null)
            {
                ArchonLogger.LogWarning("VisualStyleManager: MapSystemCoordinator not found", "map_rendering");
                return;
            }

            // Initialize border system with the selected mode
            borderDispatcher.InitializeBorders(
                style.borders.renderingMode,
                gameState.Adjacencies,
                gameState.Provinces,
                gameState.Countries,
                mapSystemCoordinator.ProvinceMapping,
                mapSystemCoordinator.GetMapPlaneTransform()
            );

            // Set border mode (which borders to show)
            borderDispatcher.SetBorderMode(ConvertBorderMode(style.borders.borderMode));

            // Set pixel-perfect parameters
            borderDispatcher.SetPixelPerfectParameters(
                style.borders.pixelPerfectCountryThickness,
                style.borders.pixelPerfectProvinceThickness,
                style.borders.pixelPerfectAntiAliasing
            );

            if (style.borders.enableBordersOnStartup)
            {
                // Populate owner texture BEFORE detecting borders
                var ownerDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                if (ownerDispatcher != null && gameState.ProvinceQueries != null)
                {
                    ownerDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);

                    // Force GPU sync before border detection
                    var texManager = textureManager ?? FindFirstObjectByType<MapTextureManager>();
                    if (texManager?.ProvinceOwnerTexture != null)
                    {
                        var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(texManager.ProvinceOwnerTexture);
                        syncRequest.WaitForCompletion();
                    }
                }

                borderDispatcher.DetectBorders();

                if (LogStyle)
                {
                    ArchonLogger.Log($"VisualStyleManager: Applied borders - Mode: {style.borders.renderingMode}, BorderMode: {style.borders.borderMode}", "map_rendering");
                }
            }
        }

        /// <summary>
        /// Apply map mode color parameters to the material
        /// </summary>
        private void ApplyMapModeColors(VisualStyleConfiguration.MapModeColors colors)
        {
            MapShaderGlobals.ApplyMapModeColors(colors);

            if (LogStyle)
            {
                ArchonLogger.Log($"VisualStyleManager: Detail mapping - Tiling: {colors.detailTiling}, Strength: {colors.detailStrength}", "map_rendering");
            }
        }

        /// <summary>
        /// Apply advanced visual effects (overlay texture, color adjustments)
        /// </summary>
        private void ApplyAdvancedEffects(VisualStyleConfiguration.AdvancedEffects effects)
        {
            MapShaderGlobals.ApplyAdvancedEffects(effects);
        }

        /// <summary>
        /// Apply fog of war visual settings to the material
        /// </summary>
        private void ApplyFogOfWarSettings(VisualStyleConfiguration.FogOfWarSettings fogOfWar)
        {
            MapShaderGlobals.ApplyFogOfWarStyle(fogOfWar);

            if (LogStyle)
            {
                ArchonLogger.Log($"VisualStyleManager: Fog of war {(fogOfWar.enabled ? "enabled" : "disabled")}", "map_rendering");
            }
        }

        /// <summary>
        /// Apply GAME custom shader parameters carried by the style (generic loop, no ENGINE code per parameter)
        /// </summary>
        private void ApplyCustomShaderParameters(VisualStyleConfiguration style)
        {
            if (style.customShaderParameters == null) return;

            foreach (var entry in style.customShaderParameters)
            {
                if (entry == null) continue;
                MapShaderParameterRegistry.Apply(entry.name, entry.ToValue(), entry.scope, runtimeMaterial);
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

                if (LogStyle)
                {
                    ArchonLogger.Log($"VisualStyleManager: Tessellation settings applied (height: {tessellation.heightScale}, factor: {tessellation.tessellationFactor}, range: {tessellation.tessellationMinDistance}-{tessellation.tessellationMaxDistance})", "map_rendering");
                }
            }
            else if (LogStyle)
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
        /// </summary>
        public void SwitchStyle(VisualStyleConfiguration newStyle)
        {
            if (newStyle == null)
            {
                ArchonLogger.LogError("VisualStyleManager: Cannot switch to null style", "map_rendering");
                return;
            }

            ApplyStyle(newStyle);

            // Apply border configuration
            if (borderDispatcher != null)
            {
                borderDispatcher.SetBorderRenderingMode(newStyle.borders.renderingMode);
                borderDispatcher.SetBorderMode(ConvertBorderMode(newStyle.borders.borderMode));
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

            // Apply border rendering mode
            if (borderDispatcher != null)
            {
                borderDispatcher.SetBorderRenderingMode(style.borders.renderingMode);
                borderDispatcher.SetBorderMode(ConvertBorderMode(style.borders.borderMode));
                borderDispatcher.SetPixelPerfectParameters(
                    style.borders.pixelPerfectCountryThickness,
                    style.borders.pixelPerfectProvinceThickness,
                    style.borders.pixelPerfectAntiAliasing
                );

                ArchonLogger.Log($"VisualStyleManager.ReloadMaterialFromAsset: Applied borders - Mode: {style.borders.renderingMode}", "map_rendering");
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
