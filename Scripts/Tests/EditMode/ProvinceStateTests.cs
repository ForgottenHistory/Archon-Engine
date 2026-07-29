using System;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Core.Data;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for ProvinceState - the 8-byte simulation struct.
    ///
    /// WHY THIS MATTERS: the 8-byte size is an architectural invariant, not an
    /// optimisation. Province data is stored as a contiguous NativeArray sized on that
    /// assumption, and the byte serialisation reinterprets raw memory. A field added
    /// carelessly changes the memory footprint, the network payload, and the save format
    /// all at once.
    /// </summary>
    public class ProvinceStateTests
    {
        // ===== Size invariant =====

        [Test]
        public void Size_IsExactlyEightBytes()
        {
            Assert.AreEqual(8, UnsafeUtility.SizeOf<ProvinceState>(),
                "ProvinceState must remain exactly 8 bytes. Changing it breaks the " +
                "memory budget, the network payload and the save format simultaneously.");
        }

        [Test]
        public void ToBytes_ProducesEightBytes()
        {
            Assert.AreEqual(8, ProvinceState.CreateDefault().ToBytes().Length);
        }

        // ===== Serialisation round-trip =====

        [Test]
        public void ToBytes_ThenFromBytes_PreservesAllFields()
        {
            var original = new ProvinceState
            {
                ownerID = 1234,
                controllerID = 5678,
                terrainType = 42,
                gameDataSlot = 999
            };

            var restored = ProvinceState.FromBytes(original.ToBytes());

            Assert.AreEqual(original.ownerID, restored.ownerID, "ownerID");
            Assert.AreEqual(original.controllerID, restored.controllerID, "controllerID");
            Assert.AreEqual(original.terrainType, restored.terrainType, "terrainType");
            Assert.AreEqual(original.gameDataSlot, restored.gameDataSlot, "gameDataSlot");
        }

        [Test]
        public void ToBytes_ThenFromBytes_PreservesExtremeValues()
        {
            var original = new ProvinceState
            {
                ownerID = ushort.MaxValue,
                controllerID = 0,
                terrainType = ushort.MaxValue,
                gameDataSlot = 0
            };

            var restored = ProvinceState.FromBytes(original.ToBytes());

            Assert.AreEqual(original.ownerID, restored.ownerID);
            Assert.AreEqual(original.controllerID, restored.controllerID);
            Assert.AreEqual(original.terrainType, restored.terrainType);
            Assert.AreEqual(original.gameDataSlot, restored.gameDataSlot);
        }

        [Test]
        public void FromBytes_WithWrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => ProvinceState.FromBytes(new byte[7]));
            Assert.Throws<ArgumentException>(() => ProvinceState.FromBytes(new byte[9]));
        }

        [Test]
        public void FromBytes_WithNull_Throws()
        {
            Assert.Throws<ArgumentException>(() => ProvinceState.FromBytes(null));
        }

        // ===== Factory methods =====

        [Test]
        public void CreateDefault_IsUnownedGrassland()
        {
            var state = ProvinceState.CreateDefault();

            Assert.AreEqual(0, state.ownerID, "Default must be unowned");
            Assert.AreEqual(0, state.controllerID, "Default must be uncontrolled");
            Assert.AreEqual(1, state.terrainType, "Default terrain is grassland");
            Assert.IsFalse(state.IsOwned);
        }

        [Test]
        public void CreateOwned_SetsControllerToOwner()
        {
            var state = ProvinceState.CreateOwned(7);

            Assert.AreEqual(7, state.ownerID);
            Assert.AreEqual(7, state.controllerID,
                "A newly owned province is controlled by its owner");
            Assert.IsTrue(state.IsOwned);
            Assert.IsFalse(state.IsOccupied, "Owner-controlled is not occupied");
        }

        [Test]
        public void CreateOcean_IsUnownedWithTerrainZero()
        {
            var state = ProvinceState.CreateOcean();

            Assert.AreEqual(0, state.terrainType);
            Assert.IsTrue(state.IsOcean);
            Assert.IsFalse(state.IsOwned);
        }

        [Test]
        public void FactoryMethods_PreserveGameDataSlot()
        {
            Assert.AreEqual(123, ProvinceState.CreateDefault(1, 123).gameDataSlot);
            Assert.AreEqual(456, ProvinceState.CreateOwned(1, 1, 456).gameDataSlot);
            Assert.AreEqual(789, ProvinceState.CreateOcean(789).gameDataSlot);
        }

        // ===== Derived properties =====

        [Test]
        public void IsOwned_IsFalseOnlyForOwnerZero()
        {
            Assert.IsFalse(new ProvinceState { ownerID = 0 }.IsOwned);
            Assert.IsTrue(new ProvinceState { ownerID = 1 }.IsOwned);
            Assert.IsTrue(new ProvinceState { ownerID = ushort.MaxValue }.IsOwned);
        }

        [Test]
        public void IsOccupied_RequiresAnOwnerAndADifferentController()
        {
            var occupied = new ProvinceState { ownerID = 5, controllerID = 9 };
            Assert.IsTrue(occupied.IsOccupied, "Different owner and controller means occupied");

            var peaceful = new ProvinceState { ownerID = 5, controllerID = 5 };
            Assert.IsFalse(peaceful.IsOccupied, "Owner controlling its own land is not occupation");
        }

        /// <summary>
        /// An unowned province with a non-zero controller is deliberately NOT occupied -
        /// occupation is defined relative to an owner, and there is nobody to occupy from.
        /// </summary>
        [Test]
        public void IsOccupied_IsFalseForUnownedProvinces()
        {
            var state = new ProvinceState { ownerID = 0, controllerID = 9 };

            Assert.IsFalse(state.IsOccupied,
                "With no owner there is nothing to occupy, even with a controller set");
        }

        [Test]
        public void IsOcean_IsTrueOnlyForTerrainZero()
        {
            Assert.IsTrue(new ProvinceState { terrainType = 0 }.IsOcean);
            Assert.IsFalse(new ProvinceState { terrainType = 1 }.IsOcean);
        }

        // ===== Equality and hashing =====

        [Test]
        public void Equals_ComparesAllFields()
        {
            var a = new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 3, gameDataSlot = 4 };
            var b = new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 3, gameDataSlot = 4 };

            Assert.IsTrue(a.Equals(b));
        }

        [Test]
        public void Equals_DetectsDifferenceInEachField()
        {
            var baseState = new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 3, gameDataSlot = 4 };

            Assert.IsFalse(baseState.Equals(
                new ProvinceState { ownerID = 9, controllerID = 2, terrainType = 3, gameDataSlot = 4 }),
                "ownerID difference must be detected");
            Assert.IsFalse(baseState.Equals(
                new ProvinceState { ownerID = 1, controllerID = 9, terrainType = 3, gameDataSlot = 4 }),
                "controllerID difference must be detected");
            Assert.IsFalse(baseState.Equals(
                new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 9, gameDataSlot = 4 }),
                "terrainType difference must be detected");
            Assert.IsFalse(baseState.Equals(
                new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 3, gameDataSlot = 9 }),
                "gameDataSlot difference must be detected");
        }

        /// <summary>
        /// The hash is used for multiplayer state checksums, so equal states must hash
        /// equally - otherwise clients report a desync that has not happened.
        /// </summary>
        [Test]
        public void GetHashCode_IsEqualForEqualStates()
        {
            var a = new ProvinceState { ownerID = 100, controllerID = 200, terrainType = 3, gameDataSlot = 400 };
            var b = new ProvinceState { ownerID = 100, controllerID = 200, terrainType = 3, gameDataSlot = 400 };

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void GetHashCode_DiffersForDifferentStates()
        {
            var a = new ProvinceState { ownerID = 1, controllerID = 2, terrainType = 3, gameDataSlot = 4 };
            var b = new ProvinceState { ownerID = 4, controllerID = 3, terrainType = 2, gameDataSlot = 1 };

            Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode(),
                "Distinct states should not collide on this simple field permutation");
        }

        [Test]
        public void GetHashCode_IsStableAcrossCalls()
        {
            var state = new ProvinceState { ownerID = 7, controllerID = 8, terrainType = 9, gameDataSlot = 10 };

            Assert.AreEqual(state.GetHashCode(), state.GetHashCode(),
                "A checksum that changes between calls is useless");
        }

        // ===== Terrain enum =====

        [Test]
        public void TerrainType_OceanIsZero()
        {
            Assert.AreEqual(0, (ushort)TerrainType.Ocean,
                "IsOcean tests terrainType == 0, so Ocean must stay at 0");
        }
    }
}
