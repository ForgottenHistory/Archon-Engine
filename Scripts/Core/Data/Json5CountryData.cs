using Unity.Collections;
using System;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Burst-compatible struct for country data extracted from JSON5
    /// Used as intermediate format between JSON parsing and burst processing
    ///
    /// ENGINE layer - generic country data only.
    /// Game-specific fields (religion, revolutionary colors, etc.) should be
    /// parsed by game layer and stored in CountryColdData.customData.
    /// </summary>
    [Serializable]
    public struct RawCountryData
    {
        // Core identity
        public FixedString64Bytes tag;
        public FixedString64Bytes graphicalCulture;

        // Color information (RGB values 0-255)
        public byte colorR;
        public byte colorG;
        public byte colorB;

        // Flags for optional data
        public bool hasGraphicalCulture;

        public static RawCountryData Invalid => new RawCountryData
        {
            tag = new FixedString64Bytes("---"),
            graphicalCulture = new FixedString64Bytes("unknown"),
            colorR = 128,
            colorG = 128,
            colorB = 128,
            hasGraphicalCulture = false
        };

        /// <summary>
        /// Get the country color as Unity Color32
        /// </summary>
        public Color32 GetColor()
        {
            return new Color32(colorR, colorG, colorB, 255);
        }
    }

    /// <summary>
    /// Result of JSON5 country loading phase
    /// </summary>
    public struct Json5CountryLoadResult
    {
        public bool success;
        public NativeArray<RawCountryData> rawData;
        public int loadedCount;
        public int failedCount;
        public string errorMessage;

        public static Json5CountryLoadResult Success(NativeArray<RawCountryData> data, int loaded, int failed)
        {
            return new Json5CountryLoadResult
            {
                success = true,
                rawData = data,
                loadedCount = loaded,
                failedCount = failed,
                errorMessage = ""
            };
        }

        public static Json5CountryLoadResult Failure(string error)
        {
            return new Json5CountryLoadResult
            {
                success = false,
                rawData = new NativeArray<RawCountryData>(),
                loadedCount = 0,
                failedCount = 0,
                errorMessage = error
            };
        }

        public void Dispose()
        {
            if (rawData.IsCreated)
                rawData.Dispose();
        }
    }
}
