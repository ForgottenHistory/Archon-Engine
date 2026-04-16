using Unity.Collections;
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
        public byte Terrain;

        // Engine-level province properties
        public bool IsPassable;     // Default true; province json5 can set passable: false

        // Terrain override from province history file (highest priority)
        // Empty = no override (use auto-assign or terrain.json5)
        public FixedString64Bytes TerrainOverride;

        // Ownership tags for reference resolution
        public FixedString64Bytes OwnerTag;
        public FixedString64Bytes ControllerTag;

        // Game-specific fields (Culture, Religion, TradeGood, Development, etc.)
        // are NOT stored here — they belong in the game layer.
        // Game layer should parse province json5 files for its own fields.

        public static ProvinceInitialState Invalid => new ProvinceInitialState { IsValid = false };

        public static ProvinceInitialState Create(int provinceID)
        {
            return new ProvinceInitialState
            {
                ProvinceID = provinceID,
                IsValid = true,
                IsPassable = true
            };
        }

        /// <summary>
        /// Convert to hot ProvinceState for simulation (engine layer only)
        /// </summary>
        public ProvinceState ToProvinceState()
        {
            return new ProvinceState
            {
                ownerID = OwnerID,
                controllerID = ControllerID,
                terrainType = Terrain,
                gameDataSlot = (ushort)ProvinceID  // Default 1:1 mapping (provinceID == gameDataSlot)
            };
        }
    }

    /// <summary>
    /// Result container for Burst-compatible province history loading
    /// </summary>
    public struct ProvinceInitialStateLoadResult : IDisposable
    {
        public bool IsSuccess;
        public FixedString512Bytes ErrorMessage;
        public NativeArray<ProvinceInitialState> InitialStates;
        public int LoadedCount;
        public int FailedCount;

        public void Dispose()
        {
            if (InitialStates.IsCreated)
                InitialStates.Dispose();
        }

        public static ProvinceInitialStateLoadResult Failure(string error)
        {
            return new ProvinceInitialStateLoadResult
            {
                IsSuccess = false,
                ErrorMessage = new FixedString512Bytes(error),
                InitialStates = default
            };
        }

        public static ProvinceInitialStateLoadResult Success(NativeArray<ProvinceInitialState> states, int failed)
        {
            return new ProvinceInitialStateLoadResult
            {
                IsSuccess = true,
                InitialStates = states,
                LoadedCount = states.Length,
                FailedCount = failed
            };
        }
    }
}