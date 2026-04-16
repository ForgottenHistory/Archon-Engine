using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Core;
using Core.Registries;
using Map.MapModes;
using Utils;
using TerrainData = Core.Registries.TerrainData;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Updates terrain colors across all related files and GPU textures.
    /// Handles: terrain.json5 (color definition), terrain.png (pixel data),
    /// .pixels cache (invalidation), in-memory TerrainData, and GPU palette.
    ///
    /// terrain.png patching is deferred to after play mode exits because Texture2D
    /// operations during play mode allocate GPU memory that corrupts active render textures.
    /// </summary>
    public static class TerrainColorPatcher
    {
        // Deferred terrain.png patches — flushed after play mode exits
        private static readonly List<(Color32 oldColor, Color32 newColor, string dataDirectory)> deferredImagePatches = new();

        /// <summary>
        /// Whether there are deferred terrain.png patches waiting to be flushed.
        /// </summary>
        public static bool HasDeferredPatches => deferredImagePatches.Count > 0;

        /// <summary>
        /// Flush all deferred terrain.png patches. Call AFTER exiting play mode
        /// (e.g., from EditorApplication.playModeStateChanged callback).
        /// </summary>
        public static void FlushDeferredImagePatches()
        {
            if (deferredImagePatches.Count == 0) return;

            ArchonLogger.Log($"TerrainColorPatcher: Flushing {deferredImagePatches.Count} deferred terrain.png patches", "starter_kit");

            foreach (var patch in deferredImagePatches)
            {
                string mapDir = Path.Combine(patch.dataDirectory, "map");
                PatchTerrainImage(patch.oldColor, patch.newColor, mapDir);
            }

            deferredImagePatches.Clear();
        }
        /// <summary>
        /// Live preview: update in-memory TerrainData and GPU palette only.
        /// No disk writes — avoids Unity reimport which disrupts rendering.
        /// </summary>
        public static bool ApplyColorLive(
            TerrainData terrain,
            Color32 newColor,
            MapModeDataTextures dataTextures)
        {
            if (terrain == null) return false;

            // Update in-memory TerrainData
            terrain.ColorR = newColor.r;
            terrain.ColorG = newColor.g;
            terrain.ColorB = newColor.b;

            // Update GPU terrain color palette
            if (dataTextures?.TerrainColorPalette != null && terrain.TerrainId < 32)
            {
                dataTextures.TerrainColorPalette.SetPixel(terrain.TerrainId, 0, newColor);
                dataTextures.TerrainColorPalette.Apply(false);
            }

            ArchonLogger.Log(
                $"TerrainColorPatcher: Live preview '{terrain.Key}' → ({newColor.r},{newColor.g},{newColor.b})",
                "starter_kit");

            return true;
        }

        /// <summary>
        /// Save all pending terrain color changes to disk: terrain.json5 + terrain.png + cache invalidation.
        /// Call this from the "Save to Disk" button, NOT on every color tweak.
        /// </summary>
        public static bool SaveColorToDisk(
            TerrainData terrain,
            Color32 oldColor,
            Color32 newColor,
            string dataDirectory)
        {
            if (terrain == null) return false;

            // Update terrain.json5
            if (!PatchTerrainJson5(terrain.Key, newColor, dataDirectory))
            {
                ArchonLogger.LogError($"TerrainColorPatcher: Failed to patch terrain.json5 for '{terrain.Key}'", "starter_kit");
                return false;
            }

            // Patch the .pixels cache directly (pure CPU, no Texture2D GPU allocation).
            // The engine loads from cache when it's newer than the source image.
            // We ensure a terrain.png exists in the override directory so the engine
            // resolves there and finds our patched cache alongside it.
            string overrideMapDir = Path.Combine(dataDirectory, "map");
            EnsureTerrainImageInOverride(overrideMapDir);
            PatchPixelsCache(oldColor, newColor, overrideMapDir);

            ArchonLogger.Log(
                $"TerrainColorPatcher: Saved '{terrain.Key}' color ({newColor.r},{newColor.g},{newColor.b}) to disk",
                "starter_kit");

            return true;
        }

        /// <summary>
        /// Patch the color array for a terrain key in terrain.json5.
        /// Finds the terrain key's block, then replaces the color values on the next color: line.
        /// </summary>
        private static bool PatchTerrainJson5(string terrainKey, Color32 newColor, string dataDirectory)
        {
            string path = Path.Combine(dataDirectory, "map", "terrain.json5");

            // If the file doesn't exist in the override directory, copy from base
            if (!File.Exists(path))
            {
                if (!Core.Modding.DataFileResolver.IsInitialized) return false;

                string basePath = Path.Combine(Core.Modding.DataFileResolver.BaseDirectory, "map", "terrain.json5");
                if (!File.Exists(basePath)) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(basePath, path);
            }

            var lines = File.ReadAllLines(path);
            var colorLineRegex = new Regex(@"^(\s*color\s*:\s*\[)\s*\d+\s*,\s*\d+\s*,\s*\d+\s*(\].*)$");

            // Find the line containing "terrainKey:" then patch the next "color:" line after it
            bool foundKey = false;
            bool patched = false;

            var colorValueRegex = new Regex(@"\[\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\]");

            for (int i = 0; i < lines.Length; i++)
            {
                // Look for the terrain key definition (e.g., "    mountain: {")
                if (!foundKey && lines[i].TrimStart().StartsWith(terrainKey + ":"))
                {
                    foundKey = true;
                    continue;
                }

                // After finding the key, patch the first color: line we encounter
                if (foundKey && lines[i].TrimStart().StartsWith("color"))
                {
                    lines[i] = colorValueRegex.Replace(lines[i],
                        $"[{newColor.r}, {newColor.g}, {newColor.b}]");
                    patched = true;
                    break;
                }

                // If we hit another terrain key block before finding color:, stop
                if (foundKey && lines[i].TrimStart().Contains(": {") && !lines[i].TrimStart().StartsWith("color"))
                {
                    break;
                }
            }

            if (!patched)
            {
                ArchonLogger.LogWarning($"TerrainColorPatcher: Could not find color for '{terrainKey}' in terrain.json5", "starter_kit");
                return false;
            }

            File.WriteAllLines(path, lines);
            return true;
        }

        /// <summary>
        /// Ensure terrain.png and its .pixels cache exist in the override directory.
        /// Copies from base if not present.
        /// </summary>
        private static void EnsureTerrainImageInOverride(string overrideMapDir)
        {
            if (!Core.Modding.DataFileResolver.IsInitialized) return;

            string overridePng = Path.Combine(overrideMapDir, "terrain.png");
            if (File.Exists(overridePng)) return;

            string basePng = Path.Combine(Core.Modding.DataFileResolver.BaseDirectory, "map", "terrain.png");
            if (!File.Exists(basePng)) return;

            Directory.CreateDirectory(overrideMapDir);
            File.Copy(basePng, overridePng);

            // Also copy the .pixels cache if it exists
            string baseCache = basePng + ".pixels";
            string overrideCache = overridePng + ".pixels";
            if (File.Exists(baseCache) && !File.Exists(overrideCache))
            {
                File.Copy(baseCache, overrideCache);
            }

            ArchonLogger.Log("TerrainColorPatcher: Copied terrain.png + cache to override directory", "starter_kit");
        }

        /// <summary>
        /// Patch the .pixels cache file directly with color swaps. Pure CPU, no GPU allocation.
        /// The engine loads from cache when cache timestamp > source image timestamp.
        /// Cache format: 16-byte header (magic "RPXL" + width + height + bpp + colorType + bitDepth + reserved)
        ///               then raw pixel bytes (RGB, 3 bytes per pixel for terrain).
        /// </summary>
        private static void PatchPixelsCache(Color32 oldColor, Color32 newColor, string mapDirectory)
        {
            // Look for terrain image in the specified directory
            string pngPath = Path.Combine(mapDirectory, "terrain.png");
            string bmpPath = Path.Combine(mapDirectory, "terrain.bmp");
            string imagePath = File.Exists(pngPath) ? pngPath : (File.Exists(bmpPath) ? bmpPath : null);
            if (imagePath == null)
            {
                ArchonLogger.LogWarning("TerrainColorPatcher: No terrain image found, skipping cache patch", "starter_kit");
                return;
            }

            string cachePath = imagePath + ".pixels";
            if (!File.Exists(cachePath))
            {
                ArchonLogger.LogWarning("TerrainColorPatcher: No .pixels cache found, skipping cache patch", "starter_kit");
                return;
            }

            // Read entire cache file
            byte[] cacheBytes = File.ReadAllBytes(cachePath);

            // Validate header
            const int HEADER_SIZE = 16;
            if (cacheBytes.Length < HEADER_SIZE ||
                cacheBytes[0] != 0x52 || cacheBytes[1] != 0x50 ||
                cacheBytes[2] != 0x58 || cacheBytes[3] != 0x4C) // "RPXL"
            {
                ArchonLogger.LogWarning("TerrainColorPatcher: Invalid .pixels cache header", "starter_kit");
                return;
            }

            int bytesPerPixel = cacheBytes[12];
            if (bytesPerPixel < 3)
            {
                ArchonLogger.LogWarning($"TerrainColorPatcher: Unexpected bytesPerPixel={bytesPerPixel}", "starter_kit");
                return;
            }

            // Swap matching RGB triplets in the raw pixel data
            int replaced = 0;
            for (int i = HEADER_SIZE; i + bytesPerPixel <= cacheBytes.Length; i += bytesPerPixel)
            {
                if (cacheBytes[i] == oldColor.r && cacheBytes[i + 1] == oldColor.g && cacheBytes[i + 2] == oldColor.b)
                {
                    cacheBytes[i] = newColor.r;
                    cacheBytes[i + 1] = newColor.g;
                    cacheBytes[i + 2] = newColor.b;
                    replaced++;
                }
            }

            if (replaced > 0)
            {
                File.WriteAllBytes(cachePath, cacheBytes);
                ArchonLogger.Log($"TerrainColorPatcher: Patched {replaced} pixels in .pixels cache", "starter_kit");
            }
            else
            {
                ArchonLogger.LogWarning($"TerrainColorPatcher: No pixels matched ({oldColor.r},{oldColor.g},{oldColor.b}) in cache", "starter_kit");
            }
        }

        /// <summary>
        /// Replace all pixels matching oldColor with newColor in terrain.png.
        /// WARNING: Uses Texture2D which allocates GPU memory — do NOT call during play mode.
        /// </summary>
        private static void PatchTerrainImage(Color32 oldColor, Color32 newColor, string mapDirectory)
        {
            string pngPath = Path.Combine(mapDirectory, "terrain.png");

            // If the file doesn't exist in the override directory, copy from base
            if (!File.Exists(pngPath) && Core.Modding.DataFileResolver.IsInitialized)
            {
                string basePath = Path.Combine(Core.Modding.DataFileResolver.BaseDirectory, "map", "terrain.png");
                if (File.Exists(basePath))
                {
                    Directory.CreateDirectory(mapDirectory);
                    File.Copy(basePath, pngPath);
                }
            }

            if (!File.Exists(pngPath))
            {
                ArchonLogger.LogWarning("TerrainColorPatcher: terrain.png not found, skipping image update", "starter_kit");
                return;
            }

            // Load the PNG
            byte[] pngBytes = File.ReadAllBytes(pngPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(pngBytes))
            {
                Object.DestroyImmediate(texture);
                ArchonLogger.LogError("TerrainColorPatcher: Failed to load terrain.png", "starter_kit");
                return;
            }

            // Get all pixels and replace matching colors
            var pixels = texture.GetPixels32();
            int replaced = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r == oldColor.r && pixels[i].g == oldColor.g && pixels[i].b == oldColor.b)
                {
                    pixels[i] = newColor;
                    replaced++;
                }
            }

            if (replaced > 0)
            {
                texture.SetPixels32(pixels);
                texture.Apply();

                // Save back to PNG
                byte[] newPngBytes = texture.EncodeToPNG();
                File.WriteAllBytes(pngPath, newPngBytes);

                ArchonLogger.Log($"TerrainColorPatcher: Replaced {replaced} pixels in terrain.png", "starter_kit");
            }

            Object.DestroyImmediate(texture);

            // Invalidate .pixels cache so it regenerates on next load
            string cachePath = pngPath + ".pixels";
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
                ArchonLogger.Log("TerrainColorPatcher: Deleted .pixels cache", "starter_kit");
            }
        }
    }
}
