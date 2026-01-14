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
            ArchonLogger.Log("CrossReferenceBuilder initialized", "core_data_linking");
        }

        /// <summary>
        /// Build all cross-references after data loading is complete
        /// </summary>
        public void BuildAllCrossReferences()
        {
            ArchonLogger.Log("CrossReferenceBuilder: Building all cross-references", "core_data_linking");

            BuildCountryProvinceReferences();
            BuildProvinceNeighborReferences();
            ValidateCrossReferences();

            ArchonLogger.Log("CrossReferenceBuilder: All cross-references built successfully", "core_data_linking");
        }

        /// <summary>
        /// Build bidirectional references between countries and provinces
        /// Countries get lists of owned/controlled provinces
        /// </summary>
        public void BuildCountryProvinceReferences()
        {
            ArchonLogger.Log("CrossReferenceBuilder: Building country-province references", "core_data_linking");

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
                        ArchonLogger.LogWarning($"Province {province.DefinitionId} has invalid owner ID {province.OwnerId}", "core_data_linking");
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
                        ArchonLogger.LogWarning($"Province {province.DefinitionId} has invalid controller ID {province.ControllerId}", "core_data_linking");
                    }
                }
            }

            ArchonLogger.Log($"CrossReferenceBuilder: Built {ownedCount} ownership and {controlledCount} control relationships", "core_data_linking");
        }

        /// <summary>
        /// Build province neighbor references (placeholder for future implementation)
        /// </summary>
        public void BuildProvinceNeighborReferences()
        {
            ArchonLogger.Log("CrossReferenceBuilder: Building province neighbor references", "core_data_linking");

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

            ArchonLogger.Log("CrossReferenceBuilder: Province neighbor references initialized (bitmap analysis pending)", "core_data_linking");
        }

        /// <summary>
        /// Validate all cross-references for consistency
        /// </summary>
        public void ValidateCrossReferences()
        {
            ArchonLogger.Log("CrossReferenceBuilder: Validating cross-references", "core_data_linking");

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
                        ArchonLogger.LogError($"Country {country.Tag} claims to own invalid province {provinceId}", "core_data_linking");
                        country.OwnedProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                    else if (province.OwnerId != country.Id)
                    {
                        ArchonLogger.LogError($"Country {country.Tag} claims to own province {province.DefinitionId}, but province owner is {province.OwnerId}", "core_data_linking");
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
                        ArchonLogger.LogError($"Country {country.Tag} claims to control invalid province {provinceId}", "core_data_linking");
                        country.ControlledProvinces.Remove(provinceId);
                        validationErrors++;
                    }
                    else if (province.ControllerId != country.Id)
                    {
                        ArchonLogger.LogError($"Country {country.Tag} claims to control province {province.DefinitionId}, but province controller is {province.ControllerId}", "core_data_linking");
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
                        ArchonLogger.LogWarning($"Country {country.Tag} has invalid capital province {country.CapitalProvinceId}", "core_data_linking");
                        country.CapitalProvinceId = 0;
                        validationWarnings++;
                    }
                    else if (capital.OwnerId != country.Id)
                    {
                        ArchonLogger.LogWarning($"Country {country.Tag} capital {capital.DefinitionId} is not owned by the country (owner: {capital.OwnerId})", "core_data_linking");
                        validationWarnings++;
                    }
                }
            }

            if (validationErrors > 0 || validationWarnings > 0)
            {
                ArchonLogger.LogWarning($"CrossReferenceBuilder: Validation completed with {validationErrors} errors and {validationWarnings} warnings", "core_data_linking");
            }
            else
            {
                ArchonLogger.Log("CrossReferenceBuilder: Validation passed - all cross-references are consistent", "core_data_linking");
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