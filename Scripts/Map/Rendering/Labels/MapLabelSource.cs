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
    }
}
