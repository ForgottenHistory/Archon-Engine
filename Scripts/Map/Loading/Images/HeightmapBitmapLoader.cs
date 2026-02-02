using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Map.Rendering;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Loads heightmap.png (or heightmap.bmp) and populates the heightmap texture.
    /// Uses ImageParser for unified BMP/PNG support with PNG preferred.
    /// Height values: 0-255 (8-bit) → written directly to R8 texture.
    /// Supports raw pixel cache to skip PNG decompression on subsequent loads.
    /// </summary>
    public class HeightmapImageLoader
    {
        private MapTextureManager textureManager;
        private bool logProgress;

        // Raw pixel cache format (same as ProvinceMapProcessor):
        // [0-3]   Magic: "RPXL"
        // [4-7]   Width (int32 LE)
        // [8-11]  Height (int32 LE)
        // [12]    BytesPerPixel (byte)
        // [13]    ColorType (byte)
        // [14]    BitDepth (byte)
        // [15]    Reserved (byte)
        // [16..]  Raw pixel data
        private const int CACHE_HEADER_SIZE = 16;
        private static readonly byte[] CACHE_MAGIC = { 0x52, 0x50, 0x58, 0x4C }; // "RPXL"

        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
        }

        /// <summary>
        /// Load heightmap image and populate heightmap texture.
        /// Tries heightmap.png first, falls back to heightmap.bmp.
        /// Uses raw pixel cache to skip PNG decompression on subsequent loads.
        /// </summary>
        public void LoadAndPopulate(string mapDirectory)
        {
            if (textureManager == null || textureManager.HeightmapTexture == null)
            {
                ArchonLogger.LogError("HeightmapImageLoader: Texture manager or heightmap texture not available", "map_initialization");
                return;
            }

            // Try PNG first, then BMP
            string pngPath = System.IO.Path.Combine(mapDirectory, "heightmap.png");
            string bmpPath = System.IO.Path.Combine(mapDirectory, "heightmap.bmp");

            string imagePath = null;
            if (System.IO.File.Exists(pngPath))
            {
                imagePath = pngPath;
            }
            else if (System.IO.File.Exists(bmpPath))
            {
                imagePath = bmpPath;
            }
            else
            {
                ArchonLogger.LogWarning($"HeightmapImageLoader: No heightmap image found (tried {pngPath} and {bmpPath}), using defaults", "map_initialization");
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Try raw pixel cache first
            string cachePath = imagePath + ".pixels";
            NativeArray<byte> rawPixels = default;
            int imgWidth = 0, imgHeight = 0, bytesPerPixel = 0;
            bool cacheHit = TryLoadPixelCache(imagePath, cachePath, out rawPixels, out imgWidth, out imgHeight, out bytesPerPixel);

            if (!cacheHit)
            {
                // Cache miss — decompress PNG
                byte[] fileBytes = System.IO.File.ReadAllBytes(imagePath);
                var fileData = new NativeArray<byte>(fileBytes, Allocator.Temp);

                try
                {
                    var pixelData = ImageParser.Parse(fileData, Allocator.Persistent);

                    if (!pixelData.IsSuccess)
                    {
                        ArchonLogger.LogError($"HeightmapImageLoader: Failed to parse {imagePath}", "map_initialization");
                        return;
                    }

                    imgWidth = pixelData.Header.Width;
                    imgHeight = pixelData.Header.Height;

                    if (pixelData.Format == ImageParser.ImageFormat.PNG)
                    {
                        bytesPerPixel = pixelData.PNGData.Header.BytesPerPixel;
                        rawPixels = pixelData.PNGData.DecodedPixels;

                        // Save cache for next time
                        SavePixelCache(cachePath, pixelData);

                        long cacheMs = sw.ElapsedMilliseconds;
                        ArchonLogger.Log($"HeightmapImageLoader: Cache miss — decoded {imgWidth}x{imgHeight} PNG in {cacheMs}ms, saved cache", "map_rendering");
                    }
                    else
                    {
                        // BMP fallback — extract pixels via TryGetPixelRGB into temp array
                        bytesPerPixel = 3;
                        rawPixels = new NativeArray<byte>(imgWidth * imgHeight * 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        unsafe
                        {
                            byte* dst = (byte*)rawPixels.GetUnsafePtr();
                            for (int y = 0; y < imgHeight; y++)
                            {
                                for (int x = 0; x < imgWidth; x++)
                                {
                                    int idx = (y * imgWidth + x) * 3;
                                    if (ImageParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                                    {
                                        dst[idx] = r;
                                        dst[idx + 1] = g;
                                        dst[idx + 2] = b;
                                    }
                                    else
                                    {
                                        dst[idx] = 128; // mid-height default
                                        dst[idx + 1] = 128;
                                        dst[idx + 2] = 128;
                                    }
                                }
                            }
                        }
                        pixelData.Dispose();
                    }
                }
                finally
                {
                    fileData.Dispose();
                }
            }
            else
            {
                long cacheMs = sw.ElapsedMilliseconds;
                ArchonLogger.Log($"HeightmapImageLoader: Cache hit — read {imgWidth}x{imgHeight} pixels in {cacheMs}ms from {cachePath}", "map_rendering");
            }

            // Populate R8 texture directly using GetRawTextureData<byte>()
            PopulateTextureDirect(rawPixels, imgWidth, imgHeight, bytesPerPixel);

            // Dispose raw pixels (unless they're still owned by a PNG parse result we already cached)
            if (rawPixels.IsCreated)
                rawPixels.Dispose();

            long totalMs = sw.ElapsedMilliseconds;
            ArchonLogger.Log($"HeightmapImageLoader: Complete in {totalMs}ms (cache {(cacheHit ? "hit" : "miss")})", "map_rendering");
        }

        /// <summary>
        /// Populate R8 heightmap texture directly from raw pixel bytes.
        /// Writes 1 byte per pixel via GetRawTextureData — zero managed allocation.
        /// </summary>
        private void PopulateTextureDirect(NativeArray<byte> rawPixels, int imgWidth, int imgHeight, int bytesPerPixel)
        {
            var heightmapTexture = textureManager.HeightmapTexture;
            int texWidth = heightmapTexture.width;
            int texHeight = heightmapTexture.height;

            // Get direct access to R8 texture buffer (1 byte per pixel)
            var texData = heightmapTexture.GetRawTextureData<byte>();

            int copyWidth = System.Math.Min(texWidth, imgWidth);
            int copyHeight = System.Math.Min(texHeight, imgHeight);

            unsafe
            {
                byte* src = (byte*)rawPixels.GetUnsafeReadOnlyPtr();
                byte* dst = (byte*)texData.GetUnsafePtr();

                // Extract R channel from RGB/RGBA source into R8 destination
                for (int y = 0; y < copyHeight; y++)
                {
                    int srcRowStart = y * imgWidth * bytesPerPixel;
                    int dstRowStart = y * texWidth;

                    for (int x = 0; x < copyWidth; x++)
                    {
                        // Height = R channel of source pixel
                        dst[dstRowStart + x] = src[srcRowStart + x * bytesPerPixel];
                    }
                }

                // Fill remaining rows with mid-height if texture is taller than image
                if (copyHeight < texHeight)
                {
                    byte* fillStart = dst + copyHeight * texWidth;
                    UnsafeUtility.MemSet(fillStart, 128, (texHeight - copyHeight) * texWidth);
                }

                // Fill remaining columns if texture is wider than image
                if (copyWidth < texWidth)
                {
                    for (int y = 0; y < copyHeight; y++)
                    {
                        byte* rowFillStart = dst + y * texWidth + copyWidth;
                        UnsafeUtility.MemSet(rowFillStart, 128, texWidth - copyWidth);
                    }
                }
            }

            heightmapTexture.Apply(false);
        }

        private bool TryLoadPixelCache(string imagePath, string cachePath,
            out NativeArray<byte> rawPixels, out int width, out int height, out int bytesPerPixel)
        {
            rawPixels = default;
            width = 0;
            height = 0;
            bytesPerPixel = 0;

            if (!System.IO.File.Exists(cachePath))
                return false;

            // Invalidate if source image is newer than cache
            if (System.IO.File.GetLastWriteTimeUtc(imagePath) > System.IO.File.GetLastWriteTimeUtc(cachePath))
            {
                ArchonLogger.Log("HeightmapImageLoader: Pixel cache stale, will regenerate", "map_rendering");
                return false;
            }

            try
            {
                byte[] cacheBytes = System.IO.File.ReadAllBytes(cachePath);

                if (cacheBytes.Length < CACHE_HEADER_SIZE)
                    return false;

                // Validate magic
                if (cacheBytes[0] != CACHE_MAGIC[0] || cacheBytes[1] != CACHE_MAGIC[1] ||
                    cacheBytes[2] != CACHE_MAGIC[2] || cacheBytes[3] != CACHE_MAGIC[3])
                    return false;

                width = cacheBytes[4] | (cacheBytes[5] << 8) | (cacheBytes[6] << 16) | (cacheBytes[7] << 24);
                height = cacheBytes[8] | (cacheBytes[9] << 8) | (cacheBytes[10] << 16) | (cacheBytes[11] << 24);
                bytesPerPixel = cacheBytes[12];

                int expectedDataSize = width * height * bytesPerPixel;
                if (cacheBytes.Length != CACHE_HEADER_SIZE + expectedDataSize)
                {
                    ArchonLogger.LogWarning($"HeightmapImageLoader: Pixel cache size mismatch (expected {CACHE_HEADER_SIZE + expectedDataSize}, got {cacheBytes.Length})", "map_rendering");
                    return false;
                }

                // Single bulk copy from managed array to NativeArray
                rawPixels = new NativeArray<byte>(expectedDataSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                unsafe
                {
                    fixed (byte* src = &cacheBytes[CACHE_HEADER_SIZE])
                    {
                        UnsafeUtility.MemCpy(rawPixels.GetUnsafePtr(), src, expectedDataSize);
                    }
                }

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"HeightmapImageLoader: Failed to load pixel cache: {e.Message}", "map_rendering");
                return false;
            }
        }

        private void SavePixelCache(string cachePath, ImageParser.ImagePixelData pixelData)
        {
            if (pixelData.Format != ImageParser.ImageFormat.PNG || !pixelData.IsSuccess)
                return;

            try
            {
                var pngData = pixelData.PNGData;
                var header = pngData.Header;
                int dataSize = pngData.DecodedPixels.Length;

                using var stream = new System.IO.FileStream(cachePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 1024 * 1024);

                // Write header
                byte[] headerBuf = new byte[CACHE_HEADER_SIZE];
                headerBuf[0] = CACHE_MAGIC[0];
                headerBuf[1] = CACHE_MAGIC[1];
                headerBuf[2] = CACHE_MAGIC[2];
                headerBuf[3] = CACHE_MAGIC[3];
                headerBuf[4] = (byte)(header.Width);
                headerBuf[5] = (byte)(header.Width >> 8);
                headerBuf[6] = (byte)(header.Width >> 16);
                headerBuf[7] = (byte)(header.Width >> 24);
                headerBuf[8] = (byte)(header.Height);
                headerBuf[9] = (byte)(header.Height >> 8);
                headerBuf[10] = (byte)(header.Height >> 16);
                headerBuf[11] = (byte)(header.Height >> 24);
                headerBuf[12] = (byte)header.BytesPerPixel;
                headerBuf[13] = header.ColorType;
                headerBuf[14] = header.BitDepth;
                headerBuf[15] = 0; // Reserved

                stream.Write(headerBuf, 0, CACHE_HEADER_SIZE);

                // Write pixel data in 1MB chunks
                unsafe
                {
                    byte* src = (byte*)pngData.DecodedPixels.GetUnsafeReadOnlyPtr();
                    byte[] writeBuffer = new byte[1024 * 1024];
                    int totalWritten = 0;
                    while (totalWritten < dataSize)
                    {
                        int toWrite = System.Math.Min(writeBuffer.Length, dataSize - totalWritten);
                        fixed (byte* dst = writeBuffer)
                        {
                            UnsafeUtility.MemCpy(dst, src + totalWritten, toWrite);
                        }
                        stream.Write(writeBuffer, 0, toWrite);
                        totalWritten += toWrite;
                    }
                }

                ArchonLogger.Log($"HeightmapImageLoader: Saved pixel cache ({dataSize / (1024 * 1024)}MB) to {cachePath}", "map_rendering");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"HeightmapImageLoader: Failed to save pixel cache: {e.Message}", "map_rendering");
            }
        }
    }

    /// <summary>
    /// Legacy alias for HeightmapImageLoader for backward compatibility.
    /// </summary>
    public class HeightmapBitmapLoader : HeightmapImageLoader { }
}
