using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages province color palette texture (256×1 RGBA32)
    /// Handles palette generation and color updates
    /// Extracted from MapTextureManager for single responsibility
    /// </summary>
    public class PaletteTextureManager
    {
        private readonly bool logCreation;

        private Texture2D provinceColorPalette;

        // Shader property ID
        private static readonly int ProvinceColorPaletteID = Shader.PropertyToID("_ProvinceColorPalette");

        public Texture2D ProvinceColorPalette => provinceColorPalette;

        public PaletteTextureManager(bool logCreation = true)
        {
            this.logCreation = logCreation;
        }

        /// <summary>
        /// Create province color palette texture
        /// </summary>
        public void CreatePalette()
        {
            provinceColorPalette = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            provinceColorPalette.name = "ProvinceColorPalette";
            provinceColorPalette.filterMode = FilterMode.Point;
            provinceColorPalette.wrapMode = TextureWrapMode.Clamp;
            provinceColorPalette.anisoLevel = 0;

            // Initialize with default colors
            var colors = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                colors[i] = GenerateDefaultPaletteColor(i);
            }

            provinceColorPalette.SetPixels32(colors);
            provinceColorPalette.Apply(false);

            if (logCreation)
            {
                ArchonLogger.LogMapInit("PaletteTextureManager: Created Province Color Palette 256×1 RGBA32");
            }
        }

        /// <summary>
        /// Generate a default color for palette index
        /// Uses HSV with golden angle for visually distinct colors
        /// </summary>
        private Color32 GenerateDefaultPaletteColor(int index)
        {
            if (index == 0) return new Color32(64, 64, 64, 255); // Dark gray for unowned

            // Golden angle distribution for good visual separation
            float hue = (index * 137.508f) % 360f;
            float saturation = 0.7f + (index % 3) * 0.1f;
            float value = 0.8f + (index % 2) * 0.2f;

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }

        /// <summary>
        /// Update single palette color
        /// </summary>
        public void SetPaletteColor(byte paletteIndex, Color32 color)
        {
            provinceColorPalette.SetPixel(paletteIndex, 0, color);
        }

        /// <summary>
        /// Update all palette colors at once
        /// </summary>
        public void SetPaletteColors(Color32[] colors)
        {
            if (colors.Length != 256)
            {
                ArchonLogger.LogError($"PaletteTextureManager: Palette colors must be exactly 256 elements, got {colors.Length}");
                return;
            }

            provinceColorPalette.SetPixels32(colors);
        }

        /// <summary>
        /// Apply palette changes
        /// </summary>
        public void ApplyChanges()
        {
            provinceColorPalette.Apply(false);
        }

        /// <summary>
        /// Bind palette to material
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(ProvinceColorPaletteID, provinceColorPalette);

            if (logCreation)
            {
                ArchonLogger.LogMapInit("PaletteTextureManager: Bound palette texture to material");
            }
        }

        /// <summary>
        /// Release palette texture
        /// </summary>
        public void Release()
        {
            if (provinceColorPalette != null) Object.DestroyImmediate(provinceColorPalette);
        }
    }
}
