using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Registries;
using Utils;
using CountryData = Core.Registries.CountryData;
using ProvinceData = Core.Registries.ProvinceData;

namespace Core.Linking
{
    /// <summary>
    /// Builds bidirectional references between game entities
    /// Creates efficient lookup structures for runtime queries
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class CrossReferenceBuilder
    {
        private readonly GameRegistries registries;

        public CrossReferenceBuilder(GameRegistries registries)
        {
            this.registries = registries ?? throw new System.ArgumentNullException(nameof(registries));
            DominionLogger.LogDataLinking("CrossReferenceBuilder initialized");
        }

        /// <summary>
        /// Build all cross-references after data loading is complete
        /// </summary>
        public void BuildAllCrossReferences()
        {
            DominionLogger.LogDataLinking("CrossReferenceBuilder: Building all cross-references");

            BuildCountryProvinceReferences();
            BuildCultureGroupReferences();
            BuildProvinceNeighborReferences();
            ValidateCrossReferences();

            DominionLogger.LogDataLinking("CrossReferenceBuilder: All cross-references built successfully");
        }

        /// <summary>
        /// Build bidirectional references between countries and provinces
        /// Countries get lists of owned/controlled provinces
        /// </summary>
        public void BuildCountryProvinceReferences()
        {
            DominionLogger.LogDataLinking("CrossReferenceBuilder: Building country-province references");

            // Clear existing references
            foreach (var country in registries.Countries.GetAll())
            {
                country.OwnedProvinces.Clear();
                country.ControlledProvinces.Clear();
            }

            int ownedCount = 0;
            int controlledCount = 0;

            // Build province lists for countries
            foreach (var province in registries.Provinces.GetAll())
            {
                // Add to owner's province list
                if (province.OwnerId != 0)
                {
                    var owner = registries.Countries.Get(province.OwnerId);
                    if (owner != null)
                    {
                        owner.OwnedProvinces.Add(province.RuntimeId);
                        ownedCount++;
                    }
                    else
                    {
                        DominionLogger.LogDataLinkingWarning($"Province {province.DefinitionId} has invalid owner ID {province.OwnerId}");
                    }
                }

                // Add to controller's province list (if different from owner)
                if (province.ControllerId != 0 && province.ControllerId != province.OwnerId)
                {
                    var controller = registries.Countries.Get(province.ControllerId);
                    if (controller != null)
                    {
                        controller.ControlledProvinces.Add(province.RuntimeId);
                        controlledCount++;
                    }
                    else
                    {
                        DominionLogger.LogDataLinkingWarning($"Province {province.DefinitionId} has invalid controller ID {province.ControllerId}");
                    }
                }
            }

            DominionLogger.LogDataLinking($"CrossReferenceBuilder: Built {ownedCount} ownership and {controlledCount} control relationships");
        }

        /// <summary>
        /// Build culture group references (placeholder for future implementation)
        /// </summary>
        public void BuildCultureGroupReferences()
        {
            DominionLogger.LogDataLinking("CrossReferenceBuilder: Building culture group references");

            // TODO: Implement when culture groups are added
            // This would group cultures by their culture groups for efficient queries

            var culturesByGroup = new Dictionary<string, List<ushort>>();

            foreach (var culture in registries.Cultures.GetAll())
            {
                var cultureData = culture as CultureData;
                if (cultureData?.CultureGroup != null)
                {
                    if (!culturesByGroup.ContainsKey(cultureData.CultureGroup))
                    {
                        culturesByGroup[cultureData.CultureGroup] = new List<ushort>();
                    }

                    // Find the culture ID
                    foreach (var cultureId in registries.Cultures.GetAllIds())
                    {
                        if (registries.Cultures.Get(cultureId) == culture)
                        {
                            culturesByGroup[cultureData.CultureGroup].Add(cultureId);
                            break;
                        }
                    }
                }
            }

            DominionLogger.LogDataLinking($"CrossReferenceBuilder: Grouped {registries.Cultures.Count} cultures into {culturesByGroup.Count} culture groups");
        }

        /// <summary>
        /// Build province neighbor references (placeholder for future implementation)
        /// </summary>
        public void BuildProvinceNeighborReferences()
        {
            DominionLogger.LogDataLinking("CrossReferenceBuilder: Building province neighbor references");

            // TODO: Implement when adjacency data is available
            // This would analyze the province bitmap to determine neighboring provinces
            // For now, just initialize empty neighbor lists

            foreach (var province in registries.Provinces.GetAll())
            {
                province.NeighborProvinces.Clear();
                // In a full implementation, this would:
                // 1. Analyze the province bitmap around each province
                // 2. Find adjacent provinces by checking neighboring pixels
                // 3. Build the bidirectional neighbor relationships
            }

            DominionLogger.LogDataLinking("CrossReferenceBuilder: Province neighbor references initialized (bitmap analysis pending)");
        }

        /// <summary>
        /// Validate all cross-references for consistency
        /// </summary>
        public void ValidateCrossReferences()
        {
            DominionLogger.LogDataLinking("CrossReferenceBuilder: Validating cross-references");

            int validationErrors = 0;
            int validationWarnings = 0;

            // Validate country-province relationships
            foreach (var country in registries.Countries.GetAll())
            {
                // Check that all owned provinces are valid
                foreach (var provinceId in country.OwnedProvinces.ToList())
                {
                    var province = registries.Provinces.GetByRuntime(provinceId);
                    if (province == null)
                    {
                        DominionLogger.LogError($"Country {country.Tag} claims to own invalid province {provinceId}");
                        country.OwnedProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                    else if (province.OwnerId != country.Id)
                    {
                        DominionLogger.LogError($"Country {country.Tag} claims to own province {province.DefinitionId}, but province owner is {province.OwnerId}");
                        country.OwnedProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                }

                // Check that all controlled provinces are valid
                foreach (var provinceId in country.ControlledProvinces.ToList())
                {
                    var province = registries.Provinces.GetByRuntime(provinceId);
                    if (province == null)
                    {
                        DominionLogger.LogError($"Country {country.Tag} claims to control invalid province {provinceId}");
                        country.ControlledProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                    else if (province.ControllerId != country.Id)
                    {
                        DominionLogger.LogError($"Country {country.Tag} claims to control province {province.DefinitionId}, but province controller is {province.ControllerId}");
                        country.ControlledProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                }

                // Check capital province
                if (country.CapitalProvinceId != 0)
                {
                    var capital = registries.Provinces.GetByRuntime(country.CapitalProvinceId);
                    if (capital == null)
                    {
                        DominionLogger.LogDataLinkingWarning($"Country {country.Tag} has invalid capital province {country.CapitalProvinceId}");
                        country.CapitalProvinceId = 0;
                        validationWarnings++;
                    }
                    else if (capital.OwnerId != country.Id)
                    {
                        DominionLogger.LogDataLinkingWarning($"Country {country.Tag} capital {capital.DefinitionId} is not owned by the country (owner: {capital.OwnerId})");
                        validationWarnings++;
                    }
                }
            }

            if (validationErrors > 0 || validationWarnings > 0)
            {
                DominionLogger.LogDataLinkingWarning($"CrossReferenceBuilder: Validation completed with {validationErrors} errors and {validationWarnings} warnings");
            }
            else
            {
                DominionLogger.LogDataLinking("CrossReferenceBuilder: Validation passed - all cross-references are consistent");
            }
        }

        /// <summary>
        /// Get statistics about the built cross-references
        /// </summary>
        public string GetCrossReferenceStatistics()
        {
            var totalOwnedProvinces = registries.Countries.GetAll().Sum(c => c.OwnedProvinces.Count);
            var totalControlledProvinces = registries.Countries.GetAll().Sum(c => c.ControlledProvinces.Count);
            var countriesWithCapitals = registries.Countries.GetAll().Count(c => c.CapitalProvinceId != 0);

            return $"Cross-Reference Statistics:\n" +
                   $"  Total owned provinces: {totalOwnedProvinces}\n" +
                   $"  Total controlled provinces: {totalControlledProvinces}\n" +
                   $"  Countries with capitals: {countriesWithCapitals}/{registries.Countries.Count}\n" +
                   $"  Provinces: {registries.Provinces.Count}\n" +
                   $"  Countries: {registries.Countries.Count}";
        }
    }
}