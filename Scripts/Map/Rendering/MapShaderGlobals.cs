using UnityEngine;
using Archon.Engine.Map;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Engine-owned map shader values, uploaded as shader globals.
    ///
    /// The engine produces these (simulation textures, map mode state, visual style values)
    /// and every map shader just declares them — no Properties block entries, no per-material
    /// binding, no rebinding after a material swap. Any shader (map, border mesh, units) can read them.
    ///
    /// Constraint: a global is shadowed by a material property of the same name, so map shaders
    /// must NOT declare these names in their Properties block or UnityPerMaterial CBUFFER.
    ///
    /// Not globals (stay per-material): tessellation values (describe the shader's geometry and are
    /// read back from the material by selection/border code) and GAME tweakables.
    /// </summary>
    public static class MapShaderGlobals
    {
        // ===== Simulation / generated textures =====
        public static readonly int ProvinceIDTexture = Shader.PropertyToID("_ProvinceIDTexture");
        public static readonly int ProvinceOwnerTexture = Shader.PropertyToID("_ProvinceOwnerTexture");
        public static readonly int ProvinceColorTexture = Shader.PropertyToID("_ProvinceColorTexture");
        public static readonly int ProvinceDevelopmentTexture = Shader.PropertyToID("_ProvinceDevelopmentTexture");
        public static readonly int ProvinceTerrainTexture = Shader.PropertyToID("_ProvinceTerrainTexture");
        public static readonly int ProvinceCultureTexture = Shader.PropertyToID("_ProvinceCultureTexture");
        public static readonly int ProvinceReligionTexture = Shader.PropertyToID("_ProvinceReligionTexture");
        public static readonly int TerrainTypeTexture = Shader.PropertyToID("_TerrainTypeTexture");
        public static readonly int ProvinceTerrainLookup = Shader.PropertyToID("_ProvinceTerrainLookup");
        public static readonly int ProvinceTerrainBuffer = Shader.PropertyToID("_ProvinceTerrainBuffer");
        public static readonly int TerrainDetailArray = Shader.PropertyToID("_TerrainDetailArray");
        public static readonly int DetailNoiseTexture = Shader.PropertyToID("_DetailNoiseTexture");
        public static readonly int DetailIndexTexture = Shader.PropertyToID("_DetailIndexTexture");
        public static readonly int DetailMaskTexture = Shader.PropertyToID("_DetailMaskTexture");
        public static readonly int HeightmapTexture = Shader.PropertyToID("_HeightmapTexture");
        public static readonly int NormalMapTexture = Shader.PropertyToID("_NormalMapTexture");
        public static readonly int DistanceFieldBorderTexture = Shader.PropertyToID("_DistanceFieldBorderTexture");
        public static readonly int PixelPerfectBorderTexture = Shader.PropertyToID("_PixelPerfectBorderTexture");
        public static readonly int HighlightTexture = Shader.PropertyToID("_HighlightTexture");
        public static readonly int FogOfWarTexture = Shader.PropertyToID("_FogOfWarTexture");

        // ===== Palettes =====
        public static readonly int ProvinceColorPalette = Shader.PropertyToID("_ProvinceColorPalette");
        public static readonly int CountryColorPalette = Shader.PropertyToID("_CountryColorPalette");
        public static readonly int CultureColorPalette = Shader.PropertyToID("_CultureColorPalette");
        public static readonly int ReligionColorPalette = Shader.PropertyToID("_ReligionColorPalette");
        public static readonly int TerrainColorPalette = Shader.PropertyToID("_TerrainColorPalette");
        public static readonly int ProvincePaletteTexture = Shader.PropertyToID("_ProvincePaletteTexture");

        // ===== Map mode state =====
        public static readonly int MapMode = Shader.PropertyToID("_MapMode");
        public static readonly int CustomMapModeIndex = Shader.PropertyToID("_CustomMapModeIndex");
        public static readonly int MapModeTextureCount = Shader.PropertyToID("_MapModeTextureCount");
        public static readonly int MaxProvinceID = Shader.PropertyToID("_MaxProvinceID");

        // ===== Borders =====
        public static readonly int BorderRenderingModeId = Shader.PropertyToID("_BorderRenderingMode");
        public static readonly int CountryBorderStrength = Shader.PropertyToID("_CountryBorderStrength");
        public static readonly int CountryBorderColor = Shader.PropertyToID("_CountryBorderColor");
        public static readonly int ProvinceBorderStrength = Shader.PropertyToID("_ProvinceBorderStrength");
        public static readonly int ProvinceBorderColor = Shader.PropertyToID("_ProvinceBorderColor");
        public static readonly int EdgeWidth = Shader.PropertyToID("_EdgeWidth");
        public static readonly int GradientWidth = Shader.PropertyToID("_GradientWidth");
        public static readonly int EdgeSmoothness = Shader.PropertyToID("_EdgeSmoothness");
        public static readonly int EdgeColorMul = Shader.PropertyToID("_EdgeColorMul");
        public static readonly int GradientColorMul = Shader.PropertyToID("_GradientColorMul");
        public static readonly int EdgeAlpha = Shader.PropertyToID("_EdgeAlpha");
        public static readonly int GradientAlphaInside = Shader.PropertyToID("_GradientAlphaInside");
        public static readonly int GradientAlphaOutside = Shader.PropertyToID("_GradientAlphaOutside");

        // ===== Map colors, terrain, lighting =====
        public static readonly int OceanColor = Shader.PropertyToID("_OceanColor");
        public static readonly int UnownedLandColor = Shader.PropertyToID("_UnownedLandColor");
        public static readonly int TerrainBrightness = Shader.PropertyToID("_TerrainBrightness");
        public static readonly int TerrainSaturation = Shader.PropertyToID("_TerrainSaturation");
        public static readonly int DetailTiling = Shader.PropertyToID("_DetailTiling");
        public static readonly int DetailStrength = Shader.PropertyToID("_DetailStrength");
        public static readonly int NormalMapStrength = Shader.PropertyToID("_NormalMapStrength");
        public static readonly int NormalMapAmbient = Shader.PropertyToID("_NormalMapAmbient");
        public static readonly int NormalMapHighlight = Shader.PropertyToID("_NormalMapHighlight");

        // ===== Effects =====
        public static readonly int OverlayTexture = Shader.PropertyToID("_OverlayTexture");
        public static readonly int OverlayStrength = Shader.PropertyToID("_OverlayStrength");
        public static readonly int CountryColorSaturation = Shader.PropertyToID("_CountryColorSaturation");

        // ===== Fog of war =====
        public static readonly int FogOfWarEnabled = Shader.PropertyToID("_FogOfWarEnabled");
        public static readonly int FogOfWarZoomDisable = Shader.PropertyToID("_FogOfWarZoomDisable");
        public static readonly int FogUnexploredColor = Shader.PropertyToID("_FogUnexploredColor");
        public static readonly int FogExploredColor = Shader.PropertyToID("_FogExploredColor");
        public static readonly int FogExploredDesaturation = Shader.PropertyToID("_FogExploredDesaturation");
        public static readonly int FogNoiseColor = Shader.PropertyToID("_FogNoiseColor");
        public static readonly int FogNoiseScale = Shader.PropertyToID("_FogNoiseScale");
        public static readonly int FogNoiseStrength = Shader.PropertyToID("_FogNoiseStrength");
        public static readonly int FogNoiseSpeed = Shader.PropertyToID("_FogNoiseSpeed");

        /// <summary>
        /// Upload fallback values for every engine global. Replaces the defaults that used to live
        /// in each shader's Properties block. Call once before any texture/style binding.
        /// Scalar defaults come from VisualStyleConfiguration's own defaults (single source).
        /// </summary>
        public static void SetDefaults()
        {
            // Textures: same fallbacks the Properties blocks used
            Shader.SetGlobalTexture(ProvinceIDTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ProvinceOwnerTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ProvinceColorTexture, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(ProvinceDevelopmentTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ProvinceTerrainTexture, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(TerrainTypeTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ProvinceTerrainLookup, Texture2D.blackTexture);
            Shader.SetGlobalTexture(DetailNoiseTexture, Texture2D.grayTexture);
            Shader.SetGlobalTexture(DetailIndexTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(DetailMaskTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HeightmapTexture, Texture2D.grayTexture);
            Shader.SetGlobalTexture(NormalMapTexture, Texture2D.normalTexture);
            Shader.SetGlobalTexture(DistanceFieldBorderTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(PixelPerfectBorderTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HighlightTexture, Texture2D.blackTexture);
            Shader.SetGlobalTexture(FogOfWarTexture, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(ProvinceColorPalette, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(CountryColorPalette, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(TerrainColorPalette, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(ProvincePaletteTexture, Texture2D.blackTexture);
            // _TerrainDetailArray has no built-in fallback; unbound reads return zero (same as before)

            // Map mode state
            Shader.SetGlobalInteger(MapMode, 0);
            Shader.SetGlobalInteger(CustomMapModeIndex, 0);
            Shader.SetGlobalInteger(MapModeTextureCount, 0);
            Shader.SetGlobalInteger(MaxProvinceID, 65536);

            // Style values
            ApplyBorderStyle(new VisualStyleConfiguration.BorderStyle());
            ApplyMapModeColors(new VisualStyleConfiguration.MapModeColors());
            ApplyAdvancedEffects(new VisualStyleConfiguration.AdvancedEffects());
            ApplyFogOfWarStyle(new VisualStyleConfiguration.FogOfWarSettings());

            // Fog stays off until a player country is chosen (FogOfWarSystem enables it)
            Shader.SetGlobalFloat(FogOfWarEnabled, 0f);
            Shader.SetGlobalFloat(FogOfWarZoomDisable, 0f);
        }

        /// <summary>
        /// Shader border mode values: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
        /// </summary>
        public static int GetBorderShaderMode(BorderRenderingMode mode)
        {
            switch (mode)
            {
                case BorderRenderingMode.ShaderDistanceField: return 1;
                case BorderRenderingMode.ShaderPixelPerfect: return 2;
                case BorderRenderingMode.MeshGeometry: return 3;
                default: return 0;
            }
        }

        public static void SetBorderRenderingMode(int shaderMode)
        {
            Shader.SetGlobalInteger(BorderRenderingModeId, shaderMode);
        }

        public static void SetBorderColors(Color countryColor, float countryStrength, Color provinceColor, float provinceStrength)
        {
            Shader.SetGlobalColor(CountryBorderColor, countryColor);
            Shader.SetGlobalFloat(CountryBorderStrength, Mathf.Clamp01(countryStrength));
            Shader.SetGlobalColor(ProvinceBorderColor, provinceColor);
            Shader.SetGlobalFloat(ProvinceBorderStrength, Mathf.Clamp01(provinceStrength));
        }

        public static void SetDistanceFieldBorderParams(float edgeWidth, float gradientWidth, float edgeSmoothness,
            float edgeColorMul, float gradientColorMul, float edgeAlpha, float gradientAlphaInside, float gradientAlphaOutside)
        {
            Shader.SetGlobalFloat(EdgeWidth, edgeWidth);
            Shader.SetGlobalFloat(GradientWidth, gradientWidth);
            Shader.SetGlobalFloat(EdgeSmoothness, edgeSmoothness);
            Shader.SetGlobalFloat(EdgeColorMul, edgeColorMul);
            Shader.SetGlobalFloat(GradientColorMul, gradientColorMul);
            Shader.SetGlobalFloat(EdgeAlpha, edgeAlpha);
            Shader.SetGlobalFloat(GradientAlphaInside, gradientAlphaInside);
            Shader.SetGlobalFloat(GradientAlphaOutside, gradientAlphaOutside);
        }

        public static void ApplyBorderStyle(VisualStyleConfiguration.BorderStyle borders)
        {
            SetBorderRenderingMode(GetBorderShaderMode(borders.renderingMode));
            SetBorderColors(borders.countryBorderColor, borders.countryBorderStrength,
                borders.provinceBorderColor, borders.provinceBorderStrength);
            SetDistanceFieldBorderParams(borders.edgeWidth, borders.gradientWidth, borders.edgeSmoothness,
                borders.edgeColorMultiplier, borders.gradientColorMultiplier,
                borders.edgeAlpha, borders.gradientAlphaInside, borders.gradientAlphaOutside);
        }

        public static void ApplyMapModeColors(VisualStyleConfiguration.MapModeColors colors)
        {
            Shader.SetGlobalColor(OceanColor, colors.oceanColor);
            Shader.SetGlobalColor(UnownedLandColor, colors.unownedLandColor);
            SetTerrainParams(colors.terrainBrightness, colors.terrainSaturation, colors.detailTiling, colors.detailStrength);
            SetNormalMapLighting(colors.normalMapStrength, colors.normalMapAmbient, colors.normalMapHighlight);
        }

        public static void SetTerrainParams(float brightness, float saturation, float detailTiling, float detailStrength)
        {
            Shader.SetGlobalFloat(TerrainBrightness, brightness);
            Shader.SetGlobalFloat(TerrainSaturation, saturation);
            Shader.SetGlobalFloat(DetailTiling, detailTiling);
            Shader.SetGlobalFloat(DetailStrength, detailStrength);
        }

        public static void SetNormalMapLighting(float strength, float ambient, float highlight)
        {
            Shader.SetGlobalFloat(NormalMapStrength, strength);
            Shader.SetGlobalFloat(NormalMapAmbient, ambient);
            Shader.SetGlobalFloat(NormalMapHighlight, highlight);
        }

        public static void ApplyAdvancedEffects(VisualStyleConfiguration.AdvancedEffects effects)
        {
            // No overlay texture = white (multiplicative blend becomes a no-op)
            Shader.SetGlobalTexture(OverlayTexture, effects.overlayTexture != null ? effects.overlayTexture : Texture2D.whiteTexture);
            Shader.SetGlobalFloat(OverlayStrength, effects.overlayStrength);
            Shader.SetGlobalFloat(CountryColorSaturation, effects.countryColorSaturation);
        }

        public static void ApplyFogOfWarStyle(VisualStyleConfiguration.FogOfWarSettings fog)
        {
            Shader.SetGlobalFloat(FogOfWarEnabled, fog.enabled ? 1f : 0f);
            SetFogOfWarColors(fog.unexploredColor, fog.exploredTint, fog.exploredDesaturation,
                fog.noiseColor, fog.noiseScale, fog.noiseStrength, fog.noiseSpeed);
        }

        public static void SetFogOfWarColors(Color unexplored, Color exploredTint, float exploredDesaturation,
            Color noiseColor, float noiseScale, float noiseStrength, float noiseSpeed)
        {
            Shader.SetGlobalColor(FogUnexploredColor, unexplored);
            Shader.SetGlobalColor(FogExploredColor, exploredTint);
            Shader.SetGlobalFloat(FogExploredDesaturation, exploredDesaturation);
            Shader.SetGlobalColor(FogNoiseColor, noiseColor);
            Shader.SetGlobalFloat(FogNoiseScale, noiseScale);
            Shader.SetGlobalFloat(FogNoiseStrength, noiseStrength);
            Shader.SetGlobalFloat(FogNoiseSpeed, noiseSpeed);
        }

        public static void SetFogOfWarEnabled(bool enabled)
        {
            Shader.SetGlobalFloat(FogOfWarEnabled, enabled ? 1f : 0f);
        }

        public static void SetFogOfWarZoomDisable(float value)
        {
            Shader.SetGlobalFloat(FogOfWarZoomDisable, value);
        }

        public static void SetMapMode(int shaderModeId)
        {
            Shader.SetGlobalInteger(MapMode, shaderModeId);
        }
    }
}
