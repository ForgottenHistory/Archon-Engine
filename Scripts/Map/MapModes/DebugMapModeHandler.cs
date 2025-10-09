using UnityEngine;
using Core.Queries;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Generic handler for debug visualization modes
    /// These modes don't need texture updates - they just set shader mode for visualization
    /// </summary>
    public class DebugMapModeHandler : BaseMapModeHandler
    {
        private readonly MapMode mode;
        private readonly string name;
        private readonly int shaderModeID;

        public override MapMode Mode => mode;
        public override string Name => name;
        public override int ShaderModeID => shaderModeID;

        public DebugMapModeHandler(MapMode debugMode, string displayName)
        {
            mode = debugMode;
            name = displayName;
            shaderModeID = (int)debugMode; // Debug modes use their enum value as shader mode ID
        }

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            SetShaderMode(mapMaterial, ShaderModeID);
            LogActivation($"{Name} debug mode activated (shader mode {ShaderModeID})");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            // No cleanup needed for debug modes
        }

        public override void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping, object gameProvinceSystem = null)
        {
            // Debug modes don't update textures - they just visualize existing data
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            return $"{Name} - Province {provinceId}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.Never; // Debug modes don't need updates
        }
    }
}
