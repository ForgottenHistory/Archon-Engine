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

            // Defer terrain.png patch — Texture2D operations during play mode allocate GPU
            // memory that corrupts active render textures. Flushed after play mode exits.
            deferredImagePatches.Add((oldColor, newColor, dataDirectory));

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
        /// Replace all pixels matching oldColor with newColor in terrain.png.
        /// Also invalidates the .pixels cache.
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
