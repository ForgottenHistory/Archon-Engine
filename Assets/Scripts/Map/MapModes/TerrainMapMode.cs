using UnityEngine;
using Unity.Collections;
using Core.Queries;
using System;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Terrain map mode handler - displays provinces using their original terrain colors
    /// Uses ProvinceColorTexture directly from provinces.bmp for authentic terrain visualization
    /// Performance: Static data, no runtime updates needed
    /// </summary>
    public class TerrainMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Terrain;
        public override string Name => "Terrain";
        public override int ShaderModeID => 1;

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            DisableAllMapModeKeywords(mapMaterial);
            EnableMapModeKeyword(mapMaterial, "MAP_MODE_TERRAIN");
            SetShaderMode(mapMaterial, ShaderModeID);

            LogActivation("Terrain map mode - showing original terrain colors");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }

        public override void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping)
        {
            // Terrain mode uses the static ProvinceColorTexture (from provinces.bmp)
            // No dynamic updates needed - terrain colors are static

            if (dataTextures?.ProvinceColorTexture == null)
            {
                DominionLogger.LogError("TerrainMapMode: Province color texture not available");
                return;
            }

            // Nothing to update - terrain colors are loaded once from provinces.bmp
            // The shader directly samples from ProvinceColorTexture
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (provinceQueries.IsOcean(provinceId))
            {
                return "Ocean";
            }

            var development = provinceQueries.GetDevelopment(provinceId);
            var owner = provinceQueries.GetOwner(provinceId);

            var ownerName = "Unowned";
            if (owner != 0)
            {
                ownerName = countryQueries.GetTag(owner);
            }

            return $"Province {provinceId}\nTerrain: Original\nOwner: {ownerName}\nDevelopment: {development}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.Never; // Terrain colors never change
        }
    }
}