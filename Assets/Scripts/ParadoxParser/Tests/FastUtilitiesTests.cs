using System.Text;
using NUnit.Framework;
using Unity.Collections;
using ParadoxParser.Utilities;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Tests
{
    [TestFixture]
    public class FastUtilitiesTests
    {
        [Test]
        public void FastHasher_FNV1a32_ProducesConsistentHashes()
        {
            // Arrange
            string testString = "test_string";
            var data1 = new NativeArray<byte>(Encoding.UTF8.GetBytes(testString), Allocator.TempJob);
            var data2 = new NativeArray<byte>(Encoding.UTF8.GetBytes(testString), Allocator.TempJob);

            try
            {
                // Act
                uint hash1 = FastHasher.HashFNV1a32(data1);
                uint hash2 = FastHasher.HashFNV1a32(data2);

                // Assert
                Assert.AreEqual(hash1, hash2, "Same strings should produce same hash");
                Assert.AreNotEqual(0, hash1, "Hash should not be zero");
            }
            finally
            {
                data1.Dispose();
                data2.Dispose();
            }
        }

        [Test]
        public void FastHasher_DifferentStrings_ProduceDifferentHashes()
        {
            // Arrange
            var data1 = new NativeArray<byte>(Encoding.UTF8.GetBytes("string1"), Allocator.TempJob);
            var data2 = new NativeArray<byte>(Encoding.UTF8.GetBytes("string2"), Allocator.TempJob);

            try
            {
                // Act
                uint hash1 = FastHasher.HashFNV1a32(data1);
                uint hash2 = FastHasher.HashFNV1a32(data2);

                // Assert
                Assert.AreNotEqual(hash1, hash2, "Different strings should produce different hashes");
            }
            finally
            {
                data1.Dispose();
                data2.Dispose();
            }
        }

        [Test]
        public void FastHasher_CaseInsensitive_IgnoresCase()
        {
            // Arrange
            var data1 = new NativeArray<byte>(Encoding.UTF8.GetBytes("TEST"), Allocator.TempJob);
            var data2 = new NativeArray<byte>(Encoding.UTF8.GetBytes("test"), Allocator.TempJob);

            try
            {
                // Act
                uint hash1 = FastHasher.HashCaseInsensitive(data1);
                uint hash2 = FastHasher.HashCaseInsensitive(data2);

                // Assert
                Assert.AreEqual(hash1, hash2, "Case insensitive hash should ignore case");
            }
            finally
            {
                data1.Dispose();
                data2.Dispose();
            }
        }

        [Test]
        public void FastNumberParser_ParseInt32_ParsesPositiveIntegers()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("12345"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastNumberParser.ParseInt32(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse positive integer");
                Assert.AreEqual(12345, result.Value, "Should parse correct value");
                Assert.AreEqual(5, result.BytesConsumed, "Should consume all digits");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastNumberParser_ParseInt32_ParsesNegativeIntegers()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("-789"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastNumberParser.ParseInt32(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse negative integer");
                Assert.AreEqual(-789, result.Value, "Should parse correct negative value");
                Assert.AreEqual(4, result.BytesConsumed, "Should consume sign and digits");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastNumberParser_ParseFloat_ParsesDecimalNumbers()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("123.456"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastNumberParser.ParseFloat(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse float");
                Assert.AreEqual(123.456f, result.Value, 0.001f, "Should parse correct float value");
                Assert.AreEqual(7, result.BytesConsumed, "Should consume all characters");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastNumberParser_ParseHex_ParsesHexadecimalNumbers()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("0xFF"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastNumberParser.ParseHex(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse hex");
                Assert.AreEqual(255u, result.Value, "Should parse correct hex value");
                Assert.AreEqual(4, result.BytesConsumed, "Should consume prefix and digits");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastNumberParser_ParsePercentage_ParsesPercentageValues()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("75%"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastNumberParser.ParsePercentage(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse percentage");
                Assert.AreEqual(0.75f, result.Value, 0.001f, "Should convert to decimal");
                Assert.AreEqual(3, result.BytesConsumed, "Should consume number and %");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastDateParser_ParseDate_ParsesValidDates()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("1444.11.11"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastDateParser.ParseDate(data);

                // Assert
                Assert.IsTrue(result.Success, "Should successfully parse date");
                Assert.AreEqual(1444, result.Date.Year, "Should parse correct year");
                Assert.AreEqual(11, result.Date.Month, "Should parse correct month");
                Assert.AreEqual(11, result.Date.Day, "Should parse correct day");
                Assert.AreEqual(10, result.BytesConsumed, "Should consume entire date string");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastDateParser_ParseDate_RejectsInvalidDates()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("1444.13.32"), Allocator.TempJob);

            try
            {
                // Act
                var result = FastDateParser.ParseDate(data);

                // Assert
                Assert.IsFalse(result.Success, "Should reject invalid date (month 13, day 32)");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastDateParser_LooksLikeDate_DetectsDatePattern()
        {
            // Arrange
            var validDate = new NativeArray<byte>(Encoding.UTF8.GetBytes("1444.11.11"), Allocator.TempJob);
            var invalidPattern = new NativeArray<byte>(Encoding.UTF8.GetBytes("not.a.date"), Allocator.TempJob);

            try
            {
                // Act & Assert
                Assert.IsTrue(FastDateParser.LooksLikeDate(validDate), "Should detect valid date pattern");
                Assert.IsFalse(FastDateParser.LooksLikeDate(invalidPattern), "Should reject non-date pattern");
            }
            finally
            {
                validDate.Dispose();
                invalidPattern.Dispose();
            }
        }

        [Test]
        public void FastOperatorDetector_DetectSingleCharOperators()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("="), Allocator.TempJob);

            try
            {
                // Act
                var result = FastOperatorDetector.DetectOperator(data, 0);

                // Assert
                Assert.IsTrue(result.Success, "Should detect single char operator");
                Assert.AreEqual(TokenType.Equals, result.Type, "Should detect equals operator");
                Assert.AreEqual(1, result.Length, "Should consume one character");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastOperatorDetector_DetectTwoCharOperators()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(">="), Allocator.TempJob);

            try
            {
                // Act
                var result = FastOperatorDetector.DetectOperator(data, 0);

                // Assert
                Assert.IsTrue(result.Success, "Should detect two char operator");
                Assert.AreEqual(TokenType.GreaterEquals, result.Type, "Should detect greater-equals operator");
                Assert.AreEqual(2, result.Length, "Should consume two characters");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastOperatorDetector_DetectComplexOperators()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(">>="), Allocator.TempJob);

            try
            {
                // Act
                var result = FastOperatorDetector.DetectComplexOperator(data, 0);

                // Assert
                Assert.IsTrue(result.Success, "Should detect complex operator");
                Assert.AreEqual(TokenType.RightShiftAssign, result.Type, "Should detect right shift assign operator");
                Assert.AreEqual(3, result.Length, "Should consume three characters");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastOperatorDetector_CanStartOperator_DetectsOperatorStarts()
        {
            // Act & Assert
            Assert.IsTrue(FastOperatorDetector.CanStartOperator((byte)'='), "Should detect = as operator start");
            Assert.IsTrue(FastOperatorDetector.CanStartOperator((byte)'>'), "Should detect > as operator start");
            Assert.IsTrue(FastOperatorDetector.CanStartOperator((byte)'{'), "Should detect { as operator start");
            Assert.IsFalse(FastOperatorDetector.CanStartOperator((byte)'a'), "Should not detect letter as operator start");
            Assert.IsFalse(FastOperatorDetector.CanStartOperator((byte)'1'), "Should not detect digit as operator start");
        }

        [Test]
        public void FastOperatorDetector_FindNextOperator_LocatesOperators()
        {
            // Arrange
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes("abc = def"), Allocator.TempJob);

            try
            {
                // Act
                int operatorPos = FastOperatorDetector.FindNextOperator(data, 0);

                // Assert
                Assert.AreEqual(4, operatorPos, "Should find operator at position 4");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void FastOperatorDetector_IsAssignmentOperator_IdentifiesAssignments()
        {
            // Act & Assert
            Assert.IsTrue(FastOperatorDetector.IsAssignmentOperator(TokenType.Equals), "= should be assignment");
            Assert.IsTrue(FastOperatorDetector.IsAssignmentOperator(TokenType.Add), "+= should be assignment");
            Assert.IsTrue(FastOperatorDetector.IsAssignmentOperator(TokenType.Subtract), "-= should be assignment");
            Assert.IsFalse(FastOperatorDetector.IsAssignmentOperator(TokenType.LeftBrace), "{ should not be assignment");
        }

        [Test]
        public void FastOperatorDetector_IsComparisonOperator_IdentifiesComparisons()
        {
            // Act & Assert
            Assert.IsTrue(FastOperatorDetector.IsComparisonOperator(TokenType.GreaterThan), "> should be comparison");
            Assert.IsTrue(FastOperatorDetector.IsComparisonOperator(TokenType.LessEquals), "<= should be comparison");
            Assert.IsTrue(FastOperatorDetector.IsComparisonOperator(TokenType.NotEquals), "!= should be comparison");
            Assert.IsFalse(FastOperatorDetector.IsComparisonOperator(TokenType.Plus), "+ should not be comparison");
        }

        [Test]
        public void FastOperatorDetector_GetOperatorPrecedence_ReturnsCorrectPrecedence()
        {
            // Act & Assert
            Assert.Greater(FastOperatorDetector.GetOperatorPrecedence(TokenType.Asterisk),
                          FastOperatorDetector.GetOperatorPrecedence(TokenType.Plus),
                          "* should have higher precedence than +");

            Assert.Greater(FastOperatorDetector.GetOperatorPrecedence(TokenType.Plus),
                          FastOperatorDetector.GetOperatorPrecedence(TokenType.GreaterThan),
                          "+ should have higher precedence than >");

            Assert.AreEqual(-1, FastOperatorDetector.GetOperatorPrecedence(TokenType.Identifier),
                           "Non-operators should return -1");
        }

        [Test]
        public void ParadoxDate_IsValid_ValidatesCorrectly()
        {
            // Arrange
            var validDate = new FastDateParser.ParadoxDate(1444, 11, 11);
            var invalidDate1 = new FastDateParser.ParadoxDate(1444, 13, 11); // Invalid month
            var invalidDate2 = new FastDateParser.ParadoxDate(1444, 2, 30);  // Invalid day for February

            // Act & Assert
            Assert.IsTrue(validDate.IsValid(), "Valid date should pass validation");
            Assert.IsFalse(invalidDate1.IsValid(), "Invalid month should fail validation");
            Assert.IsFalse(invalidDate2.IsValid(), "Invalid day for month should fail validation");
        }

        [Test]
        public void ParadoxDate_CompareTo_ComparesCorrectly()
        {
            // Arrange
            var date1 = new FastDateParser.ParadoxDate(1444, 11, 11);
            var date2 = new FastDateParser.ParadoxDate(1445, 1, 1);
            var date3 = new FastDateParser.ParadoxDate(1444, 11, 11);

            // Act & Assert
            Assert.Less(date1.CompareTo(date2), 0, "Earlier date should be less than later date");
            Assert.Greater(date2.CompareTo(date1), 0, "Later date should be greater than earlier date");
            Assert.AreEqual(0, date1.CompareTo(date3), "Same dates should be equal");
        }
    }
}