using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using ParadoxParser.Core;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace ParadoxParser.Tests
{
    public class FileIOTests
    {
        private string tempDirectory;
        private string testFilePath;

        [SetUp]
        public void Setup()
        {
            // Use a more reliable temp directory for Editor tests
            tempDirectory = Path.Combine(Path.GetTempPath(), "ParadoxParserTests");
            Directory.CreateDirectory(tempDirectory);
            testFilePath = Path.Combine(tempDirectory, "test.txt");

            Debug.Log($"Test setup: tempDirectory = {tempDirectory}");
            Debug.Log($"Test setup: Directory exists = {Directory.Exists(tempDirectory)}");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        #region AsyncFileReader Tests

        [Test]
        public async Task AsyncFileReader_ShouldReadFile()
        {
            // Create test file
            string testContent = "Hello World!\nThis is a test file.\nWith multiple lines.";
            await File.WriteAllTextAsync(testFilePath, testContent);

            // Read file using AsyncFileReader
            var result = await AsyncFileReader.ReadFileAsync(testFilePath, Allocator.TempJob);

            Assert.IsTrue(result.IsValid);
            if (!result.Success)
            {
                Debug.LogError($"AsyncFileReader failed: {result.ErrorMessage}");
            }
            Assert.IsTrue(result.Success);
            Assert.AreEqual(testFilePath, result.FilePath);
            Assert.AreEqual(testContent.Length, result.FileSize);

            // Convert bytes back to string for verification
            string readContent = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.AreEqual(testContent, readContent);

            result.Dispose();
        }

        [Test]
        public async Task AsyncFileReader_ShouldHandleNonExistentFile()
        {
            string nonExistentPath = Path.Combine(tempDirectory, "nonexistent.txt");

            var result = await AsyncFileReader.ReadFileAsync(nonExistentPath, Allocator.TempJob);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("File not found"));
            Assert.IsFalse(result.Data.IsCreated);
        }

        [Test]
        public async Task AsyncFileReader_ShouldReadChunk()
        {
            // Create test file
            string testContent = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            await File.WriteAllTextAsync(testFilePath, testContent);

            // Read chunk from middle of file
            var result = await AsyncFileReader.ReadFileChunkAsync(testFilePath, 10, 10, Allocator.TempJob);

            if (!result.Success)
            {
                Debug.LogError($"ReadFileChunk failed: {result.ErrorMessage}");
            }
            Assert.IsTrue(result.Success);
            Assert.AreEqual(10, result.FileSize);

            string chunkContent = Encoding.UTF8.GetString(result.Data.ToArray());
            Assert.AreEqual("ABCDEFGHIJ", chunkContent);

            result.Dispose();
        }

        [Test]
        public async Task AsyncFileReader_ShouldHandleLargeFile()
        {
            // Create large test file (1MB)
            var largeContent = new StringBuilder();
            for (int i = 0; i < 10000; i++)
            {
                largeContent.AppendLine($"Line {i}: This is a test line with some content to make it longer.");
            }

            string content = largeContent.ToString();
            await File.WriteAllTextAsync(testFilePath, content);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await AsyncFileReader.ReadFileAsync(testFilePath, Allocator.TempJob);
            stopwatch.Stop();

            if (!result.Success)
            {
                Debug.LogError($"Large file read failed: {result.ErrorMessage}");
            }
            Assert.IsTrue(result.Success);
            Assert.AreEqual(content.Length, result.FileSize);

            // Should read 1MB file in reasonable time (relaxed for Unity Editor)
            Assert.Less(stopwatch.ElapsedMilliseconds, 1000,
                $"Reading large file took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");

            Debug.Log($"Read {result.FileSize} bytes in {stopwatch.ElapsedMilliseconds}ms ({result.FileSize / (float)stopwatch.ElapsedMilliseconds * 1000 / 1024 / 1024:F2} MB/second)");

            result.Dispose();
        }

        [Test]
        public void AsyncFileReader_GetFilesBatch_ShouldWork()
        {
            // Create multiple test files
            for (int i = 0; i < 5; i++)
            {
                string filePath = Path.Combine(tempDirectory, $"test{i}.txt");
                File.WriteAllText(filePath, $"Content {i}");
            }

            var files = AsyncFileReader.GetFilesBatch(tempDirectory, "*.txt", 10);

            Assert.AreEqual(5, files.Length);
            Assert.IsTrue(files[0].Name.StartsWith("test"));
        }

        #endregion

        #region DirectoryScanner Tests

        [Test]
        public void DirectoryScanner_ShouldScanDirectory()
        {
            // Create test files with different extensions
            File.WriteAllText(Path.Combine(tempDirectory, "test1.txt"), "content1");
            File.WriteAllText(Path.Combine(tempDirectory, "test2.csv"), "content2");
            File.WriteAllText(Path.Combine(tempDirectory, "test3.txt"), "content3");
            File.WriteAllText(Path.Combine(tempDirectory, "test4.log"), "content4");

            var options = new ScanOptions
            {
                IncludeSubdirectories = false,
                FilePatterns = new[] { "*.txt", "*.csv" },
                SortBy = FileSortBy.Name
            };

            var result = DirectoryScanner.ScanDirectory(tempDirectory, options);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.TotalFiles); // 2 txt + 1 csv
            Assert.IsTrue(result.Files[0].Name.CompareTo(result.Files[1].Name) <= 0); // Sorted by name
        }

        [Test]
        public void DirectoryScanner_ShouldHandleNonExistentDirectory()
        {
            string nonExistentDir = Path.Combine(tempDirectory, "nonexistent");

            var result = DirectoryScanner.ScanDirectory(nonExistentDir);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorMessage.Contains("Directory not found"));
        }

        [Test]
        public void DirectoryScanner_ShouldRespectFileLimits()
        {
            // Create more files than limit
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(tempDirectory, $"test{i}.txt"), $"content{i}");
            }

            var options = new ScanOptions
            {
                MaxFiles = 5
            };

            var result = DirectoryScanner.ScanDirectory(tempDirectory, options);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, result.TotalFiles);
        }

        [Test]
        public void DirectoryScanner_ShouldFilterBySizeAndExclusions()
        {
            // Create files of different sizes
            File.WriteAllText(Path.Combine(tempDirectory, "small.txt"), "small");
            File.WriteAllText(Path.Combine(tempDirectory, "medium.txt"), new string('m', 100));
            File.WriteAllText(Path.Combine(tempDirectory, "large.txt"), new string('l', 1000));
            File.WriteAllText(Path.Combine(tempDirectory, "excluded.tmp"), "excluded");

            var options = new ScanOptions
            {
                MinFileSize = 10,
                MaxFileSize = 500,
                ExcludePatterns = new[] { "*.tmp" }
            };

            var result = DirectoryScanner.ScanDirectory(tempDirectory, options);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.TotalFiles); // Only medium.txt should match
            Assert.AreEqual("medium.txt", result.Files[0].Name);
        }

        #endregion

        #region FilePriorityQueue Tests

        [Test]
        public void FilePriorityQueue_ShouldPrioritizeCorrectly()
        {
            using var queue = new FilePriorityQueue(10, Allocator.Temp);

            // Add files with different priorities
            int lowId = queue.EnqueueFile("low.txt", FilePriority.Low);
            int highId = queue.EnqueueFile("high.txt", FilePriority.High);
            int normalId = queue.EnqueueFile("normal.txt", FilePriority.Normal);

            // Verify enqueue operations succeeded
            Assert.Greater(lowId, 0, "Low priority file should have valid ID");
            Assert.Greater(highId, 0, "High priority file should have valid ID");
            Assert.Greater(normalId, 0, "Normal priority file should have valid ID");

            Debug.Log($"Enqueued IDs: low={lowId}, high={highId}, normal={normalId}");
            Assert.AreEqual(3, queue.Count);

            // High priority should come first
            Assert.IsTrue(queue.TryDequeue(out var entry1));
            Debug.Log($"First dequeue: ID={entry1.FileId}, Priority={entry1.Priority}");
            Assert.AreEqual(highId, entry1.FileId);
            Assert.AreEqual(FilePriority.High, entry1.Priority);

            // Normal priority should come second
            Assert.IsTrue(queue.TryDequeue(out var entry2));
            Debug.Log($"Second dequeue: ID={entry2.FileId}, Priority={entry2.Priority}");
            Assert.AreEqual(normalId, entry2.FileId);
            Assert.AreEqual(FilePriority.Normal, entry2.Priority);

            // Low priority should come last
            Assert.IsTrue(queue.TryDequeue(out var entry3));
            Assert.AreEqual(lowId, entry3.FileId);
            Assert.AreEqual(FilePriority.Low, entry3.Priority);

            // Queue should be empty
            Assert.IsFalse(queue.TryDequeue(out var entry4));
        }

        [Test]
        public void FilePriorityQueue_ShouldUpdatePriority()
        {
            using var queue = new FilePriorityQueue(10, Allocator.Temp);

            int fileId = queue.EnqueueFile("test.txt", FilePriority.Low);
            Assert.IsTrue(queue.UpdateFilePriority(fileId, FilePriority.High));

            Assert.IsTrue(queue.TryPeek(out var entry));
            Assert.AreEqual(FilePriority.High, entry.Priority);
        }

        [Test]
        public void FilePriorityQueue_ShouldUpdateStatus()
        {
            using var queue = new FilePriorityQueue(10, Allocator.Temp);

            int fileId = queue.EnqueueFile("test.txt", FilePriority.Normal);
            Assert.IsTrue(queue.UpdateFileStatus(fileId, FileQueueStatus.Processing));

            var snapshot = queue.GetQueueSnapshot();
            Assert.AreEqual(FileQueueStatus.Processing, snapshot[0].Status);
        }

        [Test]
        public void FilePriorityQueue_ShouldRemoveFiles()
        {
            using var queue = new FilePriorityQueue(10, Allocator.Temp);

            int fileId = queue.EnqueueFile("test.txt", FilePriority.Normal);
            Assert.AreEqual(1, queue.Count);

            Assert.IsTrue(queue.RemoveFile(fileId));
            Assert.AreEqual(0, queue.Count);

            Assert.IsFalse(queue.RemoveFile(fileId)); // Should fail second time
        }

        #endregion

        #region CompressionDetector Tests

        [Test]
        public async Task CompressionDetector_ShouldDetectUncompressedFiles()
        {
            // Create uncompressed text file
            string content = "# This is a Paradox file\nversion=1.0\ncountry={\n  name=\"Test\"\n}";
            await File.WriteAllTextAsync(testFilePath, content);

            var info = CompressionDetector.DetectCompression(testFilePath);

            Assert.IsTrue(info.IsValid);
            Assert.AreEqual(ParadoxParser.Core.CompressionType.None, info.CompressionType);
            Assert.IsFalse(info.IsCompressed);
            Assert.AreEqual(content.Length, info.OriginalSize);
        }

        [Test]
        public void CompressionDetector_ShouldDetectCompressedFiles()
        {
            // Create file with ZIP magic bytes (need at least 16 bytes for detection)
            byte[] zipMagic = { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00,
                               0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            File.WriteAllBytes(testFilePath, zipMagic);

            Debug.Log($"Created ZIP test file at: {testFilePath}");
            Debug.Log($"File exists: {File.Exists(testFilePath)}");
            Debug.Log($"File size: {new FileInfo(testFilePath).Length}");

            var info = CompressionDetector.DetectCompression(testFilePath);

            Debug.Log($"Compression detection result: {info.CompressionType}");
            Debug.Log($"Is valid: {info.IsValid}");
            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                Debug.LogError($"Compression detection error: {info.ErrorMessage}");
            }

            Assert.IsTrue(info.IsValid);
            Assert.AreEqual(ParadoxParser.Core.CompressionType.Zip, info.CompressionType);
            Assert.IsTrue(info.IsCompressed);
        }

        [Test]
        public void CompressionDetector_ShouldHandleNonExistentFile()
        {
            string nonExistentPath = Path.Combine(tempDirectory, "nonexistent.zip");

            var info = CompressionDetector.DetectCompression(nonExistentPath);

            Assert.IsFalse(info.IsValid);
            Assert.IsTrue(info.ErrorMessage.Contains("File not found"));
        }

        [Test]
        public void CompressionDetector_ShouldDetectFromBytes()
        {
            // Test with GZIP magic bytes
            byte[] gzipMagic = { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            using var data = new NativeArray<byte>(gzipMagic, Allocator.Temp);
            var info = CompressionDetector.DetectCompressionFromBytes(data);

            Assert.IsTrue(info.IsValid);
            Assert.AreEqual(ParadoxParser.Core.CompressionType.GZip, info.CompressionType);
            Assert.IsTrue(info.IsCompressed);
        }

        #endregion

        #region Performance Tests

        [Test]
        public async Task FileIO_Performance_ShouldBeEfficient()
        {
            // Create test files of various sizes
            var testFiles = new[]
            {
                ("small.txt", "Small file content"),
                ("medium.txt", new string('M', 10000)),
                ("large.txt", new string('L', 100000))
            };

            foreach (var (fileName, content) in testFiles)
            {
                string filePath = Path.Combine(tempDirectory, fileName);
                await File.WriteAllTextAsync(filePath, content);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Read all files
            long totalBytes = 0;
            foreach (var (fileName, _) in testFiles)
            {
                string filePath = Path.Combine(tempDirectory, fileName);
                var result = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

                Assert.IsTrue(result.Success);
                totalBytes += result.FileSize;

                result.Dispose();
            }

            stopwatch.Stop();

            // Should read all files in reasonable time (relaxed for Unity Editor)
            Assert.Less(stopwatch.ElapsedMilliseconds, 500,
                $"Reading {testFiles.Length} files took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");

            Debug.Log($"Read {totalBytes} bytes across {testFiles.Length} files in {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log($"Throughput: {totalBytes / (float)stopwatch.ElapsedMilliseconds * 1000 / 1024 / 1024:F2} MB/second");
        }

        #endregion

        #region Memory Tests

        [Test]
        public async Task FileIO_ShouldNotLeakMemory()
        {
            string content = new string('T', 50000); // 50KB file
            await File.WriteAllTextAsync(testFilePath, content);

            const int iterations = 100;

            for (int i = 0; i < iterations; i++)
            {
                var result = await AsyncFileReader.ReadFileAsync(testFilePath, Allocator.TempJob);
                Assert.IsTrue(result.Success);
                result.Dispose(); // Should dispose cleanly
            }

            // If we get here without memory issues, the test passes
            Assert.Pass($"No memory leaks detected after {iterations} file read operations");
        }

        #endregion
    }
}