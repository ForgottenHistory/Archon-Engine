using Core;
using Core.Localization;
using Core.Systems;

namespace Map.Rendering.Labels
{
    /// <summary>
    /// What the map labels say, and which piece of a country is its homeland.
    /// Set on MapRendererRegistry.LabelSource (static, so GAME can set it before the map exists).
    /// Called on the main thread.
    /// </summary>
    public interface IMapLabelSource
    {
        /// <summary>Label text for a country (null/empty = no label).</summary>
        string GetLabelText(ushort countryId, CountrySystem countries);

        /// <summary>Capital province of a country (0 = unknown: the largest piece gets the main label).</summary>
        ushort GetCapitalProvince(ushort countryId, CountrySystem countries);

        /// <summary>Which country's label a province counts towards (default: its owner; a game may use the controller).</summary>
        ushort GetLabelHolder(ushort province, ushort owner, GameState gameState);
    }

    /// <summary>
    /// Region labels: names of groups of provinces the GAME defines (states, areas), drawn by a second
    /// MapLabelRenderer in region mode with the "regionLabels" style, while MapRegionLabels.Shown is on.
    /// Set on MapRendererRegistry.RegionLabelSource (null = no region labels). Regions don't change during
    /// a game: they are read once, when the map initializes. Called on the main thread.
    /// </summary>
    public interface IMapRegionLabelSource
    {
        /// <summary>The region a province belongs to (0 = none: no label covers it).</summary>
        ushort GetRegion(ushort province);

        /// <summary>Label text for a region (null/empty = no label).</summary>
        string GetRegionText(ushort region);
    }

    /// <summary>Whether region labels are drawn (GAME switches them on, e.g. while a map tool needs them).</summary>
    public static class MapRegionLabels
    {
        public static bool Shown { get; set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Shown = false;
    }

    /// <summary>
    /// ENGINE default: localized name keyed by the country tag (falls back to the display name,
    /// then the tag); no capital.
    /// </summary>
    public class DefaultMapLabelSource : IMapLabelSource
    {
        public virtual string GetLabelText(ushort countryId, CountrySystem countries)
        {
            if (countries == null) return null;
            string tag = countries.GetCountryTag(countryId);
            if (string.IsNullOrEmpty(tag)) return null;

            if (LocalizationManager.IsInitialized && LocalizationManager.HasKey(tag))
                return LocalizationManager.Get(tag);

            string display = countries.GetCountryColdData(countryId)?.displayName;
            return string.IsNullOrEmpty(display) ? tag : display;
        }

        public virtual ushort GetCapitalProvince(ushort countryId, CountrySystem countries) => 0;

        public virtual ushort GetLabelHolder(ushort province, ushort owner, GameState gameState) => owner;
    }
}
