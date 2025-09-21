using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public unsafe struct SimpleStreamingFileSystem : IDisposable
    {
        private NativeHashMap<int, SimpleStreamingFile> m_StreamingFiles;
        private NativeQueue<int> m_AvailableIds;
        private int m_NextId;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;

        public SimpleStreamingFileSystem(int maxFiles, Allocator allocator)
        {
            m_StreamingFiles = new NativeHashMap<int, SimpleStreamingFile>(maxFiles, allocator);
            m_AvailableIds = new NativeQueue<int>(allocator);
            m_NextId = 1;
            m_IsCreated = true;
        }

        public int OpenFile(string filePath)
        {
            if (!File.Exists(filePath)) return -1;

            var fileInfo = new FileInfo(filePath);
            int fileId = GetNextId();

            var streamingFile = new SimpleStreamingFile
            {
                FileSize = fileInfo.Length,
                Position = 0,
                IsValid = true
            };

            m_StreamingFiles[fileId] = streamingFile;
            return fileId;
        }

        public void CloseFile(int fileId)
        {
            if (m_StreamingFiles.ContainsKey(fileId))
            {
                m_StreamingFiles.Remove(fileId);
                ReturnId(fileId);
            }
        }

        public long GetFileSize(int fileId)
        {
            if (m_StreamingFiles.TryGetValue(fileId, out var file))
            {
                return file.FileSize;
            }
            return -1;
        }

        public long GetPosition(int fileId)
        {
            if (m_StreamingFiles.TryGetValue(fileId, out var file))
            {
                return file.Position;
            }
            return -1;
        }

        public bool Seek(int fileId, long position)
        {
            if (m_StreamingFiles.TryGetValue(fileId, out var file))
            {
                if (position >= 0 && position <= file.FileSize)
                {
                    file.Position = position;
                    m_StreamingFiles[fileId] = file;
                    return true;
                }
            }
            return false;
        }

        private int GetNextId()
        {
            if (m_AvailableIds.Count > 0)
            {
                return m_AvailableIds.Dequeue();
            }
            return m_NextId++;
        }

        private void ReturnId(int id)
        {
            m_AvailableIds.Enqueue(id);
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                if (m_StreamingFiles.IsCreated)
                    m_StreamingFiles.Dispose();
                if (m_AvailableIds.IsCreated)
                    m_AvailableIds.Dispose();

                m_IsCreated = false;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleStreamingFile
    {
        public long FileSize;
        public long Position;
        public bool IsValid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleStreamingSegment
    {
        public long Offset;
        public long Size;
        public bool IsLoaded;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SimpleStreamingResult : IDisposable
    {
        public bool Success;
        public NativeArray<byte> Data;
        public int BytesRead;
        public int ErrorCode;

        public bool IsValid => Success && Data.IsCreated;

        public void Dispose()
        {
            if (Data.IsCreated)
            {
                Data.Dispose();
            }
        }
    }

    public static class SimpleStreamingUtilities
    {
        public static SimpleStreamingResult ReadFileChunk(string filePath, long offset, int size, Allocator allocator)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new SimpleStreamingResult
                    {
                        Success = false,
                        ErrorCode = 404,
                        Data = default
                    };
                }

                var fileInfo = new FileInfo(filePath);
                if (offset >= fileInfo.Length)
                {
                    return new SimpleStreamingResult
                    {
                        Success = false,
                        ErrorCode = 416,
                        Data = default
                    };
                }

                long actualSize = Math.Min(size, fileInfo.Length - offset);
                var buffer = new NativeArray<byte>((int)actualSize, allocator);

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fileStream.Seek(offset, SeekOrigin.Begin);

                    unsafe
                    {
                        byte* bufferPtr = (byte*)buffer.GetUnsafePtr();
                        int totalRead = 0;

                        while (totalRead < actualSize)
                        {
                            int chunkSize = Math.Min(4096, (int)actualSize - totalRead);
                            byte[] tempBuffer = new byte[chunkSize];

                            int bytesRead = fileStream.Read(tempBuffer, 0, chunkSize);
                            if (bytesRead == 0) break;

                            for (int i = 0; i < bytesRead; i++)
                            {
                                bufferPtr[totalRead + i] = tempBuffer[i];
                            }

                            totalRead += bytesRead;
                        }

                        return new SimpleStreamingResult
                        {
                            Success = true,
                            Data = buffer,
                            BytesRead = totalRead,
                            ErrorCode = 0
                        };
                    }
                }
            }
            catch (Exception)
            {
                return new SimpleStreamingResult
                {
                    Success = false,
                    ErrorCode = 500,
                    Data = default
                };
            }
        }

        public static SimpleStreamingResult ReadEntireFile(string filePath, Allocator allocator)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new SimpleStreamingResult
                    {
                        Success = false,
                        ErrorCode = 404,
                        Data = default
                    };
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > int.MaxValue)
                {
                    return new SimpleStreamingResult
                    {
                        Success = false,
                        ErrorCode = 413,
                        Data = default
                    };
                }

                return ReadFileChunk(filePath, 0, (int)fileInfo.Length, allocator);
            }
            catch (Exception)
            {
                return new SimpleStreamingResult
                {
                    Success = false,
                    ErrorCode = 500,
                    Data = default
                };
            }
        }
    }
}