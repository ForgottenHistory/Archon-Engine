namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Modifier type IDs for the modifier system.
    /// Maps string keys from JSON5 data files to ushort IDs for the Engine ModifierSystem.
    ///
    /// Convention:
    /// - Keys ending in "_modifier" are multiplicative (percentage bonus)
    /// - Keys ending in "_additive" or "monthly_" are additive (flat bonus)
    ///
    /// Usage:
    ///   modifierSystem.AddProvinceModifier(provinceId, ModifierSource.CreatePermanent(
    ///       ModifierSource.SourceType.Building,
    ///       buildingId,
    ///       (ushort)ModifierType.LocalIncomeModifier,
    ///       value,
    ///       isMultiplicative: true
    ///   ));
    /// </summary>
    public enum ModifierType : ushort
    {
        // Province-level modifiers (local to province)
        LocalIncomeModifier = 1,      // Multiplicative: +X% income in province
        LocalIncomeAdditive = 2,      // Additive: +X flat gold in province

        // Country-level modifiers (applied to all country provinces)
        CountryIncomeModifier = 100,  // Multiplicative: +X% income country-wide
    }

    /// <summary>
    /// Helper methods for modifier types
    /// </summary>
    public static class ModifierTypeHelper
    {
        /// <summary>
        /// Convert string key (from JSON5) to ModifierType
        /// </summary>
        public static ModifierType? FromKey(string key)
        {
            return key switch
            {
                "local_income_modifier" => ModifierType.LocalIncomeModifier,
                "local_income_additive" => ModifierType.LocalIncomeAdditive,
                "country_income_modifier" => ModifierType.CountryIncomeModifier,
                _ => null
            };
        }

        /// <summary>
        /// Get string key for a ModifierType
        /// </summary>
        public static string ToKey(ModifierType type)
        {
            return type switch
            {
                ModifierType.LocalIncomeModifier => "local_income_modifier",
                ModifierType.LocalIncomeAdditive => "local_income_additive",
                ModifierType.CountryIncomeModifier => "country_income_modifier",
                _ => null
            };
        }

        /// <summary>
        /// Check if a modifier type is multiplicative (percentage) or additive (flat)
        /// </summary>
        public static bool IsMultiplicative(ModifierType type)
        {
            // Convention: "_modifier" types are multiplicative, "_additive" types are additive
            return type switch
            {
                ModifierType.LocalIncomeModifier => true,
                ModifierType.LocalIncomeAdditive => false,  // Additive (flat bonus)
                ModifierType.CountryIncomeModifier => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if a modifier is province-local or country-wide
        /// </summary>
        public static bool IsCountryWide(ModifierType type)
        {
            return (ushort)type >= 100;
        }
    }
}
