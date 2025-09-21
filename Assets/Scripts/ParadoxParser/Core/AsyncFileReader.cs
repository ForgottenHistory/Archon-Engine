using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace ParadoxParser.Core
{
    public static class AsyncFileReader
    {
        public static async Task<FileReadResult> ReadFileAsync(string filePath, Allocator allocator)
        {
            NativeArray<byte> buffer = default;
            try
            {
                if (!File.Exists(filePath))
                {
                    return new FileReadResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}",
                        Data = default
                    };
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > int.MaxValue)
                {
                    return new FileReadResult
                    {
                        Success = false,
                        ErrorMessage = $"File too large: {fileInfo.Length} bytes",
                        Data = default
                    };
                }

                int fileSize = (int)fileInfo.Length;
                buffer = new NativeArray<byte>(fileSize, allocator);

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 65536, useAsync: true))
                {
                    int totalRead = 0;

                    while (totalRead < fileSize)
                    {
                        int chunkSize = Math.Min(65536, fileSize - totalRead);
                        byte[] tempBuffer = new byte[chunkSize];

                        int bytesRead = await fileStream.ReadAsync(tempBuffer, 0, chunkSize);
                        if (bytesRead == 0) break;

                        // Copy to native buffer safely
                        NativeArray<byte>.Copy(tempBuffer, 0, buffer, totalRead, bytesRead);
                        totalRead += bytesRead;
                    }
                }

                return new FileReadResult
                {
                    Success = true,
                    ErrorMessage = null,
                    Data = buffer,
                    FilePath = filePath,
                    FileSize = fileSize
                };
            }
            catch (Exception ex)
            {
                // Dispose buffer if it was allocated before the exception
                try
                {
                    if (buffer.IsCreated)
                    {
                        buffer.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Buffer was already disposed
                }

                return new FileReadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Data = default
                };
            }
        }

        public static async Task<FileReadResult> ReadFileChunkAsync(string filePath, long offset, int chunkSize, Allocator allocator)
        {
            NativeArray<byte> buffer = default;
            try
            {
                if (!File.Exists(filePath))
                {
                    return new FileReadResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}",
                        Data = default
                    };
                }

                buffer = new NativeArray<byte>(chunkSize, allocator);

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 65536, useAsync: true))
                {
                    fileStream.Seek(offset, SeekOrigin.Begin);

                    int totalRead = 0;

                    while (totalRead < chunkSize)
                    {
                        int remainingBytes = chunkSize - totalRead;
                        int readSize = Math.Min(65536, remainingBytes);
                        byte[] tempBuffer = new byte[readSize];

                        int bytesRead = await fileStream.ReadAsync(tempBuffer, 0, readSize);
                        if (bytesRead == 0) break;

                        // Copy to native buffer safely
                        NativeArray<byte>.Copy(tempBuffer, 0, buffer, totalRead, bytesRead);
                        totalRead += bytesRead;
                    }

                    // Resize buffer if we read less than expected
                    if (totalRead < chunkSize)
                    {
                        var resizedBuffer = new NativeArray<byte>(totalRead, allocator);
                        NativeArray<byte>.Copy(buffer, resizedBuffer, totalRead);
                        buffer.Dispose();
                        buffer = resizedBuffer;
                    }
                }

                return new FileReadResult
                {
                    Success = true,
                    ErrorMessage = null,
                    Data = buffer,
                    FilePath = filePath,
                    FileSize = buffer.Length
                };
            }
            catch (Exception ex)
            {
                // Dispose buffer if it was allocated before the exception
                try
                {
                    if (buffer.IsCreated)
                    {
                        buffer.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Buffer was already disposed
                }

                return new FileReadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Data = default
                };
            }
        }

        public static FileInfo[] GetFilesBatch(string directoryPath, string searchPattern = "*", int maxFiles = 1000)
        {
            try
            {
                var directory = new DirectoryInfo(directoryPath);
                if (!directory.Exists) return new FileInfo[0];

                var files = directory.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);
                if (files.Length > maxFiles)
                {
                    Array.Resize(ref files, maxFiles);
                }

                return files;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AsyncFileReader] Error scanning directory: {ex.Message}");
                return new FileInfo[0];
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileReadResult : IDisposable
    {
        public bool Success;
        public string ErrorMessage;
        public NativeArray<byte> Data;
        public string FilePath;
        public int FileSize;

        public bool IsValid => Success && Data.IsCreated;

        public void Dispose()
        {
            if (Data.IsCreated)
            {
                Data.Dispose();
            }
        }
    }

    public struct FileReadJob : IJob
    {
        public FixedString512Bytes FilePath;
        public NativeArray<byte> OutputBuffer;
        public NativeReference<int> BytesRead;
        public NativeReference<bool> Success;

        public void Execute()
        {
            try
            {
                string path = FilePath.ToString();
                if (!File.Exists(path))
                {
                    Success.Value = false;
                    return;
                }

                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int totalRead = 0;
                    int bufferSize = OutputBuffer.Length;
                    byte[] tempBuffer = new byte[Math.Min(4096, bufferSize)];

                    unsafe
                    {
                        byte* outputPtr = (byte*)OutputBuffer.GetUnsafePtr();

                        while (totalRead < bufferSize)
                        {
                            int chunkSize = Math.Min(tempBuffer.Length, bufferSize - totalRead);
                            int bytesRead = fileStream.Read(tempBuffer, 0, chunkSize);

                            if (bytesRead == 0) break;

                            for (int i = 0; i < bytesRead; i++)
                            {
                                outputPtr[totalRead + i] = tempBuffer[i];
                            }

                            totalRead += bytesRead;
                        }
                    }

                    BytesRead.Value = totalRead;
                    Success.Value = true;
                }
            }
            catch (Exception)
            {
                Success.Value = false;
                BytesRead.Value = 0;
            }
        }
    }
}