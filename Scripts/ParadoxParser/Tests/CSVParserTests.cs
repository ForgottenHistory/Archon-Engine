using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ParadoxParser.CSV;
using ParadoxParser.Utilities;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Tests for the generic CSV parser with Paradox semicolon format
    /// </summary>
    [TestFixture]
    public class CSVParserTests
    {
        [Test]
        public void CSVTokenizer_BasicSemicolonFormat_ShouldTokenizeCorrectly()
        {
            string csvData = @"province;red;green;blue;name
1;128;34;64;Stockholm
2;0;36;128;Östergötland";

            var sourceBytes = ConvertStringToBytes(csvData);
            var tokens = new NativeList<CSVTokenizer.CSVToken>(100, Allocator.Temp);

            try
            {
                var result = CSVTokenizer.Tokenize(sourceBytes, tokens);

                Assert.IsTrue(result.Success, "Tokenization should succeed");
                Assert.Greater(tokens.Length, 0, "Should generate tokens");

                // Verify we have header and data tokens
                int fieldCount = 0;
                int rowCount = 0;
                int fieldsInCurrentRow = 0;

                for (int i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    switch (token.Type)
                    {
                        case CSVTokenizer.CSVTokenType.Field:
                        case CSVTokenizer.CSVTokenType.QuotedField:
                            fieldCount++;
                            fieldsInCurrentRow++;
                            break;
                        case CSVTokenizer.CSVTokenType.EndOfRow:
                            if (fieldsInCurrentRow > 0)
                                rowCount++;
                            fieldsInCurrentRow = 0;
                            break;
                        case CSVTokenizer.CSVTokenType.EndOfFile:
                            if (fieldsInCurrentRow > 0)
                                rowCount++;
                            break;
                    }
                }

                Assert.AreEqual(3, rowCount, "Should have 3 rows (including header)");
                Assert.AreEqual(15, fieldCount, "Should have 15 total fields (5 columns x 3 rows)");

                UnityEngine.Debug.Log($"CSV Tokenizer: {fieldCount} fields, {rowCount} rows");
            }
            finally
            {
                tokens.Dispose();
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void CSVParser_ProvinceDefinitionFormat_ShouldParseCorrectly()
        {
            string csvData = @"province;red;green;blue;name;x
1;128;34;64;Stockholm;x
2;0;36;128;Östergötland;x
3;128;38;192;Småland;x";

            var sourceBytes = ConvertStringToBytes(csvData);

            try
            {
                var parseResult = CSVParser.Parse(sourceBytes, Allocator.Temp, hasHeader: true);

                Assert.IsTrue(parseResult.Success, "Parsing should succeed");
                Assert.AreEqual(3, parseResult.RowCount, "Should have 3 data rows");
                Assert.AreEqual(6, parseResult.ColumnCount, "Should have 6 columns");

                // Test column lookups
                var provinceHash = FastHasher.HashFNV1a32("province");
                var redHash = FastHasher.HashFNV1a32("red");
                var nameHash = FastHasher.HashFNV1a32("name");

                int provinceCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, provinceHash);
                int redCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, redHash);
                int nameCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, nameHash);

                Assert.AreEqual(0, provinceCol, "Province should be column 0");
                Assert.AreEqual(1, redCol, "Red should be column 1");
                Assert.AreEqual(4, nameCol, "Name should be column 4");

                // Test data extraction
                var firstRow = parseResult.Rows[0];

                Assert.IsTrue(CSVParser.TryGetInt(firstRow, provinceCol, out int provinceId), "Should parse province ID");
                Assert.AreEqual(1, provinceId, "First province should be ID 1");

                Assert.IsTrue(CSVParser.TryGetInt(firstRow, redCol, out int redValue), "Should parse red value");
                Assert.AreEqual(128, redValue, "Red value should be 128");

                // Test UTF-8 name extraction
                var nameField = CSVParser.GetField(firstRow, nameCol);
                Assert.Greater(nameField.Length, 0, "Name field should not be empty");

                UnityEngine.Debug.Log($"CSV Parser: {parseResult.RowCount} rows, {parseResult.ColumnCount} columns");
                UnityEngine.Debug.Log($"First row: Province {provinceId}, Red {redValue}");

                parseResult.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void CSVParser_AdjacenciesFormat_ShouldParseCorrectly()
        {
            string csvData = @"From;To;Type;Through;start_x;start_y;stop_x;stop_y;Comment
333;4559;sea;1300;-1;-1;-1;-1;Majorca-Minorca
4560;333;sea;1295;-1;-1;-1;-1;Ibiza-Majorca
6;12;sea;1258;3008;1633;3000;1630;Skåne-Sjaelland";

            var sourceBytes = ConvertStringToBytes(csvData);

            try
            {
                var parseResult = CSVParser.Parse(sourceBytes, Allocator.Temp, hasHeader: true);

                Assert.IsTrue(parseResult.Success, "Parsing should succeed");
                Assert.AreEqual(3, parseResult.RowCount, "Should have 3 data rows");
                Assert.AreEqual(9, parseResult.ColumnCount, "Should have 9 columns");

                // Test column lookups
                var fromHash = FastHasher.HashFNV1a32("From");
                var toHash = FastHasher.HashFNV1a32("To");
                var typeHash = FastHasher.HashFNV1a32("Type");
                var commentHash = FastHasher.HashFNV1a32("Comment");

                int fromCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, fromHash);
                int toCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, toHash);
                int typeCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, typeHash);
                int commentCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, commentHash);

                Assert.GreaterOrEqual(fromCol, 0, "Should find From column");
                Assert.GreaterOrEqual(toCol, 0, "Should find To column");
                Assert.GreaterOrEqual(typeCol, 0, "Should find Type column");
                Assert.GreaterOrEqual(commentCol, 0, "Should find Comment column");

                // Test adjacency data extraction
                var firstRow = parseResult.Rows[0];

                Assert.IsTrue(CSVParser.TryGetInt(firstRow, fromCol, out int fromProvince), "Should parse from province");
                Assert.AreEqual(333, fromProvince, "From province should be 333");

                Assert.IsTrue(CSVParser.TryGetInt(firstRow, toCol, out int toProvince), "Should parse to province");
                Assert.AreEqual(4559, toProvince, "To province should be 4559");

                var typeField = CSVParser.GetField(firstRow, typeCol);
                Assert.Greater(typeField.Length, 0, "Type field should not be empty");

                // Test UTF-8 comment with Swedish characters
                var lastRow = parseResult.Rows[2];
                var commentField = CSVParser.GetField(lastRow, commentCol);
                Assert.Greater(commentField.Length, 0, "Comment field should not be empty");

                UnityEngine.Debug.Log($"Adjacencies: {parseResult.RowCount} adjacencies parsed");
                UnityEngine.Debug.Log($"First adjacency: {fromProvince} -> {toProvince}");

                parseResult.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void CSVParser_QuotedFields_ShouldHandleCorrectly()
        {
            string csvData = @"id;name;description
1;""Simple Name"";""A simple description""
2;""Name with ; semicolon"";""Description with quoted text""
3;Unquoted;Also unquoted";

            var sourceBytes = ConvertStringToBytes(csvData);

            try
            {
                var parseResult = CSVParser.Parse(sourceBytes, Allocator.Temp, hasHeader: true);

                Assert.IsTrue(parseResult.Success, "Parsing should succeed");
                Assert.AreEqual(3, parseResult.RowCount, "Should have 3 data rows");

                // Verify quoted field handling
                var nameHash = FastHasher.HashFNV1a32("name");
                int nameCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, nameHash);
                Assert.GreaterOrEqual(nameCol, 0, "Should find name column");

                // Check field with semicolon inside quotes
                var secondRow = parseResult.Rows[1];
                var nameField = CSVParser.GetField(secondRow, nameCol);
                Assert.Greater(nameField.Length, 0, "Name field should not be empty");

                UnityEngine.Debug.Log("CSV Parser: Successfully handled quoted fields with special characters");

                parseResult.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void CSVParser_EmptyAndMissingFields_ShouldHandleGracefully()
        {
            string csvData = @"id;value;name
1;100;First
2;;Second
3;300;";

            var sourceBytes = ConvertStringToBytes(csvData);

            try
            {
                var parseResult = CSVParser.Parse(sourceBytes, Allocator.Temp, hasHeader: true);

                Assert.IsTrue(parseResult.Success, "Parsing should succeed");
                Assert.AreEqual(3, parseResult.RowCount, "Should have 3 data rows");

                var valueHash = FastHasher.HashFNV1a32("value");
                int valueCol = CSVParser.FindColumnIndex(parseResult.HeaderHashes, valueHash);

                // Test missing value (empty field)
                var secondRow = parseResult.Rows[1];
                Assert.IsFalse(CSVParser.TryGetInt(secondRow, valueCol, out int missingValue), "Should fail to parse empty field as int");

                UnityEngine.Debug.Log("CSV Parser: Successfully handled empty and missing fields");

                parseResult.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        /// <summary>
        /// Helper method to convert string to byte array for testing
        /// </summary>
        private NativeArray<byte> ConvertStringToBytes(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var nativeBytes = new NativeArray<byte>(bytes.Length, Allocator.Temp);
            nativeBytes.CopyFrom(bytes);
            return nativeBytes;
        }
    }
}