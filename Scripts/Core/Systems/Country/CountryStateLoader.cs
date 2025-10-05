using Core.Data;
using Core.Loaders;

namespace Core.Systems.Country
{
    /// <summary>
    /// Handles loading and initializing country data
    /// Extracted from CountrySystem.cs for better separation of concerns
    /// </summary>
    public class CountryStateLoader
    {
        private readonly CountryDataManager dataManager;
        private readonly EventBus eventBus;
        private readonly int initialCapacity;

        public CountryStateLoader(CountryDataManager dataManager, EventBus eventBus, int initialCapacity)
        {
            this.dataManager = dataManager;
            this.eventBus = eventBus;
            this.initialCapacity = initialCapacity;
        }

        /// <summary>
        /// Initialize countries from JobifiedCountryLoader result
        /// </summary>
        public void InitializeFromCountryData(CountryDataLoadResult countryDataResult)
        {
            if (!countryDataResult.Success)
            {
                ArchonLogger.LogError($"Cannot initialize from failed country data: {countryDataResult.ErrorMessage}");
                return;
            }

            var countryData = countryDataResult.Countries;
            ArchonLogger.Log($"Initializing {countryData.Count} countries from data");

            // Clear existing data
            dataManager.Clear();

            // Add default "unowned" country at ID 0
            AddDefaultUnownedCountry();

            // Process each country from the loaded data
            ushort nextCountryId = 1; // Start from 1 (0 is reserved for unowned)

            for (int i = 0; i < countryData.Count; i++)
            {
                var country = countryData.GetCountryByIndex(i);
                if (country == null) continue;

                var tag = country.Tag;
                var hotData = country.hotData; // Use the hotData from Burst job (already has correct color!)
                var coldData = country.coldData;

                // Skip duplicates before assigning ID
                if (dataManager.HasTag(tag))
                {
                    // Don't increment nextCountryId - reuse this slot for next non-duplicate
                    if (i < 50)
                    {
                        ArchonLogger.Log($"CountrySystem: Skipping duplicate tag '{tag}' at index {i}");
                    }
                    continue;
                }

                // DEBUG: Log colors for first 5 countries
                if (nextCountryId < 5)
                {
                    var color = hotData.Color;
                    ArchonLogger.Log($"CountrySystem: Country index {i} tag={tag} → ID {nextCountryId}, hotData color R={color.r} G={color.g} B={color.b}");
                }

                // Add country to system
                dataManager.AddCountry(nextCountryId, tag, hotData, coldData);
                nextCountryId++;

                if (nextCountryId >= initialCapacity)
                {
                    ArchonLogger.LogWarning($"Country capacity exceeded: {nextCountryId}/{initialCapacity}");
                    break;
                }
            }

            ArchonLogger.Log($"CountrySystem initialized with {dataManager.CountryCount} countries");

            // Emit initialization complete event
            eventBus?.Emit(new CountrySystemInitializedEvent
            {
                CountryCount = dataManager.CountryCount
            });
        }

        /// <summary>
        /// Add the default "unowned" country at ID 0
        /// </summary>
        private void AddDefaultUnownedCountry()
        {
            var defaultHotData = new CountryHotData
            {
                tagHash = 0, // No tag
                graphicalCultureId = 0,
                flags = 0
            };
            defaultHotData.SetColor(UnityEngine.Color.gray);

            var defaultColdData = new CountryColdData
            {
                tag = "---",
                displayName = "Unowned",
                graphicalCulture = "western",
                // ... other default values
            };

            dataManager.AddCountry(0, "---", defaultHotData, defaultColdData);
        }
    }
}
