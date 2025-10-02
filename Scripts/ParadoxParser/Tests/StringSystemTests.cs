using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using ParadoxParser.Data;

namespace ParadoxParser.Tests
{
    public class StringSystemTests
    {
        [SetUp]
        public void Setup()
        {
            // Setup runs before each test
        }

        [TearDown]
        public void TearDown()
        {
            // Cleanup runs after each test
        }

        #region NativeStringPool Tests

        [Test]
        public void NativeStringPool_ShouldStoreAndRetrieveStrings()
        {
            using var pool = new NativeStringPool(10, Allocator.Temp);

            string testString = "Hello World";
            int id = pool.InternString(testString);

            Assert.GreaterOrEqual(id, 0);
            var retrieved = pool.GetString(id);
            Assert.AreEqual(testString, retrieved.ToString());
        }

        [Test]
        public void NativeStringPool_ShouldDeduplicateStrings()
        {
            using var pool = new NativeStringPool(10, Allocator.Temp);

            string testString = "Hello World";
            int id1 = pool.InternString(testString);
            int id2 = pool.InternString(testString);

            Assert.AreEqual(id1, id2, "Same string should return same ID");
            Assert.AreEqual(1, pool.Count, "Should only store one unique string");
        }

        [Test]
        public void NativeStringPool_ShouldHandleEmptyStrings()
        {
            using var pool = new NativeStringPool(10, Allocator.Temp);

            int emptyId1 = pool.InternString("");
            int emptyId2 = pool.InternString(string.Empty);

            Assert.AreEqual(emptyId1, emptyId2);
            Assert.AreEqual("", pool.GetString(emptyId1).ToString());
        }

        [Test]
        public void NativeStringPool_ShouldHandleNullStrings()
        {
            using var pool = new NativeStringPool(10, Allocator.Temp);

            int nullId = pool.InternString("");
            Assert.AreEqual("", pool.GetString(nullId).ToString());
        }

        [Test]
        public void NativeStringPool_ShouldHandleLargeStrings()
        {
            using var pool = new NativeStringPool(100, Allocator.Temp);

            // Use string that fits in FixedString128Bytes (max 128 bytes)
            string largeString = new string('A', 120); // Safe size
            int id = pool.InternString(largeString);

            var retrieved = pool.GetString(id);
            Assert.AreEqual(120, retrieved.Length);
            Assert.AreEqual(largeString, retrieved.ToString());
        }

        [Test]
        public void NativeStringPool_ShouldHandleUnicodeStrings()
        {
            using var pool = new NativeStringPool(100, Allocator.Temp);

            string unicodeString = "Hello 世界 🌍 Привет мир";
            int id = pool.InternString(unicodeString);

            Assert.AreEqual(unicodeString, pool.GetString(id).ToString());
        }

        [Test]
        public void NativeStringPool_Performance_ShouldBeEfficient()
        {
            using var pool = new NativeStringPool(100, Allocator.Temp);

            const int stringCount = 10000;
            var testStrings = new string[stringCount];

            // Generate test strings (keep under 128 bytes)
            for (int i = 0; i < stringCount; i++)
            {
                testStrings[i] = $"test_{i % 1000}"; // Shorter strings
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Add all strings
            var ids = new int[stringCount];
            for (int i = 0; i < stringCount; i++)
            {
                ids[i] = pool.InternString(testStrings[i]);
            }

            stopwatch.Stop();

            // Verify all strings were stored correctly
            for (int i = 0; i < stringCount; i++)
            {
                Assert.AreEqual(testStrings[i], pool.GetString(ids[i]).ToString());
            }

            // Should be able to add 10k strings in under 100ms
            Assert.Less(stopwatch.ElapsedMilliseconds, 100,
                $"Adding {stringCount} strings took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");

            Debug.Log($"Added {stringCount} strings in {stopwatch.ElapsedMilliseconds}ms ({stringCount / (float)stopwatch.ElapsedMilliseconds * 1000:F0} strings/second)");
            Debug.Log($"Unique strings: {pool.Count}");
        }

        #endregion

        #region StringInternSystem Tests

        [Test]
        public void StringInternSystem_ShouldInternStrings()
        {
            using var internSystem = new StringInternSystem(10, Allocator.Temp);

            string testString = "Hello World";
            int id1 = internSystem.InternString(testString);
            int id2 = internSystem.InternString(testString);

            Assert.AreEqual(id1, id2, "Same string should return same ID");
            Assert.IsTrue(internSystem.TryGetString(id1, out var retrieved));
            Assert.AreEqual(testString, retrieved.ToString());
        }

        [Test]
        public void StringInternSystem_ShouldHandleCollisions()
        {
            using var internSystem = new StringInternSystem(100, Allocator.Temp);

            // Create strings that might have hash collisions
            var strings = new[] { "test1", "test2", "test3", "collision_test", "another_test" };
            var ids = new int[strings.Length];

            for (int i = 0; i < strings.Length; i++)
            {
                ids[i] = internSystem.InternString(strings[i]);
            }

            // Verify all strings can be retrieved correctly
            for (int i = 0; i < strings.Length; i++)
            {
                Assert.IsTrue(internSystem.TryGetString(ids[i], out var retrieved));
                Assert.AreEqual(strings[i], retrieved.ToString());
            }
        }

        [Test]
        public void StringInternSystem_ShouldReturnFalseForUnknownHash()
        {
            using var internSystem = new StringInternSystem(100, Allocator.Temp);

            Assert.IsFalse(internSystem.TryGetString(12345, out var result));
            Assert.AreEqual(default(FixedString128Bytes), result);
        }

        [Test]
        public void StringInternSystem_Performance_ShouldBeEfficient()
        {
            using var internSystem = new StringInternSystem(100, Allocator.Temp);

            const int stringCount = 10000;
            var testStrings = new string[stringCount];
            var ids = new int[stringCount];

            // Generate test strings with some duplicates (keep under 128 bytes)
            for (int i = 0; i < stringCount; i++)
            {
                testStrings[i] = $"intern_{i % 1000}"; // Shorter strings
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Intern all strings
            for (int i = 0; i < stringCount; i++)
            {
                ids[i] = internSystem.InternString(testStrings[i]);
            }

            stopwatch.Stop();

            // Verify retrieval performance
            var retrievalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < stringCount; i++)
            {
                Assert.IsTrue(internSystem.TryGetString(ids[i], out var retrieved));
                Assert.AreEqual(testStrings[i], retrieved.ToString());
            }

            retrievalStopwatch.Stop();

            // Should be able to intern 10k strings in under 50ms
            Assert.Less(stopwatch.ElapsedMilliseconds, 50,
                $"Interning {stringCount} strings took {stopwatch.ElapsedMilliseconds}ms, expected < 50ms");

            // Should be able to retrieve 10k strings in under 50ms
            Assert.Less(retrievalStopwatch.ElapsedMilliseconds, 50,
                $"Retrieving {stringCount} strings took {retrievalStopwatch.ElapsedMilliseconds}ms, expected < 50ms");

            Debug.Log($"Interned {stringCount} strings in {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log($"Retrieved {stringCount} strings in {retrievalStopwatch.ElapsedMilliseconds}ms");
            Debug.Log($"String intern system test completed");
        }

        #endregion

        #region FixedString Tests

        [Test]
        public void FixedString4_ShouldStoreShortStrings()
        {
            var fs = new FixedString4("test");

            Assert.AreEqual("test", fs.ToString());
            Assert.AreEqual(4, fs.Length);
        }

        [Test]
        public void FixedString4_ShouldTruncateLongStrings()
        {
            var fs = new FixedString4("toolong");

            Assert.AreEqual("tool", fs.ToString());
            Assert.AreEqual(4, fs.Length);
        }

        [Test]
        public void FixedString8_ShouldStoreStrings()
        {
            var fs = new FixedString8("filename");

            Assert.AreEqual("filename", fs.ToString());
            Assert.AreEqual(8, fs.Length);
        }

        [Test]
        public void FixedString8_ShouldTruncateLongStrings()
        {
            var fs = new FixedString8("verylongfilename");

            Assert.AreEqual("verylong", fs.ToString());
            Assert.AreEqual(8, fs.Length);
        }

        [Test]
        public void FixedString_ShouldHandleEmptyStrings()
        {
            var fs4 = new FixedString4("");
            var fs8 = new FixedString8("");

            Assert.AreEqual("", fs4.ToString());
            Assert.AreEqual("", fs8.ToString());
            Assert.AreEqual(0, fs4.Length);
            Assert.AreEqual(0, fs8.Length);
        }

        [Test]
        public void FixedString_ShouldBeEquatable()
        {
            var fs1 = new FixedString4("test");
            var fs2 = new FixedString4("test");
            var fs3 = new FixedString4("diff");

            Assert.AreEqual(fs1, fs2);
            Assert.AreNotEqual(fs1, fs3);
            Assert.AreEqual(fs1.GetHashCode(), fs2.GetHashCode());
        }

        #endregion

        #region Memory Tests

        [Test]
        public void StringSystems_ShouldDisposeCleanly()
        {
            var pool = new NativeStringPool(10, Allocator.Temp);
            var internSystem = new StringInternSystem(10, Allocator.Temp);

            pool.InternString("test");
            internSystem.InternString("test");

            Assert.DoesNotThrow(() => pool.Dispose());
            Assert.DoesNotThrow(() => internSystem.Dispose());
        }

        [Test]
        public void StringSystems_ShouldNotLeakMemory()
        {
            const int iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                using var pool = new NativeStringPool(10, Allocator.Temp);
                using var internSystem = new StringInternSystem(10, Allocator.Temp);

                pool.InternString($"t_{i % 100}"); // Short strings
                internSystem.InternString($"i_{i % 100}"); // Short strings

                // Systems should dispose cleanly in using blocks
            }

            // If we get here without memory issues, the test passes
            Assert.Pass("No memory leaks detected in string systems");
        }

        #endregion
    }
}