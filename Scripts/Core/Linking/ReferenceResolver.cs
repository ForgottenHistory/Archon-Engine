using System;
using System.Collections.Generic;
using Core.Data;
using Core.Data.Ids;
using Core.Loaders;
using Core.Registries;
using Utils;
using CountryData = Core.Registries.CountryData;
using ProvinceData = Core.Registries.ProvinceData;

namespace Core.Linking
{
    /// <summary>
    /// Converts string references to numeric IDs during data loading
    /// Central component of the data linking architecture
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class ReferenceResolver
    {
        private readonly GameRegistries registries;
        private readonly List<Action> deferredResolutions = new();
        private readonly List<string> errors = new();
        private readonly List<string> warnings = new();

        public ReferenceResolver(GameRegistries registries)
        {
            this.registries = registries ?? throw new ArgumentNullException(nameof(registries));
            ArchonLogger.Log("ReferenceResolver initialized", "core_data_linking");
        }

        /// <summary>
        /// Resolve all string references in province data to numeric IDs
        /// </summary>
        public void ResolveProvinceReferences(ref ProvinceInitialState rawData, ProvinceData provinceData)
        {
            var context = $"Province {rawData.ProvinceID}";

            // Resolve country references
            provinceData.OwnerId = ResolveCountryRef(rawData.OwnerTag.ToString(), $"{context} owner");
            provinceData.ControllerId = ResolveCountryRef(rawData.ControllerTag.ToString() ?? rawData.OwnerTag.ToString(), $"{context} controller");

            // Resolve static data references
            provinceData.CultureId = ResolveCultureRef(rawData.Culture.ToString(), $"{context} culture");
            provinceData.ReligionId = ResolveReligionRef(rawData.Religion.ToString(), $"{context} religion");
            provinceData.TradeGoodId = ResolveTradeGoodRef(rawData.TradeGood.ToString(), $"{context} trade good");

            // Copy other data
            provinceData.Name = $"Province {rawData.ProvinceID}"; // TODO: Load actual names
            provinceData.Development = rawData.Development;

            // Resolve terrain type using water province definitions (Phase 3: Linking)
            provinceData.Terrain = WaterProvinceLoader.GetTerrainTypeForProvince(rawData.ProvinceID);

            provinceData.Flags = rawData.Flags;
            provinceData.BaseTax = rawData.BaseTax;
            provinceData.BaseProduction = rawData.BaseProduction;
            provinceData.BaseManpower = rawData.BaseManpower;
            provinceData.CenterOfTrade = rawData.CenterOfTrade;

            // CRITICAL FIX: Copy linked IDs back to ProvinceInitialState for simulation layer
            rawData.OwnerID = (ushort)provinceData.OwnerId;
            rawData.ControllerID = (ushort)provinceData.ControllerId;
            rawData.Terrain = provinceData.Terrain;

            ArchonLogger.Log($"Resolved references for {context}: Owner={provinceData.OwnerId}, Culture={provinceData.CultureId}, Religion={provinceData.ReligionId}", "core_data_linking");
        }

        /// <summary>
        /// Resolve all string references in country data to numeric IDs
        /// </summary>
        public void ResolveCountryReferences(CountryData countryData, Dictionary<string, object> rawCountryData)
        {
            var context = $"Country {countryData.Tag}";

            // Resolve static data references if they exist in raw data
            if (rawCountryData.TryGetValue("primary_culture", out var primaryCulture))
            {
                countryData.PrimaryCultureId = ResolveCultureRef(primaryCulture.ToString(), $"{context} primary culture");
            }

            if (rawCountryData.TryGetValue("religion", out var religion))
            {
                countryData.ReligionId = ResolveReligionRef(religion.ToString(), $"{context} religion");
            }

            if (rawCountryData.TryGetValue("government", out var government))
            {
                countryData.GovernmentId = ResolveGovernmentRef(government.ToString(), $"{context} government");
            }

            if (rawCountryData.TryGetValue("technology_group", out var techGroup))
            {
                countryData.TechnologyGroupId = ResolveTechnologyGroupRef(techGroup.ToString(), $"{context} technology group");
            }

            if (rawCountryData.TryGetValue("capital", out var capital))
            {
                if (int.TryParse(capital.ToString(), out int capitalId))
                {
                    countryData.CapitalProvinceId = registries.Provinces.GetRuntimeId(capitalId);
                    if (countryData.CapitalProvinceId == 0)
                    {
                        AddWarning($"{context}: Capital province {capitalId} not found");
                    }
                }
            }

            ArchonLogger.Log($"Resolved references for {context}: Culture={countryData.PrimaryCultureId}, Religion={countryData.ReligionId}", "core_data_linking");
        }

        /// <summary>
        /// Resolve country reference by tag
        /// </summary>
        public CountryId ResolveCountryRef(string tag, string context)
        {
            if (string.IsNullOrEmpty(tag) || tag == "---" || tag == "none")
                return CountryId.None;

            if (registries.Countries.TryGetId(tag, out ushort id))
                return new CountryId(id);

            AddError($"{context}: Unknown country '{tag}'");
            return CountryId.None;
        }

        /// <summary>
        /// Resolve culture reference by name
        /// </summary>
        public CultureId ResolveCultureRef(string cultureName, string context)
        {
            if (string.IsNullOrEmpty(cultureName))
                return CultureId.None;

            if (registries.Cultures.TryGetId(cultureName, out ushort id))
                return new CultureId(id);

            AddWarning($"{context}: Unknown culture '{cultureName}', using default");
            return CultureId.None;
        }

        /// <summary>
        /// Resolve religion reference by name
        /// </summary>
        public ReligionId ResolveReligionRef(string religionName, string context)
        {
            if (string.IsNullOrEmpty(religionName))
                return ReligionId.None;

            if (registries.Religions.TryGetId(religionName, out ushort id))
                return new ReligionId(id);

            AddWarning($"{context}: Unknown religion '{religionName}', using default");
            return ReligionId.None;
        }

        /// <summary>
        /// Resolve trade good reference by name
        /// </summary>
        public TradeGoodId ResolveTradeGoodRef(string tradeGoodName, string context)
        {
            if (string.IsNullOrEmpty(tradeGoodName))
                return TradeGoodId.None;

            if (registries.TradeGoods.TryGetId(tradeGoodName, out ushort id))
                return new TradeGoodId(id);

            AddWarning($"{context}: Unknown trade good '{tradeGoodName}', using default");
            return TradeGoodId.None;
        }

        /// <summary>
        /// Resolve government reference by name (placeholder implementation)
        /// </summary>
        public ushort ResolveGovernmentRef(string governmentName, string context)
        {
            if (string.IsNullOrEmpty(governmentName))
                return 0;

            if (registries.Governments.TryGetId(governmentName, out ushort id))
                return id;

            AddWarning($"{context}: Unknown government '{governmentName}', using default");
            return 0;
        }

        /// <summary>
        /// Resolve technology group reference by name (placeholder implementation)
        /// </summary>
        public ushort ResolveTechnologyGroupRef(string techGroupName, string context)
        {
            if (string.IsNullOrEmpty(techGroupName))
                return 0;

            if (registries.Technologies.TryGetId(techGroupName, out ushort id))
                return id;

            AddWarning($"{context}: Unknown technology group '{techGroupName}', using default");
            return 0;
        }

        /// <summary>
        /// Defer a resolution action until all entities are loaded
        /// Used for cross-references that depend on other entities being available
        /// </summary>
        public void DeferResolution(Action resolution)
        {
            deferredResolutions.Add(resolution);
        }

        /// <summary>
        /// Execute all deferred resolution actions
        /// Call this after all basic data is loaded
        /// </summary>
        public void ResolveDeferredReferences()
        {
            ArchonLogger.Log($"ReferenceResolver: Executing {deferredResolutions.Count} deferred resolutions", "core_data_linking");

            foreach (var resolution in deferredResolutions)
            {
                try
                {
                    resolution();
                }
                catch (Exception e)
                {
                    AddError($"Deferred resolution failed: {e.Message}");
                }
            }

            deferredResolutions.Clear();
            ArchonLogger.Log("ReferenceResolver: Deferred resolutions complete", "core_data_linking");
        }

        /// <summary>
        /// Check if there were any errors during resolution
        /// </summary>
        public bool HasErrors() => errors.Count > 0;

        /// <summary>
        /// Check if there were any warnings during resolution
        /// </summary>
        public bool HasWarnings() => warnings.Count > 0;

        /// <summary>
        /// Get all errors that occurred during resolution
        /// </summary>
        public IReadOnlyList<string> GetErrors() => errors.AsReadOnly();

        /// <summary>
        /// Get all warnings that occurred during resolution
        /// </summary>
        public IReadOnlyList<string> GetWarnings() => warnings.AsReadOnly();

        /// <summary>
        /// Add an error message
        /// </summary>
        private void AddError(string message)
        {
            errors.Add(message);
            ArchonLogger.LogError($"Reference Resolution Error: {message}", "core_data_linking");
        }

        /// <summary>
        /// Add a warning message
        /// </summary>
        private void AddWarning(string message)
        {
            warnings.Add(message);
            ArchonLogger.LogWarning($"Reference Resolution Warning: {message}", "core_data_linking");
        }

        /// <summary>
        /// Get summary of resolution results
        /// </summary>
        public string GetResolutionSummary()
        {
            return $"Reference Resolution Summary: {errors.Count} errors, {warnings.Count} warnings, {deferredResolutions.Count} deferred actions remaining";
        }
    }
}