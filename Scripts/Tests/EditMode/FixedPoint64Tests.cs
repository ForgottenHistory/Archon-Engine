using System;
using System.Numerics;
using NUnit.Framework;
using Core.Data;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for FixedPoint64 - the deterministic arithmetic type underpinning
    /// all simulation math.
    ///
    /// WHY THIS MATTERS: Every value that feeds a simulation decision flows through
    /// this type. A wrong result here doesn't crash - it silently desyncs multiplayer
    /// clients and corrupts saves. These tests compare against exact BigInteger
    /// arithmetic rather than double, because double has its own rounding and would
    /// mask precisely the errors we care about.
    /// </summary>
    public class FixedPoint64Tests
    {
        private const int FractionalBits = 32;
        private const long OneRaw = 1L << FractionalBits;

        /// <summary>Exact reference: floor(a * b / 2^32) computed in arbitrary precision.</summary>
        private static long ExactMultiply(long rawA, long rawB)
        {
            return (long)(((BigInteger)rawA * rawB) >> FractionalBits);
        }

        private static FixedPoint64 FromRawDouble(double value)
        {
            return FixedPoint64.FromRaw((long)Math.Round(value * OneRaw));
        }

        // ===== Multiplication =====

        /// <summary>
        /// REGRESSION: the original implementation split operands with
        /// (value >> 32) and (value &amp; 0xFFFFFFFF). That decomposition is invalid for
        /// negative values - the arithmetic shift floors the high limb while the low
        /// limb stays positive, so the cross terms don't reconstruct the product.
        ///
        /// The failure only appeared when BOTH operands were negative AND both had a
        /// fractional part, which is why whole-number modifier math never caught it.
        /// This exact case returned 21.65625 instead of 22.65625 - off by exactly 1.0.
        /// </summary>
        [Test]
        public void Multiply_BothOperandsNegativeWithFractions_ReturnsExactProduct()
        {
            var a = FixedPoint64.FromFloat(-7.25f);
            var b = FixedPoint64.FromFloat(-3.125f);

            var result = a * b;

            Assert.AreEqual(ExactMultiply(a.RawValue, b.RawValue), result.RawValue,
                "-7.25 * -3.125 must equal 22.65625; a result of 21.65625 means the " +
                "negative-operand limb decomposition has regressed.");
        }

        [Test]
        public void Multiply_SignCombinations_MatchExactArithmetic()
        {
            var operands = new[] { -7.25f, -3.125f, -1.5f, -0.25f, 0.25f, 1.5f, 3.125f, 7.25f };

            foreach (var x in operands)
            {
                foreach (var y in operands)
                {
                    var a = FixedPoint64.FromFloat(x);
                    var b = FixedPoint64.FromFloat(y);

                    Assert.AreEqual(ExactMultiply(a.RawValue, b.RawValue), (a * b).RawValue,
                        $"{x} * {y} diverged from exact arithmetic");
                }
            }
        }

        /// <summary>
        /// Broad randomised sweep. The pre-fix implementation failed roughly 15% of
        /// uniformly random operand pairs, so this catches any partial regression.
        /// Seeded for reproducibility.
        /// </summary>
        [Test]
        public void Multiply_RandomOperands_MatchExactArithmetic()
        {
            var random = new System.Random(20260728);

            for (int i = 0; i < 20000; i++)
            {
                long rawA = (long)((random.NextDouble() * 2000.0 - 1000.0) * OneRaw);
                long rawB = (long)((random.NextDouble() * 2000.0 - 1000.0) * OneRaw);

                var result = FixedPoint64.FromRaw(rawA) * FixedPoint64.FromRaw(rawB);

                Assert.AreEqual(ExactMultiply(rawA, rawB), result.RawValue,
                    $"raw {rawA} * {rawB} diverged from exact arithmetic");
            }
        }

        [Test]
        public void Multiply_IsCommutative()
        {
            var random = new System.Random(1337);

            for (int i = 0; i < 2000; i++)
            {
                var a = FixedPoint64.FromRaw((long)((random.NextDouble() * 200.0 - 100.0) * OneRaw));
                var b = FixedPoint64.FromRaw((long)((random.NextDouble() * 200.0 - 100.0) * OneRaw));

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
                var v = FixedPoint64.FromFloat(value);
                Assert.AreEqual(v.RawValue, (v * FixedPoint64.One).RawValue,
                    $"{value} * 1 must be unchanged");
            }
        }

        [Test]
        public void Multiply_ByZero_IsZero()
        {
            var values = new[] { -100.5f, -1f, 0f, 1f, 100.5f };

            foreach (var value in values)
            {
                Assert.AreEqual(0L, (FixedPoint64.FromFloat(value) * FixedPoint64.Zero).RawValue,
                    $"{value} * 0 must be zero");
            }
        }

        [Test]
        public void Multiply_LargeOperands_DoesNotOverflow()
        {
            // 100,000 * 10,000 = 1e9, well inside the 32.32 integer range but large
            // enough that a naive 64-bit product without a 128-bit intermediate wraps.
            var a = FixedPoint64.FromInt(100000);
            var b = FixedPoint64.FromInt(10000);

            Assert.AreEqual(1000000000L, (a * b).ToLong());
        }

        // ===== Trigonometry =====

        [Test]
        public void Sin_KnownAngles_MatchExpectedValues()
        {
            const double tolerance = 1e-6;

            Assert.AreEqual(0.0, FixedPoint64.Sin(FixedPoint64.Zero).ToDouble(), tolerance);
            Assert.AreEqual(1.0, FixedPoint64.Sin(FixedPoint64.HalfPi).ToDouble(), tolerance);
            Assert.AreEqual(0.0, FixedPoint64.Sin(FixedPoint64.Pi).ToDouble(), tolerance);
            Assert.AreEqual(-1.0, FixedPoint64.Sin(-FixedPoint64.HalfPi).ToDouble(), tolerance);
        }

        [Test]
        public void Cos_KnownAngles_MatchExpectedValues()
        {
            const double tolerance = 1e-6;

            Assert.AreEqual(1.0, FixedPoint64.Cos(FixedPoint64.Zero).ToDouble(), tolerance);
            Assert.AreEqual(0.0, FixedPoint64.Cos(FixedPoint64.HalfPi).ToDouble(), tolerance);
            Assert.AreEqual(-1.0, FixedPoint64.Cos(FixedPoint64.Pi).ToDouble(), tolerance);
        }

        /// <summary>
        /// Range reduction must hold far outside [-Pi, Pi]. Orbital angles accumulate
        /// without bound as the calendar advances, so a body that has completed many
        /// revolutions still needs a correct position.
        /// </summary>
        [Test]
        public void SinCos_WideAngleRange_StayAccurate()
        {
            const double tolerance = 1e-6;

            for (int i = -1000; i <= 1000; i++)
            {
                double angle = i * 0.1;
                var fixedAngle = FromRawDouble(angle);

                Assert.AreEqual(Math.Sin(angle), FixedPoint64.Sin(fixedAngle).ToDouble(), tolerance,
                    $"Sin diverged at {angle} rad");
                Assert.AreEqual(Math.Cos(angle), FixedPoint64.Cos(fixedAngle).ToDouble(), tolerance,
                    $"Cos diverged at {angle} rad");
            }
        }

        [Test]
        public void SinCos_PythagoreanIdentity_Holds()
        {
            const double tolerance = 1e-6;

            for (int i = 0; i <= 720; i++)
            {
                var radians = FixedPoint64.DegreesToRadians(FixedPoint64.FromInt(i));
                var sin = FixedPoint64.Sin(radians);
                var cos = FixedPoint64.Cos(radians);

                Assert.AreEqual(1.0, (sin * sin + cos * cos).ToDouble(), tolerance,
                    $"sin^2 + cos^2 != 1 at {i} degrees");
            }
        }

        [Test]
        public void Sin_IsOdd_AndCos_IsEven()
        {
            const double tolerance = 1e-6;

            for (int i = 1; i <= 360; i++)
            {
                var radians = FixedPoint64.DegreesToRadians(FixedPoint64.FromInt(i));

                Assert.AreEqual(-FixedPoint64.Sin(radians).ToDouble(),
                    FixedPoint64.Sin(-radians).ToDouble(), tolerance,
                    $"sin(-x) must equal -sin(x) at {i} degrees");

                Assert.AreEqual(FixedPoint64.Cos(radians).ToDouble(),
                    FixedPoint64.Cos(-radians).ToDouble(), tolerance,
                    $"cos(-x) must equal cos(x) at {i} degrees");
            }
        }

        /// <summary>
        /// Determinism is the whole point of fixed-point: identical inputs must give
        /// bit-identical outputs, not merely close ones.
        /// </summary>
        [Test]
        public void Sin_RepeatedEvaluation_IsBitIdentical()
        {
            for (int i = 0; i < 360; i += 7)
            {
                var radians = FixedPoint64.DegreesToRadians(FixedPoint64.FromInt(i));

                Assert.AreEqual(FixedPoint64.Sin(radians).RawValue,
                    FixedPoint64.Sin(radians).RawValue,
                    $"Sin must be deterministic at {i} degrees");
            }
        }

        [Test]
        public void DegreesToRadians_RoundTrips()
        {
            const double tolerance = 1e-6;

            for (int degrees = -360; degrees <= 360; degrees += 15)
            {
                var original = FixedPoint64.FromInt(degrees);
                var roundTripped = FixedPoint64.RadiansToDegrees(
                    FixedPoint64.DegreesToRadians(original));

                Assert.AreEqual(original.ToDouble(), roundTripped.ToDouble(), tolerance,
                    $"{degrees} degrees failed to round-trip");
            }
        }

        // ===== Division and Sqrt (existing behaviour, guarded) =====

        /// <summary>
        /// REGRESSION: division was implemented as (dividend &lt;&lt; 32) / divisor.
        /// Since the raw value already carries 32 fractional bits, that shift overflows
        /// for any |dividend| >= 0.5 and silently wrapped: 1/180 returned 0, and
        /// 1000/180 returned 0. Only operands inside (-0.5, 0.5) were correct.
        /// </summary>
        [Test]
        public void Divide_DividendAboveOverflowThreshold_ReturnsCorrectResult()
        {
            const double tolerance = 1e-6;

            Assert.AreEqual(1.0 / 180.0,
                (FixedPoint64.One / FixedPoint64.FromInt(180)).ToDouble(), tolerance,
                "1 / 180 must not return 0 - the dividend shift has overflowed.");

            Assert.AreEqual(1000.0 / 180.0,
                (FixedPoint64.FromInt(1000) / FixedPoint64.FromInt(180)).ToDouble(), tolerance,
                "1000 / 180 must not return 0 - the dividend shift has overflowed.");
        }

        [Test]
        public void Divide_SignCombinations_ProduceExpectedMagnitudeAndSign()
        {
            const double tolerance = 1e-6;
            var pairs = new[] { (7.5f, 2.5f), (-7.5f, 2.5f), (7.5f, -2.5f), (-7.5f, -2.5f) };

            foreach (var (x, y) in pairs)
            {
                var result = FixedPoint64.FromFloat(x) / FixedPoint64.FromFloat(y);
                Assert.AreEqual(x / y, result.ToDouble(), tolerance, $"{x} / {y} was wrong");
            }
        }

        [Test]
        public void Divide_RandomOperands_MatchExactArithmetic()
        {
            var random = new System.Random(4242);

            for (int i = 0; i < 20000; i++)
            {
                long rawA = (long)((random.NextDouble() * 200000.0 - 100000.0) * OneRaw);
                long rawB = (long)((random.NextDouble() * 2000.0 - 1000.0) * OneRaw);
                if (rawB == 0) continue;

                var result = FixedPoint64.FromRaw(rawA) / FixedPoint64.FromRaw(rawB);
                long expected = (long)(((BigInteger)rawA << FractionalBits) / rawB);

                Assert.AreEqual(expected, result.RawValue,
                    $"raw {rawA} / {rawB} diverged from exact arithmetic");
            }
        }

        [Test]
        public void Divide_ByZero_Throws()
        {
            Assert.Throws<DivideByZeroException>(
                () => { var _ = FixedPoint64.One / FixedPoint64.Zero; });
        }

        /// <summary>
        /// REGRESSION: Sqrt used the same overflowing (x &lt;&lt; 32) / guess shift for its
        /// Newton step, so it was wrong for essentially every input - Sqrt(4) returned
        /// 0.0078 instead of 2. Separately, the old initial guess of value/2 was too
        /// far off for large inputs to converge in 8 iterations, leaving Sqrt(1000000)
        /// at 2120 instead of 1000. Large values are covered explicitly here.
        /// </summary>
        [Test]
        public void Sqrt_PerfectSquares_AreExact()
        {
            const double tolerance = 1e-5;

            foreach (var value in new[] { 1, 4, 9, 16, 100, 10000, 1000000 })
            {
                Assert.AreEqual(Math.Sqrt(value),
                    FixedPoint64.Sqrt(FixedPoint64.FromInt(value)).ToDouble(), tolerance,
                    $"Sqrt({value}) was wrong");
            }
        }

        [Test]
        public void Sqrt_WideValueRange_ConvergesAccurately()
        {
            const double tolerance = 1e-5;
            var random = new System.Random(31415);

            for (int i = 0; i < 5000; i++)
            {
                double value = random.NextDouble() * 1000000.0;
                var result = FixedPoint64.Sqrt(FromRawDouble(value));

                Assert.AreEqual(Math.Sqrt(value), result.ToDouble(), tolerance,
                    $"Sqrt({value}) failed to converge");
            }
        }

        [Test]
        public void Sqrt_ResultSquared_RecoversOriginal()
        {
            const double tolerance = 1e-4;

            foreach (var value in new[] { 2, 3, 5, 7, 123, 9999 })
            {
                var root = FixedPoint64.Sqrt(FixedPoint64.FromInt(value));
                Assert.AreEqual((double)value, (root * root).ToDouble(), tolerance,
                    $"Sqrt({value})^2 did not recover the original value");
            }
        }

        [Test]
        public void Sqrt_NegativeInput_ReturnsZero()
        {
            Assert.AreEqual(0L, FixedPoint64.Sqrt(FixedPoint64.FromInt(-9)).RawValue,
                "Sqrt of a negative value is defined to return Zero");
        }

        // ===== Pow and Modulo =====

        /// <summary>
        /// Pow chains multiplications, so it silently inherited the negative-operand
        /// multiply bug. These cases exercise the negative bases that were affected.
        /// </summary>
        [Test]
        public void Pow_IntegerExponents_MatchExpectedValues()
        {
            const double tolerance = 1e-6;
            var cases = new[] { (2.0, 3), (2.0, 10), (1.5, 4), (-2.0, 3), (-2.0, 2), (10.0, 3), (2.0, -2) };

            foreach (var (baseValue, exponent) in cases)
            {
                var result = FixedPoint64.Pow(FixedPoint64.FromFloat((float)baseValue), exponent);

                Assert.AreEqual(Math.Pow(baseValue, exponent), result.ToDouble(), tolerance,
                    $"{baseValue}^{exponent} was wrong");
            }
        }

        [Test]
        public void Pow_ZeroExponent_ReturnsOne()
        {
            foreach (var value in new[] { -5f, -0.5f, 0.5f, 5f })
            {
                Assert.AreEqual(FixedPoint64.One.RawValue,
                    FixedPoint64.Pow(FixedPoint64.FromFloat(value), 0).RawValue,
                    $"{value}^0 must be exactly One");
            }
        }

        /// <summary>
        /// Sin's range reduction relies on % truncating toward zero (keeping the sign
        /// of the dividend) rather than flooring. Pinned so a change to the operator
        /// doesn't silently break angle wrapping.
        /// </summary>
        [Test]
        public void Modulo_TruncatesTowardZero()
        {
            const double tolerance = 1e-6;
            var cases = new[] { (7.5f, 2f, 1.5), (-7.5f, 2f, -1.5), (7.5f, -2f, 1.5), (370f, 360f, 10.0) };

            foreach (var (x, y, expected) in cases)
            {
                var result = FixedPoint64.FromFloat(x) % FixedPoint64.FromFloat(y);
                Assert.AreEqual(expected, result.ToDouble(), tolerance, $"{x} % {y} was wrong");
            }
        }
    }
}
