using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ParadoxDataLib.Core.Common;
using ParadoxDataLib.Core.Parsers.Csv.DataStructures;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Unity-friendly wrapper for ParadoxDataLib ProvinceDefinition
    /// Provides implicit conversions and Unity serialization support
    /// </summary>
    [System.Serializable]
    public class UnityProvinceDefinition
    {
        [SerializeField] private int _id;
        [SerializeField] private Color32 _color;
        [SerializeField] private string _name;
        [SerializeField] private string _category;
        [SerializeField] private bool _isValid;

        // Unity-friendly properties
        public int Id => _id;
        public Color32 Color => _color;
        public string Name => _name;
        public string Category => _category;
        public bool IsValid => _isValid;

        // Additional Unity-specific properties
        public bool IsLand => _category.Equals("land", StringComparison.OrdinalIgnoreCase);
        public bool IsSea => _category.Equals("sea", StringComparison.OrdinalIgnoreCase);
        public bool IsLake => _category.Equals("lake", StringComparison.OrdinalIgnoreCase);
        public bool IsWater => IsSea || IsLake;

        // RGB components for convenience
        public byte Red => _color.r;
        public byte Green => _color.g;
        public byte Blue => _color.b;

        public UnityProvinceDefinition()
        {
            // Default constructor for Unity serialization
        }

        public UnityProvinceDefinition(int id, byte r, byte g, byte b, string name = "", string category = "land")
        {
            _id = id;
            _color = new Color32(r, g, b, 255);
            _name = string.IsNullOrEmpty(name) ? $"Province_{id}" : name;
            _category = category;
            _isValid = id > 0;
        }

        public UnityProvinceDefinition(ProvinceDefinition paradoxDef)
        {
            if (paradoxDef != null)
            {
                _id = paradoxDef.ProvinceId;
                _color = new Color32(paradoxDef.Red, paradoxDef.Green, paradoxDef.Blue, 255);
                _name = paradoxDef.Name;
                _category = DetermineCategory(paradoxDef);
                _isValid = paradoxDef.IsValid;
            }
            else
            {
                _isValid = false;
            }
        }

        private string DetermineCategory(ProvinceDefinition paradoxDef)
        {
            // Default to land - will be updated by default.map data
            return "land";
        }

        // Implicit conversion from ParadoxDataLib type
        public static implicit operator UnityProvinceDefinition(ProvinceDefinition paradoxDef)
        {
            return new UnityProvinceDefinition(paradoxDef);
        }

        // Explicit conversion to ParadoxDataLib type (since ProvinceDefinition is struct)
        public static explicit operator ProvinceDefinition(UnityProvinceDefinition unityDef)
        {
            if (unityDef == null || !unityDef.IsValid)
                return default(ProvinceDefinition);

            return new ProvinceDefinition(
                unityDef.Id,
                unityDef.Red,
                unityDef.Green,
                unityDef.Blue,
                unityDef.Name ?? "",
                "x" // Unused field
            );
        }

        public override string ToString()
        {
            return $"Province {_id}: {_name} ({_category}) RGB({Red},{Green},{Blue})";
        }

        public override bool Equals(object obj)
        {
            if (obj is UnityProvinceDefinition other)
            {
                return _id == other._id && _color.Equals(other._color);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_id, _color);
        }
    }

    /// <summary>
    /// Unity-friendly wrapper for default.map data
    /// Provides structured access to map configuration
    /// </summary>
    [System.Serializable]
    public class UnityMapDefinition
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private int _maxProvinces;
        [SerializeField] private List<int> _seaProvinces;
        [SerializeField] private List<int> _lakeProvinces;
        [SerializeField] private List<int> _wastelandProvinces;
        [SerializeField] private Dictionary<string, string> _filePaths;

        // Unity-friendly properties
        public int Width => _width;
        public int Height => _height;
        public int MaxProvinces => _maxProvinces;
        public IReadOnlyList<int> SeaProvinces => _seaProvinces;
        public IReadOnlyList<int> LakeProvinces => _lakeProvinces;
        public IReadOnlyList<int> WastelandProvinces => _wastelandProvinces;
        public IReadOnlyDictionary<string, string> FilePaths => _filePaths;

        // Convenience properties
        public Vector2Int MapSize => new Vector2Int(_width, _height);
        public HashSet<int> AllWaterProvinces => new HashSet<int>(_seaProvinces.Concat(_lakeProvinces));

        public UnityMapDefinition()
        {
            // Default constructor for Unity serialization
            _seaProvinces = new List<int>();
            _lakeProvinces = new List<int>();
            _wastelandProvinces = new List<int>();
            _filePaths = new Dictionary<string, string>();
        }

        public UnityMapDefinition(ParadoxNode defaultMapNode)
        {
            _seaProvinces = new List<int>();
            _lakeProvinces = new List<int>();
            _wastelandProvinces = new List<int>();
            _filePaths = new Dictionary<string, string>();

            if (defaultMapNode != null)
            {
                ParseFromParadoxNode(defaultMapNode);
            }
        }

        private void ParseFromParadoxNode(ParadoxNode node)
        {
            // Parse basic properties
            _width = ParseIntValue(node, "width");
            _height = ParseIntValue(node, "height");
            _maxProvinces = ParseIntValue(node, "max_provinces");

            // Parse province lists
            _seaProvinces = ParseIntList(node, "sea_starts");
            _lakeProvinces = ParseIntList(node, "lakes");

            // Parse file paths
            ParseFilePaths(node);
        }

        private int ParseIntValue(ParadoxNode node, string key)
        {
            try
            {
                var value = node.GetValue<string>(key, "");
                return int.TryParse(value, out int result) ? result : 0;
            }
            catch
            {
                return 0;
            }
        }

        private List<int> ParseIntList(ParadoxNode node, string key)
        {
            var result = new List<int>();
            try
            {
                var values = node.GetValues<string>(key);
                foreach (var value in values)
                {
                    if (int.TryParse(value, out int id))
                    {
                        result.Add(id);
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }
            return result;
        }

        private void ParseFilePaths(ParadoxNode node)
        {
            var pathKeys = new[] { "definitions", "provinces", "positions", "terrain", "rivers", "heightmap", "adjacencies" };

            foreach (var key in pathKeys)
            {
                try
                {
                    var value = node.GetValue<string>(key, "");
                    if (!string.IsNullOrEmpty(value))
                    {
                        _filePaths[key] = value.Trim('"');
                    }
                }
                catch
                {
                    // Ignore missing paths
                }
            }
        }

        // Helper methods
        public bool IsSeaProvince(int provinceId) => _seaProvinces.Contains(provinceId);
        public bool IsLakeProvince(int provinceId) => _lakeProvinces.Contains(provinceId);
        public bool IsWaterProvince(int provinceId) => IsSeaProvince(provinceId) || IsLakeProvince(provinceId);
        public bool IsLandProvince(int provinceId) => !IsWaterProvince(provinceId) && !_wastelandProvinces.Contains(provinceId);

        public string GetFilePath(string key)
        {
            return _filePaths.TryGetValue(key, out string path) ? path : null;
        }

        public override string ToString()
        {
            return $"MapDefinition {_width}x{_height} - {_maxProvinces} provinces, {_seaProvinces.Count} seas, {_lakeProvinces.Count} lakes";
        }
    }

    /// <summary>
    /// Unity-friendly wrapper for province history data
    /// Provides date-based queries and Unity serialization
    /// </summary>
    [System.Serializable]
    public class UnityProvinceHistory
    {
        [SerializeField] private int _provinceId;
        [SerializeField] private Dictionary<string, object> _baseData;
        [SerializeField] private List<UnityHistoryEntry> _dateEntries;

        public int ProvinceId => _provinceId;
        public IReadOnlyDictionary<string, object> BaseData => _baseData;
        public IReadOnlyList<UnityHistoryEntry> DateEntries => _dateEntries;

        public UnityProvinceHistory()
        {
            _baseData = new Dictionary<string, object>();
            _dateEntries = new List<UnityHistoryEntry>();
        }

        public UnityProvinceHistory(int provinceId, ParadoxNode historyNode)
        {
            _provinceId = provinceId;
            _baseData = new Dictionary<string, object>();
            _dateEntries = new List<UnityHistoryEntry>();

            if (historyNode != null)
            {
                ParseFromParadoxNode(historyNode);
            }
        }

        private void ParseFromParadoxNode(ParadoxNode node)
        {
            // Parse base properties (non-date entries)
            foreach (var child in node.Children)
            {
                if (IsDateKey(child.Key))
                {
                    // Parse as date entry
                    var dateEntry = new UnityHistoryEntry(child.Key, child.Value as ParadoxNode);
                    _dateEntries.Add(dateEntry);
                }
                else
                {
                    // Parse as base property
                    _baseData[child.Key] = child.Value != null ? child.Value : child;
                }
            }

            // Sort date entries
            _dateEntries.Sort((a, b) => a.Date.CompareTo(b.Date));
        }

        private bool IsDateKey(string key)
        {
            // Check if key matches date format (e.g., "1444.11.11")
            return key.Contains('.') && key.Split('.').Length == 3;
        }

        // Query methods
        public T GetValue<T>(string key, DateTime? date = null)
        {
            if (date.HasValue)
            {
                // Find the most recent entry before or on the specified date
                var applicableEntry = _dateEntries
                    .Where(e => e.Date <= date.Value)
                    .OrderByDescending(e => e.Date)
                    .FirstOrDefault(e => e.Data.ContainsKey(key));

                if (applicableEntry != null && applicableEntry.Data.TryGetValue(key, out object value))
                {
                    return ConvertValue<T>(value);
                }
            }

            // Fall back to base data
            if (_baseData.TryGetValue(key, out object baseValue))
            {
                return ConvertValue<T>(baseValue);
            }

            return default(T);
        }

        private T ConvertValue<T>(object value)
        {
            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is string stringValue && typeof(T) != typeof(string))
                {
                    // Try to convert string to target type
                    return (T)Convert.ChangeType(stringValue, typeof(T));
                }

                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }

        public override string ToString()
        {
            return $"ProvinceHistory {_provinceId} - {_baseData.Count} base properties, {_dateEntries.Count} date entries";
        }
    }

    /// <summary>
    /// Represents a single date entry in province history
    /// </summary>
    [System.Serializable]
    public class UnityHistoryEntry
    {
        [SerializeField] private string _dateString;
        [SerializeField] private DateTime _date;
        [SerializeField] private Dictionary<string, object> _data;

        public string DateString => _dateString;
        public DateTime Date => _date;
        public IReadOnlyDictionary<string, object> Data => _data;

        public UnityHistoryEntry()
        {
            _data = new Dictionary<string, object>();
        }

        public UnityHistoryEntry(string dateString, ParadoxNode node)
        {
            _dateString = dateString;
            _date = ParseDate(dateString);
            _data = new Dictionary<string, object>();

            if (node != null)
            {
                foreach (var child in node.Children)
                {
                    _data[child.Key] = child.Value != null ? child.Value : child;
                }
            }
        }

        private DateTime ParseDate(string dateString)
        {
            try
            {
                var parts = dateString.Split('.');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int year) &&
                    int.TryParse(parts[1], out int month) &&
                    int.TryParse(parts[2], out int day))
                {
                    return new DateTime(year, month, day);
                }
            }
            catch
            {
                // Return default date on parse error
            }

            return DateTime.MinValue;
        }

        public T GetValue<T>(string key)
        {
            if (_data.TryGetValue(key, out object value))
            {
                try
                {
                    if (value is T directValue)
                        return directValue;

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        public override string ToString()
        {
            return $"HistoryEntry {_dateString} - {_data.Count} properties";
        }
    }

    /// <summary>
    /// Unity-friendly wrapper for country data
    /// </summary>
    [System.Serializable]
    public class UnityCountryData
    {
        [SerializeField] private string _tag;
        [SerializeField] private string _name;
        [SerializeField] private Color32 _color;
        [SerializeField] private Dictionary<string, object> _properties;

        public string Tag => _tag;
        public string Name => _name;
        public Color32 Color => _color;
        public IReadOnlyDictionary<string, object> Properties => _properties;

        public UnityCountryData()
        {
            _properties = new Dictionary<string, object>();
        }

        public UnityCountryData(string tag, ParadoxNode countryNode)
        {
            _tag = tag;
            _properties = new Dictionary<string, object>();

            if (countryNode != null)
            {
                ParseFromParadoxNode(countryNode);
            }
        }

        private void ParseFromParadoxNode(ParadoxNode node)
        {
            foreach (var child in node.Children)
            {
                _properties[child.Key] = child.Value != null ? child.Value : child;

                // Extract common properties
                switch (child.Key.ToLower())
                {
                    case "name":
                        _name = child.Value?.ToString();
                        break;
                    case "color":
                        ParseColor(child.Value as ParadoxNode);
                        break;
                }
            }
        }

        private void ParseColor(ParadoxNode colorNode)
        {
            try
            {
                if (colorNode != null)
                {
                    // Try to get color values from the color node
                    var colorString = colorNode.Value?.ToString();
                    if (!string.IsNullOrEmpty(colorString))
                    {
                        var parts = colorString.Split(' ');
                        if (parts.Length >= 3)
                        {
                            if (byte.TryParse(parts[0], out byte r) &&
                                byte.TryParse(parts[1], out byte g) &&
                                byte.TryParse(parts[2], out byte b))
                            {
                                _color = new Color32(r, g, b, 255);
                                return;
                            }
                        }
                    }
                }
                _color = new Color32(255, 255, 255, 255);
            }
            catch
            {
                _color = new Color32(255, 255, 255, 255);
            }
        }

        public T GetProperty<T>(string key)
        {
            if (_properties.TryGetValue(key, out object value))
            {
                try
                {
                    if (value is T directValue)
                        return directValue;

                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        public override string ToString()
        {
            return $"Country {_tag}: {_name} - {_properties.Count} properties";
        }
    }

    /// <summary>
    /// Helper class for converting between Unity and ParadoxDataLib types
    /// </summary>
    public static class UnityParadoxConverter
    {
        /// <summary>
        /// Convert a list of ParadoxDataLib ProvinceDefinitions to Unity wrappers
        /// </summary>
        public static List<UnityProvinceDefinition> ConvertProvinceDefinitions(IEnumerable<ProvinceDefinition> paradoxDefinitions)
        {
            return paradoxDefinitions?.Select(def => new UnityProvinceDefinition(def)).ToList() ?? new List<UnityProvinceDefinition>();
        }

        /// <summary>
        /// Convert Unity wrappers back to ParadoxDataLib ProvinceDefinitions
        /// </summary>
        public static List<ProvinceDefinition> ConvertToParadoxDefinitions(IEnumerable<UnityProvinceDefinition> unityDefinitions)
        {
            return unityDefinitions?.Where(def => def.IsValid).Select(def => (ProvinceDefinition)def).ToList() ?? new List<ProvinceDefinition>();
        }

        /// <summary>
        /// Create Unity Color32 from RGB values
        /// </summary>
        public static Color32 CreateColor32(byte r, byte g, byte b)
        {
            return new Color32(r, g, b, 255);
        }

        /// <summary>
        /// Create Unity Color32 from ParadoxDataLib ProvinceDefinition
        /// </summary>
        public static Color32 GetColor32FromDefinition(ProvinceDefinition definition)
        {
            return new Color32(definition.Red, definition.Green, definition.Blue, 255);
        }
    }
}