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
    /// Validates data integrity after all references are resolved
    /// Ensures the game data is consistent and playable
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class DataValidator
    {
        private readonly GameRegistries registries;
        private readonly List<ValidationError> errors = new();
        private readonly List<ValidationError> warnings = new();

        public DataValidator(GameRegistries registries)
        {
            this.registries = registries ?? throw new System.ArgumentNullException(nameof(registries));
            ArchonLogger.Log("DataValidator initialized", "core_data_linking");
        }

        /// <summary>
        /// Validate all game data for consistency and playability
        /// </summary>
        public bool ValidateGameData()
        {
            ArchonLogger.Log("DataValidator: Starting comprehensive game data validation", "core_data_linking");

            ClearValidationResults();

            ValidateStaticData();
            ValidateCountries();
            ValidateProvinces();
            ValidateGameplayRequirements();

            LogValidationResults();

            bool isValid = errors.Count == 0;
            ArchonLogger.Log($"DataValidator: Validation {(isValid ? "PASSED" : "FAILED")} - {errors.Count} errors, {warnings.Count} warnings", "core_data_linking");

            return isValid;
        }

        /// <summary>
        /// Validate static data registries
        /// </summary>
        private void ValidateStaticData()
        {
            // Check that essential static data is loaded
            if (registries.Religions.Count == 0)
            {
                AddError("No religions loaded", "Static Data");
            }

            if (registries.Cultures.Count == 0)
            {
                AddError("No cultures loaded", "Static Data");
            }

            if (registries.TradeGoods.Count == 0)
            {
                AddError("No trade goods loaded", "Static Data");
            }

            if (registries.Terrains.Count == 0)
            {
                AddError("No terrain types loaded", "Static Data");
            }

            ArchonLogger.Log($"DataValidator: Static data validation complete - {registries.Religions.Count} religions, {registries.Cultures.Count} cultures, {registries.TradeGoods.Count} trade goods", "core_data_linking");
        }

        /// <summary>
        /// Validate country data integrity
        /// </summary>
        private void ValidateCountries()
        {
            if (registries.Countries.Count == 0)
            {
                AddError("No countries loaded", "Countries");
                return;
            }

            foreach (var country in registries.Countries.GetAll())
            {
                var context = $"Country {country.Tag}";

                // Validate basic data
                if (string.IsNullOrEmpty(country.Tag))
                {
                    AddError("Country has no tag", context);
                }
                else if (country.Tag.Length != 3)
                {
                    AddError($"Country tag '{country.Tag}' is not 3 characters", context);
                }

                // Validate references
                if (country.PrimaryCultureId != 0 && !registries.Cultures.Exists(country.PrimaryCultureId))
                {
                    AddError($"Invalid primary culture ID {country.PrimaryCultureId}", context);
                }

                if (country.ReligionId != 0 && !registries.Religions.Exists(country.ReligionId))
                {
                    AddError($"Invalid religion ID {country.ReligionId}", context);
                }

                // Validate capital
                if (country.CapitalProvinceId != 0)
                {
                    var capital = registries.Provinces.GetByRuntime(country.CapitalProvinceId);
                    if (capital == null)
                    {
                        AddError($"Capital province {country.CapitalProvinceId} does not exist", context);
                    }
                    else if (capital.OwnerId != country.Id)
                    {
                        AddError($"Capital province {capital.DefinitionId} is not owned by the country", context);
                    }
                }

                foreach (var provinceId in country.OwnedProvinces)
                {
                    var province = registries.Provinces.GetByRuntime(provinceId);
                    if (province == null)
                    {
                        AddError($"Owned province {provinceId} does not exist", context);
                    }
                    else if (province.OwnerId != country.Id)
                    {
                        AddError($"Claimed owned province {province.DefinitionId} has different owner {province.OwnerId}", context);
                    }
                }
            }

            ArchonLogger.Log($"DataValidator: Country validation complete - {registries.Countries.Count} countries validated", "core_data_linking");
        }

        /// <summary>
        /// Validate province data integrity
        /// </summary>
        private void ValidateProvinces()
        {
            if (registries.Provinces.Count == 0)
            {
                AddError("No provinces loaded", "Provinces");
                return;
            }

            foreach (var province in registries.Provinces.GetAll())
            {
                var context = $"Province {province.DefinitionId}";

                // Validate owner
                if (province.OwnerId != 0)
                {
                    var owner = registries.Countries.Get(province.OwnerId);
                    if (owner == null)
                    {
                        AddError($"Invalid owner ID {province.OwnerId}", context);
                    }
                }

                // Validate controller
                if (province.ControllerId != 0)
                {
                    var controller = registries.Countries.Get(province.ControllerId);
                    if (controller == null)
                    {
                        AddError($"Invalid controller ID {province.ControllerId}", context);
                    }

                    // Controller different from owner implies occupation/war
                    if (province.ControllerId != province.OwnerId && province.OwnerId != 0)
                    {
                        // This could be valid in wartime scenarios
                        AddWarning($"Province controlled by {province.ControllerId} but owned by {province.OwnerId}", context);
                    }
                }

                // Validate static references
                if (province.CultureId != 0 && !registries.Cultures.Exists(province.CultureId))
                {
                    AddError($"Invalid culture ID {province.CultureId}", context);
                }

                if (province.ReligionId != 0 && !registries.Religions.Exists(province.ReligionId))
                {
                    AddError($"Invalid religion ID {province.ReligionId}", context);
                }

                if (province.TradeGoodId != 0 && !registries.TradeGoods.Exists(province.TradeGoodId))
                {
                    AddError($"Invalid trade good ID {province.TradeGoodId}", context);
                }

                // Validate terrain
                if (province.Terrain == 0) // Ocean
                {
                    if (province.OwnerId != 0)
                    {
                        AddWarning("Ocean province has owner", context);
                    }
                    if (province.Development > 0)
                    {
                        AddWarning("Ocean province has development", context);
                    }
                }

                // Validate development
                if (province.Development == 0 && province.Terrain != 0)
                {
                    AddWarning("Land province has no development", context);
                }

                // Validate buildings
                foreach (var buildingId in province.Buildings)
                {
                    if (buildingId != 0 && !registries.Buildings.Exists(buildingId))
                    {
                        AddError($"Invalid building ID {buildingId}", context);
                    }
                }
            }

            ArchonLogger.Log($"DataValidator: Province validation complete - {registries.Provinces.Count} provinces validated", "core_data_linking");
        }

        /// <summary>
        /// Validate gameplay requirements
        /// </summary>
        private void ValidateGameplayRequirements()
        {
            // Check that there are playable countries
            var playableCountries = registries.Countries.GetAll().Where(c => c.OwnedProvinces.Count > 0).Count();
            if (playableCountries == 0)
            {
                AddError("No playable countries found (countries with owned provinces)", "Gameplay");
            }
            else if (playableCountries < 5)
            {
                AddWarning($"Only {playableCountries} playable countries found", "Gameplay");
            }

            // Check that there are land provinces
            var landProvinces = registries.Provinces.GetAll().Where(p => p.Terrain != 0).Count();
            if (landProvinces == 0)
            {
                AddError("No land provinces found", "Gameplay");
            }

            // Check that there are owned provinces
            var ownedProvinces = registries.Provinces.GetAll().Where(p => p.OwnerId != 0).Count();
            if (ownedProvinces == 0)
            {
                AddError("No provinces are owned by any country", "Gameplay");
            }

            ArchonLogger.Log($"DataValidator: Gameplay validation complete - {playableCountries} playable countries, {landProvinces} land provinces, {ownedProvinces} owned provinces", "core_data_linking");
        }

        /// <summary>
        /// Add a validation error
        /// </summary>
        private void AddError(string message, string category)
        {
            errors.Add(new ValidationError { Message = message, Category = category, IsError = true });
        }

        /// <summary>
        /// Add a validation warning
        /// </summary>
        private void AddWarning(string message, string category)
        {
            warnings.Add(new ValidationError { Message = message, Category = category, IsError = false });
        }

        /// <summary>
        /// Clear validation results
        /// </summary>
        private void ClearValidationResults()
        {
            errors.Clear();
            warnings.Clear();
        }

        /// <summary>
        /// Log all validation results
        /// </summary>
        private void LogValidationResults()
        {
            if (errors.Count > 0)
            {
                ArchonLogger.LogError($"DataValidator: {errors.Count} validation errors found:", "core_data_linking");
                foreach (var error in errors)
                {
                    ArchonLogger.LogError($"  [{error.Category}] {error.Message}", "core_data_linking");
                }
            }

            if (warnings.Count > 0)
            {
                ArchonLogger.LogWarning($"DataValidator: {warnings.Count} validation warnings found:", "core_data_linking");
            }
        }

        /// <summary>
        /// Get all validation errors
        /// </summary>
        public IReadOnlyList<ValidationError> GetErrors() => errors.AsReadOnly();

        /// <summary>
        /// Get all validation warnings
        /// </summary>
        public IReadOnlyList<ValidationError> GetWarnings() => warnings.AsReadOnly();

        /// <summary>
        /// Get validation summary
        /// </summary>
        public string GetValidationSummary()
        {
            return $"Validation Summary: {errors.Count} errors, {warnings.Count} warnings\n" +
                   $"Static Data: {registries.Religions.Count} religions, {registries.Cultures.Count} cultures, {registries.TradeGoods.Count} trade goods\n" +
                   $"Entities: {registries.Countries.Count} countries, {registries.Provinces.Count} provinces";
        }
    }

    /// <summary>
    /// Represents a validation error or warning
    /// </summary>
    public class ValidationError
    {
        public string Message { get; set; }
        public string Category { get; set; }
        public bool IsError { get; set; }

        public override string ToString()
        {
            return $"[{Category}] {(IsError ? "ERROR" : "WARNING")}: {Message}";
        }
    }
}