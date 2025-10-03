using Unity.Collections;
using Unity.Mathematics;
using System;

namespace Core.Data
{
    /// <summary>
    /// Initial state data extracted from province history files
    /// Used to initialize ProvinceState (hot data) at game start
    /// This is the Burst-compatible version of province history
    /// </summary>
    [System.Serializable]
    public struct ProvinceInitialState
    {
        public int ProvinceID;
        public bool IsValid;

        // Core state that goes into ProvinceState (hot data)
        public ushort OwnerID;
        public ushort ControllerID;
        public byte Development;    // Combined from BaseTax + BaseProduction + BaseManpower
        public byte Terrain;
        public byte Flags;          // IsCity, IsHRE, etc packed as bits

        // Additional cold data for initialization only
        public FixedString64Bytes OwnerTag;
        public FixedString64Bytes ControllerTag;
        public FixedString64Bytes Culture;
        public FixedString64Bytes Religion;
        public FixedString64Bytes TradeGood;

        // Development components
        public byte BaseTax;
        public byte BaseProduction;
        public byte BaseManpower;
        public byte CenterOfTrade;

        public static ProvinceInitialState Invalid => new ProvinceInitialState { IsValid = false };

        public static ProvinceInitialState Create(int provinceID)
        {
            return new ProvinceInitialState
            {
                ProvinceID = provinceID,
                IsValid = true
            };
        }

        /// <summary>
        /// Convert to hot ProvinceState for simulation
        /// </summary>
        public ProvinceState ToProvinceState()
        {
            return new ProvinceState
            {
                ownerID = OwnerID,
                controllerID = ControllerID,
                development = Development,
                terrain = Terrain,
                fortLevel = 0,
                flags = Flags
            };
        }

        /// <summary>
        /// Calculate development from base values
        /// Default formula: sum of components, capped at 255
        /// NOTE: This is a convenience default for Burst compatibility
        /// GAME can override Development value after loading if different formula is needed
        /// </summary>
        public void CalculateDevelopment()
        {
            Development = (byte)math.min(255, BaseTax + BaseProduction + BaseManpower);
        }

        /// <summary>
        /// Pack boolean flags into single byte
        /// </summary>
        public void PackFlags(bool isCity, bool isHRE)
        {
            Flags = 0;
            if (isCity) Flags |= 1;
            if (isHRE) Flags |= 2;
        }
    }

    /// <summary>
    /// Result container for Burst-compatible province history loading
    /// </summary>
    public struct ProvinceInitialStateLoadResult : IDisposable
    {
        public bool Success;
        public FixedString512Bytes ErrorMessage;
        public NativeArray<ProvinceInitialState> InitialStates;
        public int LoadedCount;
        public int FailedCount;

        public void Dispose()
        {
            if (InitialStates.IsCreated)
                InitialStates.Dispose();
        }

        public static ProvinceInitialStateLoadResult Failed(string error)
        {
            return new ProvinceInitialStateLoadResult
            {
                Success = false,
                ErrorMessage = new FixedString512Bytes(error),
                InitialStates = default
            };
        }

        public static ProvinceInitialStateLoadResult Successful(NativeArray<ProvinceInitialState> states, int failed)
        {
            return new ProvinceInitialStateLoadResult
            {
                Success = true,
                InitialStates = states,
                LoadedCount = states.Length,
                FailedCount = failed
            };
        }
    }
}