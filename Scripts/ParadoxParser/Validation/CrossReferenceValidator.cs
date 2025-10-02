using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Cross-reference validation for Paradox data
    /// Validates that referenced IDs, tags, and keys exist in their respective databases
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class CrossReferenceValidator
    {
        /// <summary>
        /// Reference validation message hashes
        /// </summary>
        public static class MessageHashes
        {
            public static readonly uint InvalidReference = FastHasher.HashFNV1a32("INVALID_REFERENCE");
            public static readonly uint MissingProvince = FastHasher.HashFNV1a32("MISSING_PROVINCE");
            public static readonly uint MissingCountry = FastHasher.HashFNV1a32("MISSING_COUNTRY");
            public static readonly uint MissingCulture = FastHasher.HashFNV1a32("MISSING_CULTURE");
            public static readonly uint MissingReligion = FastHasher.HashFNV1a32("MISSING_RELIGION");
            public static readonly uint MissingTechnology = FastHasher.HashFNV1a32("MISSING_TECHNOLOGY");
            public static readonly uint MissingTradeGood = FastHasher.HashFNV1a32("MISSING_TRADE_GOOD");
            public static readonly uint CircularReference = FastHasher.HashFNV1a32("CIRCULAR_REFERENCE");
        }

        /// <summary>
        /// Types of references that can be validated
        /// </summary>
        public enum ReferenceType : byte
        {
            Identifier = 0,  // Generic identifier reference
            Numeric = 1,     // Numeric ID reference
            File = 2,        // File path reference
            Key = 3          // Key reference to another definition
        }

        /// <summary>
        /// Reference validation rule
        /// </summary>
        public struct ReferenceRule
        {
            public uint KeyHash;
            public ReferenceType Type;
            public bool Required;

            public static ReferenceRule Create(uint keyHash, ReferenceType type, bool required = true)
            {
                return new ReferenceRule
                {
                    KeyHash = keyHash,
                    Type = type,
                    Required = required
                };
            }
        }

        /// <summary>
        /// Database of valid references for lookup
        /// Generic collections that can store any type of identifier
        /// </summary>
        public struct ReferenceDatabase
        {
            public NativeHashSet<uint> ValidIdentifiers;
            public NativeHashSet<uint> ValidNumericIds;
            public NativeHashSet<uint> ValidKeys;

            public static ReferenceDatabase Create(Allocator allocator)
            {
                return new ReferenceDatabase
                {
                    ValidIdentifiers = new NativeHashSet<uint>(1000, allocator),
                    ValidNumericIds = new NativeHashSet<uint>(1000, allocator),
                    ValidKeys = new NativeHashSet<uint>(1000, allocator)
                };
            }

            public void Dispose()
            {
                if (ValidIdentifiers.IsCreated) ValidIdentifiers.Dispose();
                if (ValidNumericIds.IsCreated) ValidNumericIds.Dispose();
                if (ValidKeys.IsCreated) ValidKeys.Dispose();
            }
        }

        /// <summary>
        /// Validate cross-references in parsed data
        /// </summary>
        public static ValidationResult ValidateCrossReferences(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ReferenceDatabase database,
            SemanticValidator.ValidationContext context,
            ValidationOptions options)
        {
            var messages = new NativeList<ValidationMessage>(32, Allocator.Temp);
            var result = ValidationResult.Valid;
            result.Messages = messages;

            try
            {
                if (options.ValidateReferences)
                {
                    var referenceRules = GetReferenceRulesForContext(context);

                    try
                    {
                        ValidateReferences(keyValues, sourceData, database, referenceRules, ref result, options);
                        ValidateCircularReferences(keyValues, ref result, options);
                    }
                    finally
                    {
                        referenceRules.Dispose();
                    }
                }

                return ValidationResult.Create(result.Messages);
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Validate individual references
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateReferences(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ReferenceDatabase database,
            NativeArray<ReferenceRule> referenceRules,
            ref ValidationResult result,
            ValidationOptions options)
        {
            for (int i = 0; i < keyValues.Length; i++)
            {
                var kvp = keyValues[i];

                // Find reference rule for this key
                ReferenceRule? rule = null;
                for (int j = 0; j < referenceRules.Length; j++)
                {
                    if (referenceRules[j].KeyHash == kvp.KeyHash)
                    {
                        rule = referenceRules[j];
                        break;
                    }
                }

                if (rule.HasValue)
                {
                    ValidateReference(kvp, sourceData, database, rule.Value, ref result);
                }

                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }
        }

        /// <summary>
        /// Validate a single reference
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateReference(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ReferenceDatabase database,
            ReferenceRule rule,
            ref ValidationResult result)
        {
            // Only validate literal values that could be references
            if (kvp.Value.Type != ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
                return;

            var referenceHash = FastHasher.HashFNV1a32(kvp.Value.RawData);
            bool exists = false;
            uint messageHash = MessageHashes.InvalidReference;

            switch (rule.Type)
            {
                case ReferenceType.Identifier:
                    exists = database.ValidIdentifiers.Contains(referenceHash);
                    messageHash = MessageHashes.InvalidReference;
                    break;

                case ReferenceType.Numeric:
                    exists = database.ValidNumericIds.Contains(referenceHash);
                    messageHash = MessageHashes.InvalidReference;
                    break;

                case ReferenceType.Key:
                    exists = database.ValidKeys.Contains(referenceHash);
                    messageHash = MessageHashes.InvalidReference;
                    break;

                case ReferenceType.File:
                    // File references could be validated by checking file existence
                    exists = true; // Skip for now
                    break;
            }

            if (!exists)
            {
                var severity = rule.Required ? ValidationSeverity.Error : ValidationSeverity.Warning;
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Reference,
                    severity,
                    kvp.LineNumber,
                    0,
                    0,
                    messageHash,
                    kvp.Value.RawData);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate for circular references
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateCircularReferences(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            ref ValidationResult result,
            ValidationOptions options)
        {
            // Track dependencies for circular reference detection
            var dependencies = new NativeHashMap<uint, uint>(keyValues.Length, Allocator.Temp);
            var visited = new NativeHashSet<uint>(keyValues.Length, Allocator.Temp);
            var recursionStack = new NativeHashSet<uint>(keyValues.Length, Allocator.Temp);

            try
            {
                // Build dependency graph
                for (int i = 0; i < keyValues.Length; i++)
                {
                    var kvp = keyValues[i];
                    if (IsHierarchicalKey(kvp.KeyHash))
                    {
                        var valueHash = FastHasher.HashFNV1a32(kvp.Value.RawData);
                        dependencies.TryAdd(kvp.KeyHash, valueHash);
                    }
                }

                // Check for cycles using DFS
                var keys = dependencies.GetKeyArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (!visited.Contains(keys[i]))
                        {
                            if (HasCircularReference(keys[i], dependencies, visited, recursionStack))
                            {
                                var message = ValidationMessage.Create(
                                    ValidationType.Reference,
                                    ValidationSeverity.Error,
                                    1, // Line unknown for circular references
                                    1,
                                    0,
                                    MessageHashes.CircularReference);
                                result.AddMessage(message);
                                break; // Stop after finding first circular reference
                            }
                        }
                    }
                }
                finally
                {
                    keys.Dispose();
                }
            }
            finally
            {
                dependencies.Dispose();
                visited.Dispose();
                recursionStack.Dispose();
            }
        }

        /// <summary>
        /// Check if a key represents a hierarchical relationship
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHierarchicalKey(uint keyHash)
        {
            // Generic hierarchical keys that could form cycles
            return keyHash == FastHasher.HashFNV1a32("parent") ||
                   keyHash == FastHasher.HashFNV1a32("child") ||
                   keyHash == FastHasher.HashFNV1a32("depends_on") ||
                   keyHash == FastHasher.HashFNV1a32("refers_to") ||
                   keyHash == FastHasher.HashFNV1a32("inherits_from");
        }

        /// <summary>
        /// DFS to detect circular references
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasCircularReference(
            uint node,
            NativeHashMap<uint, uint> dependencies,
            NativeHashSet<uint> visited,
            NativeHashSet<uint> recursionStack)
        {
            visited.Add(node);
            recursionStack.Add(node);

            if (dependencies.TryGetValue(node, out var dependency))
            {
                if (!visited.Contains(dependency))
                {
                    if (HasCircularReference(dependency, dependencies, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(dependency))
                {
                    return true; // Found cycle
                }
            }

            recursionStack.Remove(node);
            return false;
        }

        /// <summary>
        /// Get reference rules for a validation context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<ReferenceRule> GetReferenceRulesForContext(SemanticValidator.ValidationContext context)
        {
            switch (context)
            {
                case SemanticValidator.ValidationContext.Country:
                    return CreateCountryReferenceRules();
                case SemanticValidator.ValidationContext.Province:
                    return CreateProvinceReferenceRules();
                default:
                    return new NativeArray<ReferenceRule>(0, Allocator.Temp);
            }
        }

        private static NativeArray<ReferenceRule> CreateCountryReferenceRules()
        {
            var rules = new NativeArray<ReferenceRule>(6, Allocator.Temp);
            int index = 0;

            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("capital"), ReferenceType.Numeric);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("culture"), ReferenceType.Identifier);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("religion"), ReferenceType.Identifier);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("parent"), ReferenceType.Identifier, required: false);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("depends_on"), ReferenceType.Identifier, required: false);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("tech_group"), ReferenceType.Identifier);

            return rules;
        }

        private static NativeArray<ReferenceRule> CreateProvinceReferenceRules()
        {
            var rules = new NativeArray<ReferenceRule>(5, Allocator.Temp);
            int index = 0;

            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("owner"), ReferenceType.Identifier);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("controller"), ReferenceType.Identifier, required: false);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("culture"), ReferenceType.Identifier);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("religion"), ReferenceType.Identifier);
            rules[index++] = ReferenceRule.Create(FastHasher.HashFNV1a32("resource"), ReferenceType.Identifier);

            return rules;
        }

        /// <summary>
        /// Populate reference database with example data
        /// In a real implementation, this would be loaded from configuration files
        /// </summary>
        public static void PopulateDefaultDatabase(ref ReferenceDatabase database)
        {
            // Add example identifiers (cultures, religions, trade goods, etc.)
            var exampleIdentifiers = new string[]
            {
                "culture_a", "culture_b", "culture_c", "northern_culture", "southern_culture",
                "religion_a", "religion_b", "ancient_faith", "modern_faith", "folk_belief",
                "grain", "livestock", "fish", "stone", "wood", "metal", "gems", "textiles",
                "western", "eastern", "muslim", "chinese", "indian"
            };

            foreach (var identifier in exampleIdentifiers)
            {
                var hash = FastHasher.HashFNV1a32(identifier);
                database.ValidIdentifiers.Add(hash);
            }

            // Add example numeric IDs (province IDs, etc.)
            for (int i = 1; i <= 100; i++)
            {
                var hash = FastHasher.HashFNV1a32(i.ToString());
                database.ValidNumericIds.Add(hash);
            }

            // Add example keys (common keys from Paradox files)
            var exampleKeys = new string[]
            {
                "owner", "controller", "culture", "religion", "capital", "government",
                "tech_group", "primary_culture", "stability", "legitimacy", "prestige"
            };

            foreach (var key in exampleKeys)
            {
                var hash = FastHasher.HashFNV1a32(key);
                database.ValidKeys.Add(hash);
            }
        }
    }
}