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

        private const string Owner = "Archon";

        /// <summary>
        /// Register every ENGINE map shader parameter in MapShaderParameterRegistry (the contract).
        /// Global defaults replace the old Properties block defaults; style defaults come from
        /// VisualStyleConfiguration's own defaults (single source). Idempotent.
        /// </summary>
        public static void RegisterEngineParameters()
        {
            // Textures: same fallbacks the Properties blocks used
            E("_ProvinceIDTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_ProvinceOwnerTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_ProvinceColorTexture", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_ProvinceDevelopmentTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_ProvinceTerrainTexture", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_ProvinceCultureTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_ProvinceReligionTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_TerrainTypeTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_ProvinceTerrainLookup", ShaderValue.Tex(Texture2D.blackTexture));
            E("_TerrainDetailArray", ShaderValue.Tex(null)); // no built-in 2D array fallback; unbound reads return zero
            E("_DetailNoiseTexture", ShaderValue.Tex(Texture2D.grayTexture));
            E("_DetailIndexTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_DetailMaskTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_HeightmapTexture", ShaderValue.Tex(Texture2D.grayTexture));
            E("_NormalMapTexture", ShaderValue.Tex(Texture2D.normalTexture));
            E("_DistanceFieldBorderTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_PixelPerfectBorderTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_HighlightTexture", ShaderValue.Tex(Texture2D.blackTexture));
            E("_FogOfWarTexture", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_ProvinceColorPalette", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_CountryColorPalette", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_CultureColorPalette", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_ReligionColorPalette", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_TerrainColorPalette", ShaderValue.Tex(Texture2D.whiteTexture));
            E("_ProvincePaletteTexture", ShaderValue.Tex(Texture2D.blackTexture));

            // Map mode state
            E("_MapMode", ShaderValue.Int(0));
            E("_CustomMapModeIndex", ShaderValue.Int(0));
            E("_MapModeTextureCount", ShaderValue.Int(0));
            E("_MaxProvinceID", ShaderValue.Int(65536));

            // Borders
            var borders = new VisualStyleConfiguration.BorderStyle();
            E("_BorderRenderingMode", ShaderValue.Int(GetBorderShaderMode(borders.renderingMode)));
            G("_CountryBorderColor", ShaderValue.Color(borders.countryBorderColor));
            G("_CountryBorderStrength", ShaderValue.Float(borders.countryBorderStrength));
            G("_ProvinceBorderColor", ShaderValue.Color(borders.provinceBorderColor));
            G("_ProvinceBorderStrength", ShaderValue.Float(borders.provinceBorderStrength));
            G("_EdgeWidth", ShaderValue.Float(borders.edgeWidth));
            G("_GradientWidth", ShaderValue.Float(borders.gradientWidth));
            G("_EdgeSmoothness", ShaderValue.Float(borders.edgeSmoothness));
            G("_EdgeColorMul", ShaderValue.Float(borders.edgeColorMultiplier));
            G("_GradientColorMul", ShaderValue.Float(borders.gradientColorMultiplier));
            G("_EdgeAlpha", ShaderValue.Float(borders.edgeAlpha));
            G("_GradientAlphaInside", ShaderValue.Float(borders.gradientAlphaInside));
            G("_GradientAlphaOutside", ShaderValue.Float(borders.gradientAlphaOutside));

            // Map colors, terrain, lighting
            var colors = new VisualStyleConfiguration.MapModeColors();
            G("_OceanColor", ShaderValue.Color(colors.oceanColor));
            G("_UnownedLandColor", ShaderValue.Color(colors.unownedLandColor));
            G("_TerrainBrightness", ShaderValue.Float(colors.terrainBrightness));
            G("_TerrainSaturation", ShaderValue.Float(colors.terrainSaturation));
            G("_DetailTiling", ShaderValue.Float(colors.detailTiling));
            G("_DetailStrength", ShaderValue.Float(colors.detailStrength));
            G("_NormalMapStrength", ShaderValue.Float(colors.normalMapStrength));
            G("_NormalMapAmbient", ShaderValue.Float(colors.normalMapAmbient));
            G("_NormalMapHighlight", ShaderValue.Float(colors.normalMapHighlight));

            // Effects (no overlay texture = white, multiplicative blend becomes a no-op)
            var effects = new VisualStyleConfiguration.AdvancedEffects();
            G("_OverlayTexture", ShaderValue.Tex(Texture2D.whiteTexture));
            G("_OverlayStrength", ShaderValue.Float(effects.overlayStrength));
            G("_CountryColorSaturation", ShaderValue.Float(effects.countryColorSaturation));

            // Fog of war (off until a player country is chosen; FogOfWarSystem enables it)
            var fog = new VisualStyleConfiguration.FogOfWarSettings();
            E("_FogOfWarEnabled", ShaderValue.Float(0f));
            E("_FogOfWarZoomDisable", ShaderValue.Float(0f));
            G("_FogUnexploredColor", ShaderValue.Color(fog.unexploredColor));
            G("_FogExploredColor", ShaderValue.Color(fog.exploredTint));
            G("_FogExploredDesaturation", ShaderValue.Float(fog.exploredDesaturation));
            G("_FogNoiseColor", ShaderValue.Color(fog.noiseColor));
            G("_FogNoiseScale", ShaderValue.Float(fog.noiseScale));
            G("_FogNoiseStrength", ShaderValue.Float(fog.noiseStrength));
            G("_FogNoiseSpeed", ShaderValue.Float(fog.noiseSpeed));

            // Material parameters read by ENGINE shader code in every map pass
            M("_TriPlanarTightenFactor", ShaderParameterType.Float, required: true);
            M("_HighlightStrength", ShaderParameterType.Float, required: true);

            // Tessellation: terrain pass only (flat shaders legitimately omit it)
            M("_HeightScale", ShaderParameterType.Float, required: false);
            M("_TessellationFactor", ShaderParameterType.Float, required: false);
            M("_TessellationMinDistance", ShaderParameterType.Float, required: false);
            M("_TessellationMaxDistance", ShaderParameterType.Float, required: false);
        }

        // Style-settable global (visual styles / style data files may set it)
        private static void G(string name, ShaderValue defaultValue) =>
            MapShaderParameterRegistry.RegisterGlobal(name, defaultValue, Owner);

        // Engine-managed global (simulation data / runtime state; styles may not set it)
        private static void E(string name, ShaderValue defaultValue) =>
            MapShaderParameterRegistry.RegisterGlobal(name, defaultValue, Owner, styleSettable: false);

        private static void M(string name, ShaderParameterType type, bool required) =>
            MapShaderParameterRegistry.RegisterMaterial(name, type, required, Owner);

        /// <summary>
        /// Register ENGINE parameters and upload every global default. Call once before any
        /// texture/style binding (MapTextureManager.Initialize).
        /// </summary>
        public static void SetDefaults()
        {
            RegisterEngineParameters();
            MapShaderParameterRegistry.ApplyGlobalDefaults();
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
