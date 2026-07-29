using System;
using System.Numerics;
using NUnit.Framework;
using Core.Data;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for FixedPoint32 - the 16.16 deterministic arithmetic type.
    ///
    /// WHY THIS MATTERS: only 16 fractional bits, so precision loss shows up early, and
    /// it backs DeterministicRandom's uniform values - an error here biases every random
    /// draw in the simulation.
    ///
    /// Reference values use BigInteger rather than double, which carries its own
    /// rounding and would mask the errors we care about.
    /// </summary>
    public class FixedPoint32Tests
    {
        private const int FractionalBits = 16;
        private const int OneRaw = 1 << FractionalBits;

        /// <summary>Exact reference: floor(a * b / 2^16) in arbitrary precision.</summary>
        private static int ExactMultiply(int rawA, int rawB)
        {
            return (int)(((BigInteger)rawA * rawB) >> FractionalBits);
        }

        /// <summary>Exact reference: floor(a * 2^16 / b) in arbitrary precision.</summary>
        private static int ExactDivide(int rawA, int rawB)
        {
            return (int)(((BigInteger)rawA << FractionalBits) / rawB);
        }

        // ===== Frac / Floor invariant =====

        /// <summary>
        /// REGRESSION: a "correction" branch for negatives (ONE_RAW - fractional)
        /// inverted a value the two's-complement mask already got right, breaking
        /// Floor(x) + Frac(x) == x for every negative non-integer. Frac(-0.25) returned
        /// 0.25, giving -0.75 instead of -0.25.
        /// </summary>
        [Test]
        public void Frac_NegativeNonIntegers_ReconstructsValueWithFloor()
        {
            // raw, expected Frac raw
            var cases = new[]
            {
                (-16384, 49152),  // -0.25 -> frac 0.75  (floor is -1.0)
                (-81920, 49152),  // -1.25 -> frac 0.75  (floor is -2.0)
                (-49152, 16384),  // -0.75 -> frac 0.25  (floor is -1.0)
                (16384, 16384),   //  0.25 -> frac 0.25  (floor is  0.0)
            };

            foreach (var (raw, expectedFracRaw) in cases)
            {
                var value = FixedPoint32.FromRaw(raw);

                Assert.AreEqual(expectedFracRaw, FixedPoint32.Frac(value).RawValue,
                    $"Frac(raw {raw}) must be consistent with floor-toward-negative-infinity. " +
                    "A result of ONE_RAW - expected means the negative correction branch has returned.");
            }
        }

        /// <summary>
        /// The invariant that actually matters: Floor(x) + Frac(x) == x, exactly, in raw
        /// units. Swept across negatives and positives with a stride that is coprime to
        /// 2^16 so it lands on many distinct fractional parts.
        /// </summary>
        [Test]
        public void Frac_PlusFloor_ReconstructsOriginal_AcrossRange()
        {
            for (int raw = -500000; raw <= 500000; raw += 37)
            {
                var value = FixedPoint32.FromRaw(raw);

                int reconstructed = FixedPoint32.Floor(value).RawValue + FixedPoint32.Frac(value).RawValue;

                Assert.AreEqual(raw, reconstructed,
                    $"Floor(x) + Frac(x) must equal x exactly; failed at raw {raw}");
            }
        }

        [Test]
        public void Frac_IsAlwaysInUnitInterval()
        {
            for (int raw = -500000; raw <= 500000; raw += 37)
            {
                int frac = FixedPoint32.Frac(FixedPoint32.FromRaw(raw)).RawValue;

                Assert.IsTrue(frac >= 0 && frac < OneRaw,
                    $"Frac must lie in [0, 1); got raw {frac} for input raw {raw}");
            }
        }

        [Test]
        public void Floor_RoundsTowardNegativeInfinity()
        {
            var cases = new[]
            {
                (-16384, -OneRaw),      // -0.25 -> -1
                (-81920, -2 * OneRaw),  // -1.25 -> -2
                (-OneRaw, -OneRaw),     // -1.0  -> -1 (already integral)
                (16384, 0),             //  0.25 ->  0
                (98304, OneRaw),        //  1.5  ->  1
            };

            foreach (var (raw, expected) in cases)
            {
                Assert.AreEqual(expected, FixedPoint32.Floor(FixedPoint32.FromRaw(raw)).RawValue,
                    $"Floor(raw {raw}) must round toward negative infinity");
            }
        }

        [Test]
        public void Ceiling_RoundsTowardPositiveInfinity()
        {
            var cases = new[]
            {
                (-16384, 0),            // -0.25 ->  0
                (-81920, -OneRaw),      // -1.25 -> -1
                (-OneRaw, -OneRaw),     // -1.0  -> -1 (already integral)
                (16384, OneRaw),        //  0.25 ->  1
                (98304, 2 * OneRaw),    //  1.5  ->  2
            };

            foreach (var (raw, expected) in cases)
            {
                Assert.AreEqual(expected, FixedPoint32.Ceiling(FixedPoint32.FromRaw(raw)).RawValue,
                    $"Ceiling(raw {raw}) must round toward positive infinity");
            }
        }

        // ===== Multiplication =====

        [Test]
        public void Multiply_SignCombinations_MatchExactArithmetic()
        {
            var operands = new[] { -7.25f, -3.125f, -1.5f, -0.25f, 0.25f, 1.5f, 3.125f, 7.25f };

            foreach (var x in operands)
            {
                foreach (var y in operands)
                {
                    var a = FixedPoint32.FromFloat(x);
                    var b = FixedPoint32.FromFloat(y);

                    Assert.AreEqual(ExactMultiply(a.RawValue, b.RawValue), (a * b).RawValue,
                        $"{x} * {y} diverged from exact arithmetic");
                }
            }
        }

        /// <summary>
        /// Randomised sweep, seeded for reproducibility. Operands are kept well inside
        /// the 16.16 range so the exact product cannot overflow int - this test is about
        /// correctness of the limb math, not saturation behaviour.
        /// </summary>
        [Test]
        public void Multiply_RandomOperands_MatchExactArithmetic()
        {
            var random = new Random(20260729);

            for (int i = 0; i < 20000; i++)
            {
                int rawA = (int)((random.NextDouble() * 200.0 - 100.0) * OneRaw);
                int rawB = (int)((random.NextDouble() * 200.0 - 100.0) * OneRaw);

                var result = FixedPoint32.FromRaw(rawA) * FixedPoint32.FromRaw(rawB);

                Assert.AreEqual(ExactMultiply(rawA, rawB), result.RawValue,
                    $"raw {rawA} * {rawB} diverged from exact arithmetic");
            }
        }

        [Test]
        public void Multiply_IsCommutative()
        {
            var random = new Random(1337);

            for (int i = 0; i < 2000; i++)
            {
                var a = FixedPoint32.FromRaw((int)((random.NextDouble() * 200.0 - 100.0) * OneRaw));
                var b = FixedPoint32.FromRaw((int)((random.NextDouble() * 200.0 - 100.0) * OneRaw));

                Assert.AreEqual((a * b).RawValue, (b * a).RawValue,
                    "Multiplication must be commutative");
            }
        }

        [Test]
        public void Multiply_ByOne_IsIdentity()
        {
            var values = new[] { -100.5f, -1f, -0.001f, 0f, 0.001f, 1f, 100.5f };

            foreach (var value in values)
            {
                var v = FixedPoint32.FromFloat(value);
                Assert.AreEqual(v.RawValue, (v * FixedPoint32.One).RawValue,
                    $"{value} * 1 must be unchanged");
            }
        }

        // ===== Division =====

        [Test]
        public void Divide_SignCombinations_MatchExactArithmetic()
        {
            var numerators = new[] { -7.25f, -1.5f, -0.25f, 0.25f, 1.5f, 7.25f };
            var denominators = new[] { -4f, -1.5f, -0.5f, 0.5f, 1.5f, 4f };

            foreach (var x in numerators)
            {
                foreach (var y in denominators)
                {
                    var a = FixedPoint32.FromFloat(x);
                    var b = FixedPoint32.FromFloat(y);

                    Assert.AreEqual(ExactDivide(a.RawValue, b.RawValue), (a / b).RawValue,
                        $"{x} / {y} diverged from exact arithmetic");
                }
            }
        }

        [Test]
        public void Divide_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(
                () => { var _ = FixedPoint32.One / FixedPoint32.Zero; },
                "Division by zero must fail loudly rather than produce a garbage value");
        }

        [Test]
        public void FromFraction_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(
                () => FixedPoint32.FromFraction(1, 0));
        }

        // ===== Sqrt =====

        /// <summary>
        /// REGRESSION: seeding Newton-Raphson with (RawValue >> 1) is far too high for
        /// large inputs and the iterations cannot recover - Sqrt(10000) returned 116.75
        /// instead of 100. FixedPoint2.Length and Distance route through this, so every
        /// distance above ~30 units was affected.
        /// </summary>
        [Test]
        public void Sqrt_LargeInputs_ConvergeCorrectly()
        {
            var cases = new[]
            {
                (900f, 30f),
                (2500f, 50f),
                (10000f, 100f),
            };

            foreach (var (input, expected) in cases)
            {
                var actual = FixedPoint32.Sqrt(FixedPoint32.FromFloat(input)).ToFloat();

                Assert.AreEqual(expected, actual, 0.01,
                    $"Sqrt({input}) must converge to {expected}; a result well above it " +
                    "means the initial-guess regression has returned.");
            }
        }

        [Test]
        public void Sqrt_MatchesReferenceAcrossRange()
        {
            var inputs = new[] { 0.25f, 0.5f, 1f, 2f, 4f, 9f, 16f, 100f, 841f, 2500f, 10000f, 32000f };

            foreach (var input in inputs)
            {
                var result = FixedPoint32.Sqrt(FixedPoint32.FromFloat(input));
                double expected = Math.Sqrt(input);
                double actual = result.ToFloat();

                double tolerance = Math.Max(0.001, expected * 0.001);

                Assert.AreEqual(expected, actual, tolerance,
                    $"Sqrt({input}) returned {actual}, expected ~{expected}");
            }
        }

        /// <summary>
        /// 180^2 = 32400, near the top of what 16.16 represents.
        /// </summary>
        [Test]
        public void Sqrt_OfPerfectSquares_IsNearExact()
        {
            for (int n = 1; n <= 180; n++)
            {
                var result = FixedPoint32.Sqrt(FixedPoint32.FromInt(n * n));

                Assert.AreEqual(n, result.ToFloat(), 0.01,
                    $"Sqrt({n * n}) should be very close to {n}");
            }
        }

        /// <summary>
        /// Sqrt must be non-decreasing. A bad initial guess can produce a larger result
        /// for a smaller input, which would make distance comparisons order incorrectly
        /// even where the absolute error looks tolerable.
        /// </summary>
        [Test]
        public void Sqrt_IsMonotonic()
        {
            int previous = 0;

            for (int raw = 1; raw < 20000000; raw += 1337)
            {
                int current = FixedPoint32.Sqrt(FixedPoint32.FromRaw(raw)).RawValue;

                Assert.GreaterOrEqual(current, previous,
                    $"Sqrt must be non-decreasing; raw {raw} produced {current} after {previous}");

                previous = current;
            }
        }

        [Test]
        public void Sqrt_OfNegative_ReturnsZero()
        {
            Assert.AreEqual(FixedPoint32.Zero.RawValue,
                FixedPoint32.Sqrt(FixedPoint32.FromFloat(-4f)).RawValue,
                "Sqrt of a negative must return Zero rather than loop or return garbage");
        }

        [Test]
        public void Sqrt_OfZero_IsZero()
        {
            Assert.AreEqual(FixedPoint32.Zero.RawValue,
                FixedPoint32.Sqrt(FixedPoint32.Zero).RawValue);
        }

        // ===== Percentage =====

        /// <summary>
        /// KNOWN LIMITATION: Percentage divides before multiplying, discarding
        /// significant digits at small ratios. Deliberately loose - tighten this if the
        /// operation order is ever changed to (value * Hundred) / total.
        /// </summary>
        [Test]
        public void Percentage_SmallRatio_LosesPrecisionAsDocumented()
        {
            var result = FixedPoint32.Percentage(FixedPoint32.One, FixedPoint32.FromInt(1000));

            // True answer is 0.1. Divide-first yields roughly 0.0999.
            Assert.AreEqual(0.1, result.ToFloat(), 0.005,
                "1/1000 as a percentage should be ~0.1; a much larger error means the " +
                "division order regressed further.");
        }

        [Test]
        public void Percentage_CommonRatios_AreAccurate()
        {
            var cases = new[]
            {
                (1f, 2f, 50f),
                (1f, 4f, 25f),
                (3f, 4f, 75f),
                (1f, 1f, 100f),
                (0f, 5f, 0f),
            };

            foreach (var (value, total, expected) in cases)
            {
                var result = FixedPoint32.Percentage(
                    FixedPoint32.FromFloat(value), FixedPoint32.FromFloat(total));

                Assert.AreEqual(expected, result.ToFloat(), 0.01,
                    $"{value}/{total} should be {expected}%");
            }
        }

        [Test]
        public void Percentage_OfZeroTotal_ReturnsZero()
        {
            Assert.AreEqual(FixedPoint32.Zero.RawValue,
                FixedPoint32.Percentage(FixedPoint32.One, FixedPoint32.Zero).RawValue,
                "Zero total must return Zero rather than throwing");
        }

        // ===== Conversion round-trips =====

        /// <summary>
        /// FixedPoint32 (16.16) to FixedPoint64 (32.32) is documented as a lossless
        /// upcast. Verify the round-trip actually preserves the value, since the shift
        /// amounts are easy to get backwards.
        /// </summary>
        [Test]
        public void ToFixed64_ThenBack_PreservesValue()
        {
            for (int raw = -500000; raw <= 500000; raw += 37)
            {
                var original = FixedPoint32.FromRaw(raw);
                var roundTripped = FixedPoint32.FromFixed64(original.ToFixed64());

                Assert.AreEqual(original.RawValue, roundTripped.RawValue,
                    $"32 -> 64 -> 32 must be lossless; failed at raw {raw}");
            }
        }

        [Test]
        public void FromInt_ThenToInt_PreservesValue()
        {
            foreach (var n in new[] { -30000, -1000, -1, 0, 1, 1000, 30000 })
            {
                Assert.AreEqual(n, FixedPoint32.FromInt(n).ToInt(),
                    $"FromInt({n}).ToInt() must round-trip");
            }
        }

        [Test]
        public void ToBytes_ThenFromBytes_PreservesValue()
        {
            var random = new Random(4242);

            for (int i = 0; i < 1000; i++)
            {
                var original = FixedPoint32.FromRaw(random.Next(int.MinValue, int.MaxValue));

                var roundTripped = FixedPoint32.FromBytes(original.ToBytes());

                Assert.AreEqual(original.RawValue, roundTripped.RawValue,
                    "Network serialisation round-trip must be exact");
            }
        }

        [Test]
        public void FromBytes_WithInsufficientBytes_Throws()
        {
            Assert.Throws<ArgumentException>(() => FixedPoint32.FromBytes(new byte[3]));
        }

        [Test]
        public void FromBytes_WithNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => FixedPoint32.FromBytes(null));
        }

        // ===== Lerp / interpolation =====

        [Test]
        public void Lerp_AtEndpoints_ReturnsEndpoints()
        {
            var a = FixedPoint32.FromInt(10);
            var b = FixedPoint32.FromInt(20);

            Assert.AreEqual(a.RawValue, FixedPoint32.Lerp(a, b, FixedPoint32.Zero).RawValue,
                "Lerp at t=0 must return a exactly");
            Assert.AreEqual(b.RawValue, FixedPoint32.Lerp(a, b, FixedPoint32.One).RawValue,
                "Lerp at t=1 must return b exactly");
        }

        [Test]
        public void Lerp_AtMidpoint_ReturnsAverage()
        {
            var result = FixedPoint32.Lerp(
                FixedPoint32.FromInt(10), FixedPoint32.FromInt(20), FixedPoint32.Half);

            Assert.AreEqual(15f, result.ToFloat(), 0.001);
        }

        [Test]
        public void LerpClamped_OutsideUnitInterval_ClampsToEndpoints()
        {
            var a = FixedPoint32.FromInt(10);
            var b = FixedPoint32.FromInt(20);

            Assert.AreEqual(a.RawValue,
                FixedPoint32.LerpClamped(a, b, FixedPoint32.FromInt(-5)).RawValue,
                "t below 0 must clamp to a");
            Assert.AreEqual(b.RawValue,
                FixedPoint32.LerpClamped(a, b, FixedPoint32.FromInt(5)).RawValue,
                "t above 1 must clamp to b");
        }

        [Test]
        public void InverseLerp_IsInverseOfLerp()
        {
            var a = FixedPoint32.FromInt(10);
            var b = FixedPoint32.FromInt(20);

            foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var value = FixedPoint32.Lerp(a, b, FixedPoint32.FromFloat(t));
                var recovered = FixedPoint32.InverseLerp(a, b, value);

                Assert.AreEqual(t, recovered.ToFloat(), 0.001,
                    $"InverseLerp must recover t={t}");
            }
        }

        [Test]
        public void InverseLerp_WithDegenerateRange_ReturnsZero()
        {
            var a = FixedPoint32.FromInt(10);

            Assert.AreEqual(FixedPoint32.Zero.RawValue,
                FixedPoint32.InverseLerp(a, a, FixedPoint32.FromInt(15)).RawValue,
                "Equal endpoints must return Zero rather than dividing by zero");
        }

        // ===== MoveTowards =====

        [Test]
        public void MoveTowards_WithinDelta_SnapsToTarget()
        {
            var result = FixedPoint32.MoveTowards(
                FixedPoint32.FromInt(10), FixedPoint32.FromInt(11), FixedPoint32.FromInt(5));

            Assert.AreEqual(FixedPoint32.FromInt(11).RawValue, result.RawValue,
                "When the gap is smaller than maxDelta, MoveTowards must land exactly on target");
        }

        [Test]
        public void MoveTowards_BeyondDelta_MovesByDeltaOnly()
        {
            var result = FixedPoint32.MoveTowards(
                FixedPoint32.FromInt(10), FixedPoint32.FromInt(100), FixedPoint32.FromInt(5));

            Assert.AreEqual(15f, result.ToFloat(), 0.001);
        }

        [Test]
        public void MoveTowards_Downward_MovesByNegativeDelta()
        {
            var result = FixedPoint32.MoveTowards(
                FixedPoint32.FromInt(10), FixedPoint32.FromInt(-100), FixedPoint32.FromInt(5));

            Assert.AreEqual(5f, result.ToFloat(), 0.001,
                "MoveTowards must handle descending motion");
        }

        // ===== Pow =====

        [Test]
        public void Pow_WithZeroExponent_ReturnsOne()
        {
            Assert.AreEqual(FixedPoint32.One.RawValue,
                FixedPoint32.Pow(FixedPoint32.FromInt(7), 0).RawValue);
        }

        [Test]
        public void Pow_WithPositiveExponent_MatchesRepeatedMultiplication()
        {
            var value = FixedPoint32.FromFloat(1.5f);

            var expected = FixedPoint32.One;
            for (int i = 0; i < 5; i++)
                expected = expected * value;

            Assert.AreEqual(expected.RawValue, FixedPoint32.Pow(value, 5).RawValue,
                "Binary exponentiation must agree with repeated multiplication");
        }

        [Test]
        public void Pow_WithNegativeExponent_ReturnsReciprocalPower()
        {
            var result = FixedPoint32.Pow(FixedPoint32.FromInt(2), -2);

            Assert.AreEqual(0.25f, result.ToFloat(), 0.001,
                "2^-2 must be 0.25");
        }

        // ===== Comparison / equality =====

        [Test]
        public void Comparison_IsConsistentWithRawOrdering()
        {
            var random = new Random(777);

            for (int i = 0; i < 2000; i++)
            {
                int rawA = random.Next(-1000000, 1000000);
                int rawB = random.Next(-1000000, 1000000);

                var a = FixedPoint32.FromRaw(rawA);
                var b = FixedPoint32.FromRaw(rawB);

                Assert.AreEqual(rawA < rawB, a < b, $"'<' inconsistent for {rawA}, {rawB}");
                Assert.AreEqual(rawA > rawB, a > b, $"'>' inconsistent for {rawA}, {rawB}");
                Assert.AreEqual(rawA == rawB, a == b, $"'==' inconsistent for {rawA}, {rawB}");
                Assert.AreEqual(Math.Sign(rawA.CompareTo(rawB)), Math.Sign(a.CompareTo(b)),
                    $"CompareTo inconsistent for {rawA}, {rawB}");
            }
        }

        [Test]
        public void Sign_MatchesRawSign()
        {
            Assert.AreEqual(-1, FixedPoint32.FromInt(-5).Sign);
            Assert.AreEqual(0, FixedPoint32.Zero.Sign);
            Assert.AreEqual(1, FixedPoint32.FromInt(5).Sign);
        }

        [Test]
        public void Abs_OfNegative_ReturnsPositive()
        {
            Assert.AreEqual(FixedPoint32.FromFloat(7.25f).RawValue,
                FixedPoint32.Abs(FixedPoint32.FromFloat(-7.25f)).RawValue);
        }

        [Test]
        public void Clamp_ConstrainsToRange()
        {
            var min = FixedPoint32.FromInt(0);
            var max = FixedPoint32.FromInt(10);

            Assert.AreEqual(min.RawValue,
                FixedPoint32.Clamp(FixedPoint32.FromInt(-5), min, max).RawValue);
            Assert.AreEqual(max.RawValue,
                FixedPoint32.Clamp(FixedPoint32.FromInt(15), min, max).RawValue);
            Assert.AreEqual(FixedPoint32.FromInt(5).RawValue,
                FixedPoint32.Clamp(FixedPoint32.FromInt(5), min, max).RawValue);
        }

        // ===== Constants =====

        [Test]
        public void Constants_HaveExpectedRawValues()
        {
            Assert.AreEqual(0, FixedPoint32.Zero.RawValue, "Zero");
            Assert.AreEqual(OneRaw, FixedPoint32.One.RawValue, "One must be raw 65536 (16.16)");
            Assert.AreEqual(OneRaw / 2, FixedPoint32.Half.RawValue, "Half");
            Assert.AreEqual(OneRaw * 2, FixedPoint32.Two.RawValue, "Two");
            Assert.AreEqual(-OneRaw, FixedPoint32.NegativeOne.RawValue, "NegativeOne");
            Assert.AreEqual(OneRaw * 10, FixedPoint32.Ten.RawValue, "Ten");
            Assert.AreEqual(OneRaw * 100, FixedPoint32.Hundred.RawValue, "Hundred");
        }

        // ===== FixedPoint2 =====

        [Test]
        public void FixedPoint2_LengthSquared_MatchesManualComputation()
        {
            var v = new FixedPoint2(FixedPoint32.FromInt(3), FixedPoint32.FromInt(4));

            Assert.AreEqual(25f, v.LengthSquared.ToFloat(), 0.001);
        }

        [Test]
        public void FixedPoint2_Length_OfThreeFourFive_IsFive()
        {
            var v = new FixedPoint2(FixedPoint32.FromInt(3), FixedPoint32.FromInt(4));

            Assert.AreEqual(5f, v.Length.ToFloat(), 0.01,
                "3-4-5 triangle must produce length 5");
        }

        [Test]
        public void FixedPoint2_Dot_OfPerpendicularVectors_IsZero()
        {
            var a = new FixedPoint2(FixedPoint32.One, FixedPoint32.Zero);
            var b = new FixedPoint2(FixedPoint32.Zero, FixedPoint32.One);

            Assert.AreEqual(0f, FixedPoint2.Dot(a, b).ToFloat(), 0.001);
        }

        [Test]
        public void FixedPoint2_Distance_IsSymmetric()
        {
            var a = new FixedPoint2(FixedPoint32.FromInt(1), FixedPoint32.FromInt(2));
            var b = new FixedPoint2(FixedPoint32.FromInt(4), FixedPoint32.FromInt(6));

            Assert.AreEqual(FixedPoint2.Distance(a, b).RawValue,
                FixedPoint2.Distance(b, a).RawValue,
                "Distance must be symmetric");
        }
    }
}
