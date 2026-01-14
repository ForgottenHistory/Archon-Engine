using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Map.Loading.Bitmaps;
using Map.Rendering;
using System.Threading.Tasks;

namespace Map.Loading
{
    /// <summary>
    /// Compatibility layer for legacy Map.Loading namespace
    /// Bridges old ProvinceMapLoader API to new ProvinceMapProcessor system
    /// </summary>
    public static class ProvinceMapLoader
    {
        /// <summary>
        /// Legacy LoadResult structure - now bridges to new system
        /// </summary>
        public struct LoadResult
        {
            public bool IsSuccess;
            public int ProvinceCount;
            public int Width;
            public int Height;
            public string ErrorMessage;
            public NativeHashMap<Color32, ushort> ColorToID;
            public NativeArray<ProvincePixel> ProvincePixels;

            public void Dispose()
            {
                if (ColorToID.IsCreated) ColorToID.Dispose();
                if (ProvincePixels.IsCreated) ProvincePixels.Dispose();
            }
        }

        /// <summary>
        /// Legacy ProvincePixel structure
        /// </summary>
        public struct ProvincePixel
        {
            public int2 Position;
            public ushort ProvinceID;
            public Color32 Color;
        }

        /// <summary>
        /// Legacy LoadProvinceMap method - now uses ProvinceMapProcessor internally
        /// </summary>
        public static LoadResult LoadProvinceMap(string bitmapPath, MapTextureManager textureManager, string definitionCsvPath = null)
        {
            // Run the async version synchronously for compatibility
            var task = LoadProvinceMapAsync(bitmapPath, textureManager, definitionCsvPath);
            task.Wait();
            return task.Result;
        }

        /// <summary>
        /// Async version using new ProvinceMapProcessor
        /// </summary>
        public static async Task<LoadResult> LoadProvinceMapAsync(string bitmapPath, MapTextureManager textureManager, string definitionCsvPath = null)
        {
            try
            {
                var processor = new ProvinceMapProcessor();
                var result = await processor.LoadProvinceMapAsync(bitmapPath, definitionCsvPath);

                if (!result.IsSuccess)
                {
                    return new LoadResult
                    {
                        IsSuccess = false,
                        ErrorMessage = result.ErrorMessage
                    };
                }

                // Convert new result format to legacy format
                var colorToID = new NativeHashMap<Color32, ushort>(result.ProvinceMappings.ColorToProvinceID.Count, Allocator.TempJob);
                var provincePixels = new NativeList<ProvincePixel>(result.BMPData.Width * result.BMPData.Height / 10, Allocator.Temp);

                // Convert color mappings
                var colorMappingEnumerator = result.ProvinceMappings.ColorToProvinceID.GetEnumerator();
                while (colorMappingEnumerator.MoveNext())
                {
                    int packedRGB = colorMappingEnumerator.Current.Key;
                    int provinceID = colorMappingEnumerator.Current.Value;

                    // Convert packed RGB to Color32
                    byte r = (byte)((packedRGB >> 16) & 0xFF);
                    byte g = (byte)((packedRGB >> 8) & 0xFF);
                    byte b = (byte)(packedRGB & 0xFF);
                    var color = new Color32(r, g, b, 255);

                    colorToID[color] = (ushort)provinceID;
                }

                // Sample some pixels for ProvincePixel array (for performance, don't convert all)
                var pixelData = result.BMPData.GetPixelData();
                int sampleStride = 10; // Sample every 10th pixel

                for (int y = 0; y < result.BMPData.Height; y += sampleStride)
                {
                    for (int x = 0; x < result.BMPData.Width; x += sampleStride)
                    {
                        if (pixelData.TryGetPixelRGB(x, y, out byte r, out byte g, out byte b))
                        {
                            var color = new Color32(r, g, b, 255);
                            if (colorToID.TryGetValue(color, out ushort provinceID))
                            {
                                provincePixels.Add(new ProvincePixel
                                {
                                    Position = new int2(x, y),
                                    ProvinceID = provinceID,
                                    Color = color
                                });
                            }
                        }
                    }
                }

                var legacyResult = new LoadResult
                {
                    IsSuccess = true,
                    ProvinceCount = result.HasDefinitions ? result.Definitions.AllDefinitions.Length : result.ProvinceMappings.ColorToProvinceID.Count,
                    Width = result.BMPData.Width,
                    Height = result.BMPData.Height,
                    ColorToID = colorToID,
                    ProvincePixels = provincePixels.AsArray()
                };

                // Clean up new result
                result.Dispose();
                provincePixels.Dispose();

                return legacyResult;
            }
            catch (System.Exception e)
            {
                return new LoadResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Exception in compatibility layer: {e.Message}"
                };
            }
        }

        /// <summary>
        /// Create error texture - compatibility method
        /// </summary>
        public static Texture2D CreateErrorTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color32[width * height];

            // Create red error pattern
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 0, 0, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}