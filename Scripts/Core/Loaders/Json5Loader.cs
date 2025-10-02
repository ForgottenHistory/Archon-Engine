using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Loaders
{
    /// <summary>
    /// Simple and reliable JSON5 loader to replace the broken ParadoxParser
    /// Handles JSON5 files converted from Paradox format with proper structure
    /// </summary>
    public static class Json5Loader
    {
        /// <summary>
        /// Load and parse a JSON5 file
        /// </summary>
        public static JObject LoadJson5File(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"JSON5 file not found: {filePath}");
            }

            try
            {
                string content = File.ReadAllText(filePath);

                // Remove JSON5 comments (simple approach)
                content = RemoveJson5Comments(content);

                // Parse as JSON (since we removed comments and Unity's JSON parser is more reliable)
                return JObject.Parse(content);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Failed to parse JSON5 file {filePath}: {e.Message}", e);
            }
        }

        /// <summary>
        /// Remove JSON5-style comments from content
        /// </summary>
        private static string RemoveJson5Comments(string content)
        {
            var lines = content.Split('\n');
            var cleanLines = new List<string>();

            foreach (var line in lines)
            {
                string cleanLine = line;

                // Find comment start (// )
                int commentIndex = line.IndexOf("//");
                if (commentIndex >= 0)
                {
                    // Check if it's inside a string
                    string beforeComment = line.Substring(0, commentIndex);
                    if (beforeComment.Split('"').Length % 2 == 1) // Even number of quotes means not inside string
                    {
                        cleanLine = beforeComment.TrimEnd();
                    }
                }

                cleanLines.Add(cleanLine);
            }

            return string.Join("\n", cleanLines);
        }

        /// <summary>
        /// Get string value from JObject
        /// </summary>
        public static string GetString(JObject obj, string key, string defaultValue = "")
        {
            var token = obj[key];
            return token?.Value<string>() ?? defaultValue;
        }

        /// <summary>
        /// Get integer value from JObject
        /// </summary>
        public static int GetInt(JObject obj, string key, int defaultValue = 0)
        {
            var token = obj[key];
            return token?.Value<int>() ?? defaultValue;
        }

        /// <summary>
        /// Get float value from JObject
        /// </summary>
        public static float GetFloat(JObject obj, string key, float defaultValue = 0f)
        {
            var token = obj[key];
            return token?.Value<float>() ?? defaultValue;
        }

        /// <summary>
        /// Get boolean value from JObject
        /// </summary>
        public static bool GetBool(JObject obj, string key, bool defaultValue = false)
        {
            var token = obj[key];
            return token?.Value<bool>() ?? defaultValue;
        }

        /// <summary>
        /// Get array of strings from JObject
        /// </summary>
        public static List<string> GetStringArray(JObject obj, string key)
        {
            var result = new List<string>();
            var token = obj[key];

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    result.Add(item.Value<string>() ?? "");
                }
            }

            return result;
        }

        /// <summary>
        /// Get array of integers from JObject
        /// </summary>
        public static List<int> GetIntArray(JObject obj, string key)
        {
            var result = new List<int>();
            var token = obj[key];

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    result.Add(item.Value<int>());
                }
            }

            return result;
        }

        /// <summary>
        /// Get nested object from JObject
        /// </summary>
        public static JObject GetObject(JObject obj, string key)
        {
            var token = obj[key];
            return token as JObject;
        }

        /// <summary>
        /// Get all keys that start with a prefix (useful for date entries)
        /// </summary>
        public static List<string> GetKeysStartingWith(JObject obj, string prefix)
        {
            var result = new List<string>();

            foreach (var property in obj.Properties())
            {
                if (property.Name.StartsWith(prefix))
                {
                    result.Add(property.Name);
                }
            }

            return result;
        }

        /// <summary>
        /// Check if key exists in object
        /// </summary>
        public static bool HasKey(JObject obj, string key)
        {
            return obj.ContainsKey(key);
        }

        /// <summary>
        /// Convert Color array to Unity Color32
        /// </summary>
        public static Color32 GetColor32(JObject obj, string key, Color32 defaultColor = default)
        {
            var colorArray = GetIntArray(obj, key);

            if (colorArray.Count >= 3)
            {
                return new Color32(
                    (byte)colorArray[0],
                    (byte)colorArray[1],
                    (byte)colorArray[2],
                    colorArray.Count > 3 ? (byte)colorArray[3] : (byte)255
                );
            }

            return defaultColor;
        }

        /// <summary>
        /// Get all date-based entries (keys that look like dates)
        /// </summary>
        public static Dictionary<string, JObject> GetDateEntries(JObject obj)
        {
            var result = new Dictionary<string, JObject>();

            foreach (var property in obj.Properties())
            {
                // Check if key looks like a date (contains dots and digits)
                if (IsDateKey(property.Name) && property.Value is JObject dateObj)
                {
                    result[property.Name] = dateObj;
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a key looks like a date (e.g., "1444.11.11")
        /// </summary>
        private static bool IsDateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            // Simple check: contains at least two dots and starts with digit
            return key.Split('.').Length >= 3 && char.IsDigit(key[0]);
        }
    }
}