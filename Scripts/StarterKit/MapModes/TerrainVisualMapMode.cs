using UnityEngine;
using Core.Queries;
using Map.MapModes;
using Map.Rendering;

namespace StarterKit.MapModes
{
    /// <summary>
    /// STARTERKIT: Terrain Visual Map Mode
    ///
    /// Shows the actual terrain colors from terrain blend maps (grassland, mountain, desert, etc.)
    /// Uses the ENGINE's built-in RenderTerrain shader path (shader mode 1).
    /// No palette or gradient needed — the shader reads directly from blend map textures.
    ///
    /// This is distinct from TerrainCostMapMode which shows movement cost gradients.
    /// </summary>
    public class TerrainVisualMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Terrain;
        public override string Name => "Terrain";
        public override int ShaderModeID => 1;

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            DisableAllMapModeKeywords(mapMaterial);
            SetShaderMode(mapMaterial, ShaderModeID);
            LogActivation("Terrain visual mode - showing terrain blend maps");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }

        public override void UpdateTextures(MapModeDataTextures dataTextures,
            ProvinceQueries provinceQueries, CountryQueries countryQueries,
            ProvinceMapping provinceMapping, object gameProvinceSystem = null)
        {
            // Nothing to update — terrain blend maps are static, generated at load time
        }

        public override string GetProvinceTooltip(ushort provinceId,
            ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (provinceQueries.IsOcean(provinceId))
                return "Ocean";

            ushort terrainId = provinceQueries.GetTerrain(provinceId);
            ushort ownerId = provinceQueries.GetOwner(provinceId);
            string ownerName = ownerId == 0 ? "Unowned" : (countryQueries.GetTag(ownerId) ?? $"Country {ownerId}");

            return $"Province {provinceId}\nTerrain ID: {terrainId}\nOwner: {ownerName}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.Never;
        }
    }
}
