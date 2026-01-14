using Unity.Collections;
using Unity.Mathematics;
using System;

namespace Core.Data
{
    /// <summary>
    /// Burst-compatible struct for province data extracted from JSON5
    /// Used as intermediate format between JSON parsing and burst processing
    /// </summary>
    [Serializable]
    public struct RawProvinceData
    {
        public int provinceID;
        public FixedString64Bytes owner;
        public FixedString64Bytes controller;
        public FixedString64Bytes culture;
        public FixedString64Bytes religion;
        public FixedString64Bytes tradeGood;
        public int baseTax;
        public int baseProduction;
        public int baseManpower;
        public FixedString64Bytes capital;
        public bool isCity;
        public bool hre;
        public int centerOfTrade;
        public int extraCost;

        // Flags for optional data
        public bool hasOwner;
        public bool hasController;
        public bool hasCulture;
        public bool hasReligion;
        public bool hasTradeGood;
        public bool hasCapital;

        public static RawProvinceData Invalid => new RawProvinceData
        {
            provinceID = 0,
            owner = new FixedString64Bytes("---"),
            controller = new FixedString64Bytes("---"),
            culture = new FixedString64Bytes("unknown"),
            religion = new FixedString64Bytes("unknown"),
            tradeGood = new FixedString64Bytes("unknown"),
            baseTax = 1,
            baseProduction = 1,
            baseManpower = 1,
            capital = new FixedString64Bytes(""),
            isCity = false,
            hre = false,
            centerOfTrade = 0,
            extraCost = 0,
            hasOwner = false,
            hasController = false,
            hasCulture = false,
            hasReligion = false,
            hasTradeGood = false,
            hasCapital = false
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