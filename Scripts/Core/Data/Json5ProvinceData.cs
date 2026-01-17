using Unity.Collections;
using System;

namespace Core.Data
{
    /// <summary>
    /// Burst-compatible struct for province data extracted from JSON5
    /// Used as intermediate format between JSON parsing and burst processing
    ///
    /// ENGINE layer - generic province data only.
    /// Game-specific fields (culture, religion, trade goods, development, etc.)
    /// should be parsed by game layer and stored in ProvinceColdData.CustomData.
    /// </summary>
    [Serializable]
    public struct RawProvinceData
    {
        // Core identity
        public int provinceID;

        // Ownership (generic - all games have ownership)
        public FixedString64Bytes owner;
        public FixedString64Bytes controller;

        // Flags for optional data
        public bool hasOwner;
        public bool hasController;

        public static RawProvinceData Invalid => new RawProvinceData
        {
            provinceID = 0,
            owner = new FixedString64Bytes("---"),
            controller = new FixedString64Bytes("---"),
            hasOwner = false,
            hasController = false
        };
    }

    /// <summary>
    /// Result of JSON5 province loading phase
    /// </summary>
    public struct Json5ProvinceLoadResult
    {
        public bool success;
        public NativeArray<RawProvinceData> rawData;
        public int loadedCount;
        public int failedCount;
        public string errorMessage;

        public static Json5ProvinceLoadResult Success(NativeArray<RawProvinceData> data, int loaded, int failed)
        {
            return new Json5ProvinceLoadResult
            {
                success = true,
                rawData = data,
                loadedCount = loaded,
                failedCount = failed,
                errorMessage = ""
            };
        }

        public static Json5ProvinceLoadResult Failure(string error)
        {
            return new Json5ProvinceLoadResult
            {
                success = false,
                rawData = new NativeArray<RawProvinceData>(),
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
