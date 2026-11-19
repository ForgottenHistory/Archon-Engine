using Core.Systems;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Updates border arc styles from province ownership.
    /// Geometry never changes here; only the per-arc style in BorderCurveCache.
    /// </summary>
    public class BorderStyleUpdater
    {
        private readonly BorderCurveCache curveCache;
        private readonly ProvinceSystemType provinceSystem;

        public BorderStyleUpdater(BorderCurveCache cache, ProvinceSystemType provinces, CountrySystem countries)
        {
            curveCache = cache;
            provinceSystem = provinces;
        }

        private ushort GetOwner(ushort provinceID) => provinceSystem.GetProvinceState(provinceID).ownerID;

        /// <summary>Reclassify all arcs from current ownership.</summary>
        public void UpdateAllBorderStyles()
        {
            if (curveCache == null || provinceSystem == null)
                return;

            curveCache.UpdateAllStyles(GetOwner);

            int country = 0, province = 0;
            foreach (var (_, style) in curveCache.GetAllBorderStyles())
            {
                if (style.type == BorderType.Country) country++;
                else if (style.type == BorderType.Province) province++;
            }
            ArchonLogger.Log($"BorderStyleUpdater: Classified arcs - Country: {country}, Province: {province}", "map_initialization");
        }

        /// <summary>Reclassify the arcs of a single province (when its ownership changes).</summary>
        public void UpdateProvinceBorderStyle(ushort provinceID)
        {
            if (curveCache == null || provinceSystem == null)
                return;
            curveCache.UpdateProvinceStyles(provinceID, GetOwner);
        }
    }
}
