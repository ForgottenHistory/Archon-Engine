using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Map.Rendering;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Loads terrain.png (or terrain.bmp) and populates the terrain texture.
    /// Uses ImageParser for unified BMP/PNG support.
    /// Terrain colors from the image are used directly - TerrainRGBLookup converts to indices later.
    /// Supports raw pixel cache to skip PNG decompression on subsequent loads.
    /// </summary>
    public class TerrainImageLoader
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
        /// Load terrain image and populate terrain texture.
        /// Tries terrain.png first, falls back to terrain.bmp.
        /// Uses raw pixel cache to skip PNG decompression on subsequent loads.
        /// </summary>
        public void LoadAndPopulate(string mapDirectory)
        {
            if (textureManager == null || textureManager.ProvinceTerrainTexture == null)
            {
                ArchonLogger.LogError("TerrainImageLoader: Texture manager or terrain texture not available", "map_initialization");
                return;
            }

            // Try PNG first, then BMP
            string pngPath = System.IO.Path.Combine(mapDirectory, "terrain.png");
            string bmpPath = System.IO.Path.Combine(mapDirectory, "terrain.bmp");

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
                ArchonLogger.LogWarning($"TerrainImageLoader: No terrain image found (tried {pngPath} and {bmpPath})", "map_initialization");
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
                        ArchonLogger.LogError($"TerrainImageLoader: Failed to parse {imagePath}", "map_initialization");
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
                        ArchonLogger.Log($"TerrainImageLoader: Cache miss — decoded {imgWidth}x{imgHeight} PNG in {cacheMs}ms, saved cache", "map_rendering");
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
                                        // Default grasslands RGB
                                        dst[idx] = 86;
                                        dst[idx + 1] = 124;
                                        dst[idx + 2] = 27;
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
                ArchonLogger.Log($"TerrainImageLoader: Cache hit — read {imgWidth}x{imgHeight} pixels in {cacheMs}ms from {cachePath}", "map_rendering");
            }

            // Populate RGBA32 terrain texture directly using GetRawTextureData<byte>()
            PopulateTextureDirect(rawPixels, imgWidth, imgHeight, bytesPerPixel);

            // Dispose raw pixels
            if (rawPixels.IsCreated)
                rawPixels.Dispose();

            long populateMs = sw.ElapsedMilliseconds;

            // Generate terrain type texture from colors
            textureManager.GenerateTerrainTypeTexture();

            long totalMs = sw.ElapsedMilliseconds;
            ArchonLogger.Log($"TerrainImageLoader: Complete in {totalMs}ms (populate: {populateMs}ms, terrainTypeGen: {totalMs - populateMs}ms, cache {(cacheHit ? "hit" : "miss")})", "map_rendering");
        }

        /// <summary>
        /// Populate RGBA32 terrain texture directly from raw pixel bytes.
        /// Writes 4 bytes per pixel via GetRawTextureData — zero managed allocation.
        /// </summary>
        private void PopulateTextureDirect(NativeArray<byte> rawPixels, int imgWidth, int imgHeight, int bytesPerPixel)
        {
            var terrainTexture = textureManager.ProvinceTerrainTexture;
            int texWidth = terrainTexture.width;
            int texHeight = terrainTexture.height;

            // Get direct access to RGBA32 texture buffer (4 bytes per pixel)
            var texData = terrainTexture.GetRawTextureData<byte>();

            int copyWidth = System.Math.Min(texWidth, imgWidth);
            int copyHeight = System.Math.Min(texHeight, imgHeight);

            // Default grasslands color for fill
            byte defaultR = 86, defaultG = 124, defaultB = 27;

            unsafe
            {
                byte* src = (byte*)rawPixels.GetUnsafeReadOnlyPtr();
                byte* dst = (byte*)texData.GetUnsafePtr();

                // Copy RGB from source into RGBA32 destination
                for (int y = 0; y < copyHeight; y++)
                {
                    int srcRowStart = y * imgWidth * bytesPerPixel;
                    int dstRowStart = y * texWidth * 4;

                    for (int x = 0; x < copyWidth; x++)
                    {
                        int srcIdx = srcRowStart + x * bytesPerPixel;
                        int dstIdx = dstRowStart + x * 4;
                        dst[dstIdx] = src[srcIdx];         // R
                        dst[dstIdx + 1] = src[srcIdx + 1]; // G
                        dst[dstIdx + 2] = src[srcIdx + 2]; // B
                        dst[dstIdx + 3] = 255;             // A
                    }

                    // Fill remaining columns with default color
                    for (int x = copyWidth; x < texWidth; x++)
                    {
                        int dstIdx = dstRowStart + x * 4;
                        dst[dstIdx] = defaultR;
                        dst[dstIdx + 1] = defaultG;
                        dst[dstIdx + 2] = defaultB;
                        dst[dstIdx + 3] = 255;
                    }
                }

                // Fill remaining rows with default color
                for (int y = copyHeight; y < texHeight; y++)
                {
                    int dstRowStart = y * texWidth * 4;
                    for (int x = 0; x < texWidth; x++)
                    {
                        int dstIdx = dstRowStart + x * 4;
                        dst[dstIdx] = defaultR;
                        dst[dstIdx + 1] = defaultG;
                        dst[dstIdx + 2] = defaultB;
                        dst[dstIdx + 3] = 255;
                    }
                }
            }

            terrainTexture.Apply(false);
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
                ArchonLogger.Log("TerrainImageLoader: Pixel cache stale, will regenerate", "map_rendering");
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
                    ArchonLogger.LogWarning($"TerrainImageLoader: Pixel cache size mismatch (expected {CACHE_HEADER_SIZE + expectedDataSize}, got {cacheBytes.Length})", "map_rendering");
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
                ArchonLogger.LogWarning($"TerrainImageLoader: Failed to load pixel cache: {e.Message}", "map_rendering");
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

                ArchonLogger.Log($"TerrainImageLoader: Saved pixel cache ({dataSize / (1024 * 1024)}MB) to {cachePath}", "map_rendering");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"TerrainImageLoader: Failed to save pixel cache: {e.Message}", "map_rendering");
            }
        }
    }
}
