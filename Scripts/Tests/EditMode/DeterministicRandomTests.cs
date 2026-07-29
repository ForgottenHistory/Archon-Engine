using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Core.Data;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for DeterministicRandom - the xorshift generator behind every random
    /// decision in the simulation.
    ///
    /// WHY THIS MATTERS: two clients that draw different numbers desync. Determinism is
    /// not a quality-of-results question, it is a correctness question, so the golden
    /// sequence tests below are as important as the distribution ones.
    ///
    /// Chi-square thresholds are 99.9% critical values, so a correct generator fails
    /// roughly 1 run in 1000. All seeds are fixed to make that deterministic too.
    /// </summary>
    public class DeterministicRandomTests
    {
        // ===== Determinism =====

        /// <summary>
        /// Golden values. These pin the exact output of the xorshift step and the
        /// splitmix64 seeding. Any change to either breaks multiplayer sync and
        /// invalidates existing replays, so it must be a deliberate decision - if this
        /// test fails, do not update the constants without understanding why.
        /// </summary>
        [Test]
        public void NextUInt_ProducesKnownSequence_ForFixedSeed()
        {
            var random = new DeterministicRandom(12345);

            var expected = new uint[]
            {
                3373659176u, 3065806448u, 1040264889u, 2480955881u,
                4113724169u, 2687032353u, 2540395131u, 118787670u
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], random.NextUInt(),
                    $"Sequence diverged at index {i} for seed 12345");
            }
        }

        [Test]
        public void NextUInt_ProducesKnownSequence_ForSeedOne()
        {
            var random = new DeterministicRandom(1);

            var expected = new uint[]
            {
                1116857086u, 4246686243u, 4238700765u, 1470771942u, 2598481151u
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], random.NextUInt(),
                    $"Sequence diverged at index {i} for seed 1");
            }
        }

        [Test]
        public void SameSeed_ProducesIdenticalSequences()
        {
            var a = new DeterministicRandom(9876);
            var b = new DeterministicRandom(9876);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(),
                    $"Two generators with the same seed diverged at draw {i}");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);

            bool anyDifference = false;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.IsTrue(anyDifference, "Different seeds must not produce identical streams");
        }

        [Test]
        public void State_RoundTripsThroughSaveAndRestore()
        {
            var original = new DeterministicRandom(555);
            for (int i = 0; i < 50; i++) original.NextUInt();

            uint4 savedState = original.State;
            var expected = new uint[10];
            for (int i = 0; i < 10; i++) expected[i] = original.NextUInt();

            var restored = new DeterministicRandom(777);
            restored.State = savedState;

            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(expected[i], restored.NextUInt(),
                    $"Restoring saved state must resume the identical stream; diverged at {i}");
            }
        }

        [Test]
        public void AllZeroState_IsRejected()
        {
            var random = new DeterministicRandom(new uint4(0, 0, 0, 0));

            Assert.IsFalse(math.all(random.State == 0),
                "An all-zero state makes xorshift produce only zeros forever and must be replaced");
        }

        [Test]
        public void SetSeed_ResetsToSameSequence()
        {
            var random = new DeterministicRandom(100);
            var first = new uint[5];
            for (int i = 0; i < 5; i++) first[i] = random.NextUInt();

            random.SetSeed(100);

            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(first[i], random.NextUInt(),
                    "SetSeed must restart the identical sequence");
            }
        }

        [Test]
        public void Branch_ProducesIndependentSequence()
        {
            var parent = new DeterministicRandom(42);
            var branched = parent.Branch(1);

            var parentDraws = new uint[10];
            var branchDraws = new uint[10];
            for (int i = 0; i < 10; i++)
            {
                parentDraws[i] = parent.NextUInt();
                branchDraws[i] = branched.NextUInt();
            }

            bool anyDifference = false;
            for (int i = 0; i < 10; i++)
                if (parentDraws[i] != branchDraws[i]) anyDifference = true;

            Assert.IsTrue(anyDifference,
                "A branched generator must not replay the parent's sequence");
        }

        [Test]
        public void Branch_IsDeterministic()
        {
            var a = new DeterministicRandom(42).Branch(3);
            var b = new DeterministicRandom(42).Branch(3);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(),
                    "Branching with the same offset must be reproducible");
            }
        }

        [Test]
        public void HasSameState_DetectsDivergence()
        {
            var a = new DeterministicRandom(7);
            var b = new DeterministicRandom(7);

            Assert.IsTrue(a.HasSameState(b), "Fresh generators with equal seeds must match");

            a.NextUInt();

            Assert.IsFalse(a.HasSameState(b),
                "Advancing one generator must make the desync detectable");
        }

        // ===== NextFixed range (F2 regression) =====

        /// <summary>
        /// REGRESSION: NextFixed shifted the random word right by 1 instead of 16.
        /// FixedPoint32 is 16.16, so that produced values up to ~32768 rather than the
        /// documented [0, 1).
        ///
        /// Everything downstream broke silently: NextBool(probability) ignored its
        /// argument, weighted selection always returned the last element, and
        /// NextPointInCircle's rejection loop could effectively never accept - a hang.
        /// </summary>
        [Test]
        public void NextFixed_StaysWithinUnitInterval()
        {
            var random = new DeterministicRandom(2024);

            for (int i = 0; i < 100000; i++)
            {
                var value = random.NextFixed();

                Assert.GreaterOrEqual(value.RawValue, 0,
                    "NextFixed must never be negative");
                Assert.Less(value.RawValue, FixedPoint32.One.RawValue,
                    $"NextFixed must be below 1.0; got {value.ToFloat()} at draw {i}. " +
                    "A value in the thousands means the shift regression has returned.");
            }
        }

        /// <summary>
        /// 10 equal buckets over 1M draws. Chi-square with 9 dof, 99.9% critical value
        /// 27.88. Measured 9.09 for the current generator.
        /// </summary>
        [Test]
        public void NextFixed_IsUniformlyDistributed()
        {
            var random = new DeterministicRandom(777);
            const int sampleCount = 1000000;
            const int bucketCount = 10;

            var buckets = new int[bucketCount];
            for (int i = 0; i < sampleCount; i++)
            {
                long raw = random.NextFixed().RawValue;
                int bucket = (int)(raw * bucketCount / FixedPoint32.One.RawValue);
                if (bucket >= bucketCount) bucket = bucketCount - 1;
                buckets[bucket]++;
            }

            double expected = (double)sampleCount / bucketCount;
            double chiSquare = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                double delta = buckets[i] - expected;
                chiSquare += delta * delta / expected;
            }

            Assert.Less(chiSquare, 27.88,
                $"NextFixed distribution is not uniform (chi-square {chiSquare:F2})");
        }

        [Test]
        public void NextFixed_WithMax_StaysBelowMax()
        {
            var random = new DeterministicRandom(4321);
            var max = FixedPoint32.FromInt(10);

            for (int i = 0; i < 50000; i++)
            {
                var value = random.NextFixed(max);

                Assert.GreaterOrEqual(value.RawValue, 0);
                Assert.Less(value.RawValue, max.RawValue,
                    $"NextFixed(max) must stay below max; got {value.ToFloat()}");
            }
        }

        [Test]
        public void NextFixed_WithRange_StaysWithinRange()
        {
            var random = new DeterministicRandom(1111);
            var min = FixedPoint32.FromInt(-5);
            var max = FixedPoint32.FromInt(5);

            for (int i = 0; i < 50000; i++)
            {
                var value = random.NextFixed(min, max);

                Assert.GreaterOrEqual(value.RawValue, min.RawValue,
                    $"NextFixed(min,max) fell below min; got {value.ToFloat()}");
                Assert.Less(value.RawValue, max.RawValue,
                    $"NextFixed(min,max) reached or exceeded max; got {value.ToFloat()}");
            }
        }

        [Test]
        public void NextFixed_WithInvertedRange_ReturnsMin()
        {
            var random = new DeterministicRandom(1);
            var min = FixedPoint32.FromInt(10);
            var max = FixedPoint32.FromInt(5);

            Assert.AreEqual(min.RawValue, random.NextFixed(min, max).RawValue,
                "An inverted range must return min rather than looping or throwing");
        }

        // ===== NextPointInCircle (F2 knock-on) =====

        /// <summary>
        /// REGRESSION: this uses rejection sampling against unit length. When NextFixed
        /// returned values in the thousands the condition was essentially never
        /// satisfied, so the loop did not terminate in practice.
        ///
        /// NUnit cannot fail a hung test, so a timeout guards it. Expected acceptance
        /// rate is 4/pi, about 1.27 iterations per point.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public void NextPointInCircle_TerminatesAndStaysInsideRadius()
        {
            var random = new DeterministicRandom(31337);
            var radius = FixedPoint32.FromInt(10);
            var radiusSquared = radius * radius;

            for (int i = 0; i < 2000; i++)
            {
                var point = random.NextPointInCircle(radius);

                var lengthSquared = point.x * point.x + point.y * point.y;

                Assert.LessOrEqual(lengthSquared.RawValue, radiusSquared.RawValue,
                    $"Point {i} fell outside the requested radius");
            }
        }

        // ===== NextGaussian (F3) =====

        /// <summary>
        /// Irwin-Hall sum of 12 uniforms minus 6. Measured mean -0.0069 and stddev
        /// 1.0018 over 200k samples; tolerances are loose enough to absorb sampling
        /// noise but tight enough to catch a scale error.
        /// </summary>
        [Test]
        public void NextGaussian_HasStandardNormalMoments()
        {
            var random = new DeterministicRandom(2468);
            const int sampleCount = 200000;

            double sum = 0;
            double sumSquares = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                double value = random.NextGaussian().ToFloat();
                sum += value;
                sumSquares += value * value;
            }

            double mean = sum / sampleCount;
            double stdDev = Math.Sqrt(sumSquares / sampleCount - mean * mean);

            Assert.AreEqual(0.0, mean, 0.05, $"Gaussian mean should be ~0, got {mean:F4}");
            Assert.AreEqual(1.0, stdDev, 0.05, $"Gaussian stddev should be ~1, got {stdDev:F4}");
        }

        /// <summary>
        /// The draw count is part of the determinism contract - changing it shifts the
        /// stream for every later call.
        /// </summary>
        [Test]
        public void NextGaussian_ConsumesExactlyTwelveDraws()
        {
            var gaussian = new DeterministicRandom(555);
            gaussian.NextGaussian();

            var counter = new DeterministicRandom(555);
            for (int i = 0; i < 12; i++) counter.NextUInt();

            Assert.IsTrue(gaussian.HasSameState(counter),
                "NextGaussian must consume exactly 12 draws; a mismatch means the " +
                "implementation changed and every downstream sequence has shifted");
        }

        [Test]
        public void NextGaussian_WithMeanAndStdDev_ShiftsAndScales()
        {
            var random = new DeterministicRandom(1357);
            var mean = FixedPoint32.FromInt(100);
            var stdDev = FixedPoint32.FromInt(15);
            const int sampleCount = 100000;

            double sum = 0;
            for (int i = 0; i < sampleCount; i++)
                sum += random.NextGaussian(mean, stdDev).ToFloat();

            Assert.AreEqual(100.0, sum / sampleCount, 1.0,
                "Scaled gaussian must centre on the requested mean");
        }

        // ===== Integer ranges =====

        /// <summary>
        /// 100 buckets over 1M draws. Chi-square with 99 dof, 99.9% critical value
        /// 148.23. Measured 96.80 for the current generator.
        ///
        /// This exercises the rejection-sampling path in NextUInt(max), which is what
        /// keeps modulo bias out of every percentage roll in the game.
        /// </summary>
        [Test]
        public void NextInt_IsUniformlyDistributed()
        {
            var random = new DeterministicRandom(31337);
            const int sampleCount = 1000000;
            const int bucketCount = 100;

            var buckets = new int[bucketCount];
            for (int i = 0; i < sampleCount; i++)
                buckets[random.NextInt(bucketCount)]++;

            double expected = (double)sampleCount / bucketCount;
            double chiSquare = 0;
            for (int i = 0; i < bucketCount; i++)
            {
                double delta = buckets[i] - expected;
                chiSquare += delta * delta / expected;
            }

            Assert.Less(chiSquare, 148.23,
                $"NextInt(100) shows modulo bias (chi-square {chiSquare:F2})");
        }

        /// <summary>
        /// Small moduli are where modulo bias is most visible. 6 buckets, 5 dof,
        /// 99.9% critical value 20.52. Measured 4.12.
        /// </summary>
        [Test]
        public void NextInt_SmallRange_IsUnbiased()
        {
            var random = new DeterministicRandom(999);
            const int sampleCount = 600000;
            const int sides = 6;

            var buckets = new int[sides];
            for (int i = 0; i < sampleCount; i++)
                buckets[random.NextInt(sides)]++;

            double expected = (double)sampleCount / sides;
            double chiSquare = 0;
            for (int i = 0; i < sides; i++)
            {
                double delta = buckets[i] - expected;
                chiSquare += delta * delta / expected;
            }

            Assert.Less(chiSquare, 20.52,
                $"NextInt(6) shows modulo bias (chi-square {chiSquare:F2})");
        }

        [Test]
        public void NextInt_WithRange_ObservesBothBounds()
        {
            var random = new DeterministicRandom(555);
            int observedMin = int.MaxValue;
            int observedMax = int.MinValue;

            for (int i = 0; i < 200000; i++)
            {
                int value = random.NextInt(10, 20);
                observedMin = Math.Min(observedMin, value);
                observedMax = Math.Max(observedMax, value);
            }

            Assert.AreEqual(10, observedMin, "min must be inclusive");
            Assert.AreEqual(19, observedMax, "max must be exclusive");
        }

        [Test]
        public void NextInt_WithNonPositiveMax_ReturnsZero()
        {
            var random = new DeterministicRandom(1);

            Assert.AreEqual(0, random.NextInt(0));
            Assert.AreEqual(0, random.NextInt(-5));
        }

        [Test]
        public void NextInt_WithInvertedRange_ReturnsMin()
        {
            var random = new DeterministicRandom(1);

            Assert.AreEqual(20, random.NextInt(20, 10),
                "An inverted range must return min rather than looping or throwing");
        }

        [Test]
        public void NextUInt_WithMaxOfOne_ReturnsZero()
        {
            var random = new DeterministicRandom(1);

            Assert.AreEqual(0u, random.NextUInt(1),
                "max of 1 leaves only 0 as a valid result");
        }

        // ===== Booleans =====

        [Test]
        public void NextBool_IsBalanced()
        {
            var random = new DeterministicRandom(864);
            const int sampleCount = 200000;

            int trueCount = 0;
            for (int i = 0; i < sampleCount; i++)
                if (random.NextBool()) trueCount++;

            double ratio = (double)trueCount / sampleCount;

            Assert.AreEqual(0.5, ratio, 0.01, $"NextBool should be ~50/50, got {ratio:P2}");
        }

        /// <summary>
        /// Depends directly on NextFixed being in [0, 1) - with the F2 regression this
        /// returned false essentially always, silently disabling every probabilistic
        /// branch in the game.
        /// </summary>
        [Test]
        public void NextBool_WithProbability_RespectsThatProbability()
        {
            var cases = new[] { 0.25f, 0.5f, 0.75f };

            foreach (var probability in cases)
            {
                var random = new DeterministicRandom(1234);
                const int sampleCount = 200000;

                int trueCount = 0;
                for (int i = 0; i < sampleCount; i++)
                    if (random.NextBool(FixedPoint32.FromFloat(probability))) trueCount++;

                double ratio = (double)trueCount / sampleCount;

                Assert.AreEqual(probability, ratio, 0.01,
                    $"NextBool({probability}) produced {ratio:P2}");
            }
        }

        [Test]
        public void NextPercent_MatchesRequestedRate()
        {
            var random = new DeterministicRandom(88);
            const int sampleCount = 200000;

            int hits = 0;
            for (int i = 0; i < sampleCount; i++)
                if (random.NextPercent(30)) hits++;

            Assert.AreEqual(0.30, (double)hits / sampleCount, 0.01,
                "NextPercent(30) should hit about 30% of the time");
        }

        [Test]
        public void NextPercent_AtBoundaries_IsAlwaysOrNever()
        {
            var random = new DeterministicRandom(1);

            for (int i = 0; i < 1000; i++)
            {
                Assert.IsFalse(random.NextPercent(0), "0% must never hit");
                Assert.IsTrue(random.NextPercent(100), "100% must always hit");
                Assert.IsFalse(random.NextPercent(-10), "Negative must never hit");
                Assert.IsTrue(random.NextPercent(150), "Above 100 must always hit");
            }
        }

        // ===== Shuffle =====

        [Test]
        public void Shuffle_PreservesAllElements()
        {
            var random = new DeterministicRandom(2024);
            var array = new int[52];
            for (int i = 0; i < array.Length; i++) array[i] = i;

            random.Shuffle(array);

            Array.Sort(array);
            for (int i = 0; i < array.Length; i++)
            {
                Assert.AreEqual(i, array[i],
                    "Shuffle must be a permutation - no element may be lost or duplicated");
            }
        }

        [Test]
        public void Shuffle_ActuallyReorders()
        {
            var random = new DeterministicRandom(2024);
            var array = new int[52];
            for (int i = 0; i < array.Length; i++) array[i] = i;

            random.Shuffle(array);

            int fixedPoints = 0;
            for (int i = 0; i < array.Length; i++)
                if (array[i] == i) fixedPoints++;

            // A random permutation of 52 has ~1 fixed point on average; 52 would mean
            // the shuffle did nothing.
            Assert.Less(fixedPoints, 10,
                $"Shuffle left {fixedPoints} elements in place - it is not shuffling");
        }

        [Test]
        public void Shuffle_IsDeterministic()
        {
            var a = new DeterministicRandom(555);
            var b = new DeterministicRandom(555);

            var arrayA = new int[100];
            var arrayB = new int[100];
            for (int i = 0; i < 100; i++) { arrayA[i] = i; arrayB[i] = i; }

            a.Shuffle(arrayA);
            b.Shuffle(arrayB);

            CollectionAssert.AreEqual(arrayA, arrayB,
                "The same seed must produce the same shuffle on every client");
        }

        [Test]
        public void Shuffle_HandlesTrivialArrays()
        {
            var random = new DeterministicRandom(1);

            Assert.DoesNotThrow(() => random.Shuffle(new int[0]));
            Assert.DoesNotThrow(() => random.Shuffle(new int[1]));
            Assert.DoesNotThrow(() => random.Shuffle((int[])null));
        }

        [Test]
        public void Shuffle_OnNativeArray_PreservesAllElements()
        {
            var random = new DeterministicRandom(4242);
            var array = new NativeArray<int>(64, Allocator.Temp);

            try
            {
                for (int i = 0; i < array.Length; i++) array[i] = i;

                random.Shuffle(array);

                var copy = array.ToArray();
                Array.Sort(copy);
                for (int i = 0; i < copy.Length; i++)
                {
                    Assert.AreEqual(i, copy[i], "NativeArray shuffle must be a permutation");
                }
            }
            finally
            {
                array.Dispose();
            }
        }

        // ===== Weighted selection =====

        /// <summary>
        /// Weights 1:2:7 over 100k draws. Also exercises the cumulative walk, which the
        /// F2 regression broke by making the roll overshoot the total so that the final
        /// element was always returned.
        /// </summary>
        [Test]
        public void NextWeightedIndex_RespectsWeights()
        {
            var random = new DeterministicRandom(13579);
            var weights = new[] { 10, 20, 70 };
            const int sampleCount = 100000;

            var counts = new int[3];
            for (int i = 0; i < sampleCount; i++)
                counts[random.NextWeightedIndex(weights)]++;

            Assert.AreEqual(0.10, (double)counts[0] / sampleCount, 0.01, "weight 10 bucket");
            Assert.AreEqual(0.20, (double)counts[1] / sampleCount, 0.01, "weight 20 bucket");
            Assert.AreEqual(0.70, (double)counts[2] / sampleCount, 0.01, "weight 70 bucket");
        }

        [Test]
        public void NextWeightedIndex_WithZeroWeight_NeverSelectsThatIndex()
        {
            var random = new DeterministicRandom(2222);
            var weights = new[] { 50, 0, 50 };

            for (int i = 0; i < 20000; i++)
            {
                Assert.AreNotEqual(1, random.NextWeightedIndex(weights),
                    "An index with weight 0 must never be selected");
            }
        }

        [Test]
        public void NextWeightedIndex_WithAllZeroWeights_FallsBackToUniform()
        {
            var random = new DeterministicRandom(3333);
            var weights = new[] { 0, 0, 0, 0 };

            var counts = new int[4];
            for (int i = 0; i < 40000; i++)
                counts[random.NextWeightedIndex(weights)]++;

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(0.25, (double)counts[i] / 40000, 0.02,
                    "All-zero weights must fall back to a uniform choice");
            }
        }

        [Test]
        public void NextWeightedIndex_WithNegativeWeight_Throws()
        {
            var random = new DeterministicRandom(1);

            Assert.Throws<ArgumentException>(
                () => random.NextWeightedIndex(new[] { 10, -5, 10 }));
        }

        [Test]
        public void NextWeightedElement_RespectsWeights()
        {
            var random = new DeterministicRandom(24680);
            var elements = new[] { 100, 200, 300 };
            var weights = new[] { 10, 10, 80 };
            const int sampleCount = 100000;

            int lastCount = 0;
            for (int i = 0; i < sampleCount; i++)
                if (random.NextWeightedElement(elements, weights) == 300) lastCount++;

            Assert.AreEqual(0.80, (double)lastCount / sampleCount, 0.01,
                "The heavily weighted element should dominate");
        }

        [Test]
        public void NextWeightedElement_WithMismatchedArrays_Throws()
        {
            var random = new DeterministicRandom(1);

            Assert.Throws<ArgumentException>(
                () => random.NextWeightedElement(new[] { 1, 2, 3 }, new[] { 1, 2 }));
        }

        [Test]
        public void NextWeightedElement_WithEmptyArray_Throws()
        {
            var random = new DeterministicRandom(1);

            Assert.Throws<ArgumentException>(
                () => random.NextWeightedElement(new int[0], new int[0]));
        }

        // ===== Element selection =====

        [Test]
        public void NextElement_StaysWithinArray()
        {
            var random = new DeterministicRandom(1717);
            var array = new[] { 5, 10, 15, 20 };

            for (int i = 0; i < 10000; i++)
            {
                Assert.Contains(random.NextElement(array), array,
                    "NextElement must return a member of the array");
            }
        }

        [Test]
        public void NextElement_WithEmptyArray_Throws()
        {
            var random = new DeterministicRandom(1);

            Assert.Throws<ArgumentException>(() => random.NextElement(new int[0]));
        }

        [Test]
        public void NextElementExcept_AvoidsExcludedValue()
        {
            var random = new DeterministicRandom(8888);
            var array = new[] { 1, 2, 3, 4, 5 };

            for (int i = 0; i < 10000; i++)
            {
                Assert.AreNotEqual(3, random.NextElementExcept(array, 3),
                    "The excluded value must not be returned when alternatives exist");
            }
        }

        [Test]
        public void NextElementExcept_WithSingleElement_ReturnsItAnyway()
        {
            var random = new DeterministicRandom(1);
            var array = new[] { 42 };

            Assert.AreEqual(42, random.NextElementExcept(array, 42),
                "With no alternative the only element is returned even if excluded");
        }

        [Test]
        public void NextElementExceptIndices_SkipsExcludedIndices()
        {
            var random = new DeterministicRandom(6543);
            var array = new[] { 10, 20, 30, 40, 50 };

            for (int i = 0; i < 10000; i++)
            {
                int value = random.NextElementExceptIndices(array, 0, 2);

                Assert.AreNotEqual(10, value, "index 0 was excluded");
                Assert.AreNotEqual(30, value, "index 2 was excluded");
            }
        }

        [Test]
        public void NextElementExceptIndices_ExcludingEverything_Throws()
        {
            var random = new DeterministicRandom(1);

            Assert.Throws<ArgumentException>(
                () => random.NextElementExceptIndices(new[] { 1, 2 }, 0, 1));
        }

        // ===== Seed phrase =====

        [Test]
        public void SeedPhrase_HasEightWords()
        {
            var random = new DeterministicRandom(4444);

            Assert.AreEqual(8, random.ToSeedPhrase().Split('-').Length);
        }

        [Test]
        public void FromSeedPhrase_IsDeterministic()
        {
            var a = DeterministicRandom.FromSeedPhrase("alpha-brave-crown-delta-eagle-flame-glory-haven");
            var b = DeterministicRandom.FromSeedPhrase("alpha-brave-crown-delta-eagle-flame-glory-haven");

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(),
                    "The same phrase must always yield the same stream");
            }
        }

        [Test]
        public void FromSeedPhrase_IsCaseInsensitive()
        {
            var lower = DeterministicRandom.FromSeedPhrase("alpha-brave-crown-delta-eagle-flame-glory-haven");
            var upper = DeterministicRandom.FromSeedPhrase("ALPHA-BRAVE-CROWN-DELTA-EAGLE-FLAME-GLORY-HAVEN");

            Assert.IsTrue(lower.HasSameState(upper), "Phrase casing must not change the result");
        }

        [Test]
        public void FromSeedPhrase_WithEmptyInput_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => DeterministicRandom.FromSeedPhrase(""));
            Assert.DoesNotThrow(() => DeterministicRandom.FromSeedPhrase(null));
        }

        [Test]
        public void FromSeedPhrase_WithShortPhrase_FallsBackToHash()
        {
            var a = DeterministicRandom.FromSeedPhrase("alpha-brave");
            var b = DeterministicRandom.FromSeedPhrase("alpha-brave");

            Assert.IsTrue(a.HasSameState(b),
                "A short phrase must still hash deterministically");
        }
    }
}
