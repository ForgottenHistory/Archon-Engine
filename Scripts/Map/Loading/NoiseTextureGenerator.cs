using UnityEngine;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Generates tileable noise textures for anti-tiling effects
    /// Used by terrain detail mapping to break up texture repetition
    ///
    /// Architecture:
    /// - Output: Tileable noise texture (R8_UNorm grayscale)
    /// - Method: Simple value noise with tiling support
    /// - Purpose: Input for Inigo Quilez anti-tiling technique
    /// </summary>
    public static class NoiseTextureGenerator
    {
        /// <summary>
        /// Generate tileable noise texture for anti-tiling
        /// </summary>
        /// <param name="size">Texture size (power of 2 recommended)</param>
        /// <param name="logProgress">Enable progress logging</param>
        /// <returns>Tileable noise texture (R8_UNorm format)</returns>
        public static Texture2D GenerateNoiseTexture(int size = 256, bool logProgress = true)
        {
            if (logProgress)
            {
                ArchonLogger.Log($"NoiseTextureGenerator: Starting generation {size}x{size}", "map_initialization");
            }

            // Create R8 texture with explicit GraphicsFormat
            var noiseTexture = new Texture2D(
                size,
                size,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain
            );
            noiseTexture.name = "DetailNoise_Texture";
            noiseTexture.filterMode = FilterMode.Bilinear;  // Smooth filtering for noise
            noiseTexture.wrapMode = TextureWrapMode.Repeat;  // Tiling required
            noiseTexture.anisoLevel = 1;

            // Allocate output array
            byte[] noisePixels = new byte[size * size];

            // Generate simple Perlin noise (not perfectly tileable, but good enough)
            float scale = 0.1f;  // Noise frequency
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Use Perlin noise with multiple octaves
                    float noise = 0f;
                    float amplitude = 1f;
                    float frequency = 1f;
                    float maxValue = 0f;

                    // 4 octaves
                    for (int octave = 0; octave < 4; octave++)
                    {
                        float sampleX = x * scale * frequency;
                        float sampleY = y * scale * frequency;
                        float perlin = Mathf.PerlinNoise(sampleX, sampleY);
                        noise += perlin * amplitude;
                        maxValue += amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2f;
                    }

                    // Normalize to [0,1] and convert to byte
                    noise /= maxValue;
                    byte pixelValue = (byte)(noise * 255f);

                    noisePixels[y * size + x] = pixelValue;
                }
            }

            // Upload raw byte data to R8 texture
            noiseTexture.SetPixelData(noisePixels, 0);
            noiseTexture.Apply(true);  // Generate mipmaps

            if (logProgress)
            {
                ArchonLogger.Log($"NoiseTextureGenerator: Complete - {size}x{size} R8_UNorm noise texture generated", "map_initialization");
            }

            return noiseTexture;
        }
    }
}
