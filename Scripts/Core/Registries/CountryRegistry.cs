using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Utils;

namespace Core.Registries
{
    /// <summary>
    /// Specialized registry for countries with 3-letter tags
    /// Handles country-specific requirements like owned provinces tracking
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class CountryRegistry
    {
        private readonly Dictionary<string, ushort> tagToId = new();
        private readonly List<CountryData> countries = new();
        private readonly GameRegistries gameRegistries;

        public string TypeName => "Country";
        public int Count => countries.Count - 1; // Exclude index 0

        public CountryRegistry(GameRegistries gameRegistries)
        {
            this.gameRegistries = gameRegistries;

            // Reserve index 0 for "none/unowned"
            countries.Add(null);

            ArchonLogger.LogDataLinking("CountryRegistry initialized");
        }

        /// <summary>
        /// Register a country with its 3-letter tag
        /// </summary>
        public ushort Register(string tag, CountryData country)
        {
            if (string.IsNullOrEmpty(tag))
                throw new System.ArgumentException("Country tag cannot be null or empty");

            if (tag.Length != 3)
                throw new System.ArgumentException($"Country tag must be exactly 3 characters, got: '{tag}'");

            if (country == null)
                throw new System.ArgumentNullException(nameof(country));

            if (tagToId.ContainsKey(tag))
                throw new System.InvalidOperationException($"Duplicate country tag: '{tag}'");

            if (countries.Count >= ushort.MaxValue)
                throw new System.InvalidOperationException($"CountryRegistry exceeded maximum capacity of {ushort.MaxValue}");

            ushort id = (ushort)countries.Count;
            countries.Add(country);
            tagToId[tag] = id;

            // Set the ID in the country data
            country.Id = id;
            country.Tag = tag;

            ArchonLogger.LogDataLinking($"Registered country '{tag}' with ID {id}");
            return id;
        }

        /// <summary>
        /// Get country by numeric ID (O(1) array access)
        /// </summary>
        public CountryData Get(ushort id)
        {
            if (id >= countries.Count)
                return null;

            return countries[id];
        }

        /// <summary>
        /// Get country by 3-letter tag (O(1) hash lookup)
        /// </summary>
        public CountryData Get(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return null;

            if (tagToId.TryGetValue(tag, out ushort id))
                return countries[id];

            return null;
        }

        /// <summary>
        /// Get country ID by tag
        /// </summary>
        public ushort GetId(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return 0;

            return tagToId.TryGetValue(tag, out ushort id) ? id : (ushort)0;
        }

        /// <summary>
        /// Try get country by tag
        /// </summary>
        public bool TryGet(string tag, out CountryData country)
        {
            country = null;

            if (string.IsNullOrEmpty(tag))
                return false;

            if (tagToId.TryGetValue(tag, out ushort id))
            {
                country = countries[id];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try get country ID by tag
        /// </summary>
        public bool TryGetId(string tag, out ushort id)
        {
            id = 0;

            if (string.IsNullOrEmpty(tag))
                return false;

            return tagToId.TryGetValue(tag, out id);
        }

        /// <summary>
        /// Check if country exists
        /// </summary>
        public bool Exists(string tag)
        {
            return !string.IsNullOrEmpty(tag) && tagToId.ContainsKey(tag);
        }

        /// <summary>
        /// Check if country ID exists
        /// </summary>
        public bool Exists(ushort id)
        {
            return id > 0 && id < countries.Count && countries[id] != null;
        }

        /// <summary>
        /// Get all countries
        /// </summary>
        public IEnumerable<CountryData> GetAll()
        {
            return countries.Skip(1).Where(country => country != null);
        }

        /// <summary>
        /// Get all valid country IDs
        /// </summary>
        public IEnumerable<ushort> GetAllIds()
        {
            for (ushort i = 1; i < countries.Count; i++)
            {
                if (countries[i] != null)
                    yield return i;
            }
        }

        /// <summary>
        /// Get all country tags
        /// </summary>
        public IEnumerable<string> GetAllTags()
        {
            return tagToId.Keys;
        }

        /// <summary>
        /// Get diagnostic information
        /// </summary>
        public string GetDiagnostics()
        {
            var validCountries = GetAll().Count();
            return $"CountryRegistry: {validCountries} countries, {tagToId.Count} tags, capacity {countries.Count - 1}";
        }
    }

    /// <summary>
    /// Core country data structure
    /// Will be expanded as we implement more features
    /// </summary>
    public class CountryData
    {
        public ushort Id { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }

        // Visual data
        public UnityEngine.Color32 Color { get; set; }

        // Owned provinces (will be populated by CrossReferenceBuilder)
        public List<ushort> OwnedProvinces { get; set; } = new();
        public List<ushort> ControlledProvinces { get; set; } = new();

        // Static references (resolved during linking)
        public ushort PrimaryCultureId { get; set; }
        public ushort ReligionId { get; set; }
        public ushort GovernmentId { get; set; }
        public ushort TechnologyGroupId { get; set; }

        // Game state
        public ushort CapitalProvinceId { get; set; }
    }
}