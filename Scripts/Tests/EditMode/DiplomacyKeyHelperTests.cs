using NUnit.Framework;
using Core.Diplomacy;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for DiplomacyKeyHelper - packs a country pair into one relationship key.
    ///
    /// WHY THIS MATTERS: the key is the identity of a relationship. If GetKey is not
    /// order-independent, A-to-B and B-to-A become two separate relations that drift
    /// apart. If UnpackKey does not invert GetKey, iteration over the relation map
    /// attributes wars and treaties to the wrong countries.
    /// </summary>
    public class DiplomacyKeyHelperTests
    {
        // ===== Normalisation =====

        [Test]
        public void GetKey_IsOrderIndependent()
        {
            Assert.AreEqual(DiplomacyKeyHelper.GetKey(5, 10), DiplomacyKeyHelper.GetKey(10, 5),
                "A-to-B and B-to-A must resolve to the same relationship");
        }

        [Test]
        public void GetKey_IsOrderIndependent_AcrossManyPairs()
        {
            for (ushort a = 0; a < 40; a++)
            {
                for (ushort b = 0; b < 40; b++)
                {
                    Assert.AreEqual(DiplomacyKeyHelper.GetKey(a, b), DiplomacyKeyHelper.GetKey(b, a),
                        $"Key for ({a},{b}) differs from ({b},{a})");
                }
            }
        }

        [Test]
        public void UnpackKey_AlwaysReturnsAscendingOrder()
        {
            var (low, high) = DiplomacyKeyHelper.UnpackKey(DiplomacyKeyHelper.GetKey(200, 7));

            Assert.AreEqual(7, low, "The smaller ID must come first");
            Assert.AreEqual(200, high, "The larger ID must come second");
        }

        // ===== Round-trip =====

        /// <summary>
        /// The contract that matters for map iteration: unpacking a packed pair returns
        /// the same two countries, normalised.
        /// </summary>
        [Test]
        public void PackThenUnpack_RecoversBothCountries()
        {
            ushort[] ids = { 0, 1, 2, 255, 256, 1000, 30000, 65534, 65535 };

            foreach (var a in ids)
            {
                foreach (var b in ids)
                {
                    var (low, high) = DiplomacyKeyHelper.UnpackKey(DiplomacyKeyHelper.GetKey(a, b));

                    ushort expectedLow = a < b ? a : b;
                    ushort expectedHigh = a < b ? b : a;

                    Assert.AreEqual(expectedLow, low, $"Low half wrong for ({a},{b})");
                    Assert.AreEqual(expectedHigh, high, $"High half wrong for ({a},{b})");
                }
            }
        }

        /// <summary>
        /// UnpackKey masks the low half with 0xFFFFFFFF (32 bits) then casts to ushort
        /// (16 bits), while GetKey only ever places 16 meaningful bits there. The wider
        /// mask is harmless today but the asymmetry would bite if the ID type widened.
        /// The boundary values here are what would break first.
        /// </summary>
        [Test]
        public void PackThenUnpack_HandlesMaximumIds()
        {
            var (low, high) = DiplomacyKeyHelper.UnpackKey(
                DiplomacyKeyHelper.GetKey(ushort.MaxValue, ushort.MaxValue - 1));

            Assert.AreEqual(ushort.MaxValue - 1, low);
            Assert.AreEqual(ushort.MaxValue, high);
        }

        [Test]
        public void PackThenUnpack_HandlesIdenticalIds()
        {
            var (low, high) = DiplomacyKeyHelper.UnpackKey(DiplomacyKeyHelper.GetKey(42, 42));

            Assert.AreEqual(42, low);
            Assert.AreEqual(42, high);
        }

        [Test]
        public void PackThenUnpack_HandlesZero()
        {
            var (low, high) = DiplomacyKeyHelper.UnpackKey(DiplomacyKeyHelper.GetKey(0, 99));

            Assert.AreEqual(0, low);
            Assert.AreEqual(99, high);
        }

        // ===== Uniqueness =====

        /// <summary>
        /// Distinct unordered pairs must not collide - a collision would silently merge
        /// two unrelated relationships.
        /// </summary>
        [Test]
        public void DistinctPairs_ProduceDistinctKeys()
        {
            var seen = new System.Collections.Generic.Dictionary<ulong, string>();

            for (ushort a = 0; a < 80; a++)
            {
                for (ushort b = a; b < 80; b++)
                {
                    ulong key = DiplomacyKeyHelper.GetKey(a, b);
                    string pair = $"({a},{b})";

                    if (seen.TryGetValue(key, out var existing))
                    {
                        Assert.AreEqual(existing, pair,
                            $"Key collision: {existing} and {pair} share key {key}");
                    }
                    else
                    {
                        seen[key] = pair;
                    }
                }
            }
        }

        [Test]
        public void SwappedPairs_DoNotCreateSeparateEntries()
        {
            var forward = DiplomacyKeyHelper.GetKey(3, 9);
            var reverse = DiplomacyKeyHelper.GetKey(9, 3);

            Assert.AreEqual(forward, reverse,
                "Order must never produce a second key for the same relationship");
        }
    }
}
