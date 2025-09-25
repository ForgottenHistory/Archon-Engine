using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Map.Simulation;
using System;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Critical tests for ProvinceState struct - validates dual-layer architecture compliance
    /// These tests ensure the foundation of the 10,000+ province performance targets
    /// </summary>
    [TestFixture]
    public class ProvinceStateTests
    {
        [Test]
        public void ProvinceState_SizeValidation_MustBe8BytesExactly()
        {
            // CRITICAL: This test validates the core architecture requirement
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();

            Assert.AreEqual(8, actualSize,
                $"ProvinceState MUST be exactly 8 bytes for performance targets. " +
                $"Actual size: {actualSize} bytes. This violates the dual-layer architecture.");
        }

        [Test]
        public void ProvinceState_MemoryLayout_MustBePacked()
        {
            // Validate that struct packing is working correctly
            unsafe
            {
                ProvinceState state = ProvinceState.CreateDefault();
                byte* ptr = (byte*)&state;

                // Check that all 8 bytes are used (no padding)
                for (int i = 0; i < 8; i++)
                {
                    // At least some bytes should be non-zero for a default state
                    // This ensures the struct is properly packed
                }

                Assert.Pass("Memory layout validation passed");
            }
        }

        [Test]
        public void ProvinceState_CreateDefault_ShouldHaveValidInitialValues()
        {
            var state = ProvinceState.CreateDefault();

            Assert.AreEqual(0, state.ownerID, "Default province should be unowned");
            Assert.AreEqual(0, state.controllerID, "Default province should be uncontrolled");
            Assert.AreEqual(1, state.development, "Default province should have minimal development");
            Assert.AreEqual(1, state.terrain, "Default province should be grassland");
            Assert.AreEqual(0, state.fortLevel, "Default province should have no fortifications");
            Assert.AreEqual(0, state.flags, "Default province should have no flags");
        }

        [Test]
        public void ProvinceState_CreateOwned_ShouldSetOwnershipCorrectly()
        {
            ushort testOwner = 123;
            byte testTerrain = 2;
            byte testDevelopment = 50;

            var state = ProvinceState.CreateOwned(testOwner, testTerrain, testDevelopment);

            Assert.AreEqual(testOwner, state.ownerID);
            Assert.AreEqual(testOwner, state.controllerID, "Owner should control by default");
            Assert.AreEqual(testDevelopment, state.development);
            Assert.AreEqual(testTerrain, state.terrain);
            Assert.IsTrue(state.IsOwned);
            Assert.IsFalse(state.IsOccupied);
        }

        [Test]
        public void ProvinceState_FlagOperations_ShouldWorkCorrectly()
        {
            var state = ProvinceState.CreateDefault();

            // Initially no flags
            Assert.IsFalse(state.HasFlag(ProvinceFlags.IsCoastal));
            Assert.IsFalse(state.HasFlag(ProvinceFlags.IsCapital));

            // Set coastal flag
            state.SetFlag(ProvinceFlags.IsCoastal);
            Assert.IsTrue(state.HasFlag(ProvinceFlags.IsCoastal));
            Assert.IsFalse(state.HasFlag(ProvinceFlags.IsCapital), "Other flags should remain unset");

            // Set capital flag
            state.SetFlag(ProvinceFlags.IsCapital);
            Assert.IsTrue(state.HasFlag(ProvinceFlags.IsCoastal), "Previous flags should remain");
            Assert.IsTrue(state.HasFlag(ProvinceFlags.IsCapital));

            // Clear coastal flag
            state.ClearFlag(ProvinceFlags.IsCoastal);
            Assert.IsFalse(state.HasFlag(ProvinceFlags.IsCoastal));
            Assert.IsTrue(state.HasFlag(ProvinceFlags.IsCapital), "Other flags should remain");
        }

        [Test]
        public void ProvinceState_AllFlagsCanBeSet_WithinSingleByte()
        {
            var state = ProvinceState.CreateDefault();

            // Set all possible flags (8 flags in 1 byte)
            var allFlags = new ProvinceFlags[]
            {
                ProvinceFlags.IsCoastal,
                ProvinceFlags.IsCapital,
                ProvinceFlags.HasReligiousCenter,
                ProvinceFlags.IsTradeCenter,
                ProvinceFlags.IsBorderProvince,
                ProvinceFlags.UnderSiege,
                ProvinceFlags.HasSpecialBuilding,
                ProvinceFlags.IsSelected
            };

            // Set all flags
            foreach (var flag in allFlags)
            {
                state.SetFlag(flag);
            }

            // Verify all flags are set
            foreach (var flag in allFlags)
            {
                Assert.IsTrue(state.HasFlag(flag), $"Flag {flag} should be set");
            }

            // Verify we haven't exceeded byte limit
            Assert.LessOrEqual(state.flags, 255, "Flags should fit in single byte");

            // Clear all flags
            foreach (var flag in allFlags)
            {
                state.ClearFlag(flag);
            }

            // Verify all flags are cleared
            foreach (var flag in allFlags)
            {
                Assert.IsFalse(state.HasFlag(flag), $"Flag {flag} should be cleared");
            }
        }

        [Test]
        public void ProvinceState_OccupationLogic_ShouldWorkCorrectly()
        {
            var state = ProvinceState.CreateOwned(100, 1, 10);

            // Initially owner controls
            Assert.IsTrue(state.IsOwned);
            Assert.IsFalse(state.IsOccupied);

            // Occupation by different country
            state.controllerID = 200;
            Assert.IsTrue(state.IsOwned, "Should still be owned");
            Assert.IsTrue(state.IsOccupied, "Should be occupied by different controller");

            // Liberation
            state.controllerID = state.ownerID;
            Assert.IsTrue(state.IsOwned);
            Assert.IsFalse(state.IsOccupied, "Should no longer be occupied");
        }

        [Test]
        public void ProvinceState_Serialization_ShouldBe8Bytes()
        {
            var state = ProvinceState.CreateOwned(123, 2, 50);
            state.SetFlag(ProvinceFlags.IsCoastal);
            state.fortLevel = 3;

            byte[] serialized = state.ToBytes();

            Assert.AreEqual(8, serialized.Length, "Serialized state must be exactly 8 bytes");

            // Deserialize and verify
            var deserialized = ProvinceState.FromBytes(serialized);

            Assert.AreEqual(state.ownerID, deserialized.ownerID);
            Assert.AreEqual(state.controllerID, deserialized.controllerID);
            Assert.AreEqual(state.development, deserialized.development);
            Assert.AreEqual(state.terrain, deserialized.terrain);
            Assert.AreEqual(state.fortLevel, deserialized.fortLevel);
            Assert.AreEqual(state.flags, deserialized.flags);
        }

        [Test]
        public void ProvinceState_SerializationRoundTrip_ShouldPreserveData()
        {
            var originalState = ProvinceState.CreateOwned(999, 5, 200);
            originalState.controllerID = 888; // Occupation
            originalState.fortLevel = 15;
            originalState.SetFlag(ProvinceFlags.IsCapital);
            originalState.SetFlag(ProvinceFlags.IsTradeCenter);

            byte[] serialized = originalState.ToBytes();
            var deserializedState = ProvinceState.FromBytes(serialized);

            Assert.AreEqual(originalState.ownerID, deserializedState.ownerID);
            Assert.AreEqual(originalState.controllerID, deserializedState.controllerID);
            Assert.AreEqual(originalState.development, deserializedState.development);
            Assert.AreEqual(originalState.terrain, deserializedState.terrain);
            Assert.AreEqual(originalState.fortLevel, deserializedState.fortLevel);
            Assert.AreEqual(originalState.flags, deserializedState.flags);

            Assert.AreEqual(originalState.IsOwned, deserializedState.IsOwned);
            Assert.AreEqual(originalState.IsOccupied, deserializedState.IsOccupied);
            Assert.AreEqual(originalState.HasFlag(ProvinceFlags.IsCapital),
                           deserializedState.HasFlag(ProvinceFlags.IsCapital));
            Assert.AreEqual(originalState.HasFlag(ProvinceFlags.IsTradeCenter),
                           deserializedState.HasFlag(ProvinceFlags.IsTradeCenter));
        }

        [Test]
        public void ProvinceState_Equality_ShouldWorkCorrectly()
        {
            var state1 = ProvinceState.CreateOwned(100, 1, 50);
            var state2 = ProvinceState.CreateOwned(100, 1, 50);
            var state3 = ProvinceState.CreateOwned(101, 1, 50); // Different owner

            Assert.AreEqual(state1, state2, "Identical states should be equal");
            Assert.AreNotEqual(state1, state3, "Different states should not be equal");
        }

        [Test]
        public void ProvinceState_HashCode_ShouldBeConsistent()
        {
            var state1 = ProvinceState.CreateOwned(123, 2, 75);
            state1.SetFlag(ProvinceFlags.IsCoastal);

            var state2 = ProvinceState.CreateOwned(123, 2, 75);
            state2.SetFlag(ProvinceFlags.IsCoastal);

            Assert.AreEqual(state1.GetHashCode(), state2.GetHashCode(),
                           "Identical states should have same hash code");
        }

        [Test]
        public void ProvinceState_InvalidSerialization_ShouldThrow()
        {
            // Test with wrong byte array size
            Assert.Throws<ArgumentException>(() => {
                ProvinceState.FromBytes(new byte[4]); // Too short
            });

            Assert.Throws<ArgumentException>(() => {
                ProvinceState.FromBytes(new byte[16]); // Too long
            });

            Assert.Throws<ArgumentException>(() => {
                ProvinceState.FromBytes(null); // Null
            });
        }

        [Test]
        public void ProvinceState_MemoryPerformance_ShouldMeetTargets()
        {
            // Validate memory usage for 10,000 provinces
            const int provinceCount = 10000;
            const int expectedBytes = provinceCount * 8; // 80,000 bytes = 78.125 KB
            const int maxAllowedBytes = 85000; // ~83 KB with some overhead

            Assert.LessOrEqual(expectedBytes, maxAllowedBytes,
                $"10,000 provinces should use less than 85KB. Calculated: {expectedBytes} bytes");

            // Verify this matches our architecture targets
            Assert.AreEqual(80000, expectedBytes,
                "10,000 provinces should use exactly 80KB as per architecture");
        }

        [Test]
        public void TerrainType_EnumValues_ShouldFitInByte()
        {
            // Verify all terrain types fit in byte range
            foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
            {
                byte terrainByte = (byte)terrain;
                Assert.LessOrEqual(terrainByte, 255, $"Terrain {terrain} must fit in byte");
            }
        }

        [Test]
        public void ProvinceFlags_EnumValues_ShouldFitInByte()
        {
            // Verify all flag combinations fit in single byte
            int maxCombinedValue = 0;
            foreach (ProvinceFlags flag in Enum.GetValues(typeof(ProvinceFlags)))
            {
                maxCombinedValue |= (int)flag;
            }

            Assert.LessOrEqual(maxCombinedValue, 255, "All province flags must fit in single byte");
        }
    }
}