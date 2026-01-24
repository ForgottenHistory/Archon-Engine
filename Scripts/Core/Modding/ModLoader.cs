using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace Core.Modding
{
    /// <summary>
    /// ENGINE LAYER: Loads mod AssetBundles from StreamingAssets/Mods/
    ///
    /// Mod Structure:
    ///   StreamingAssets/Mods/
    ///     MyMod/
    ///       shaders.bundle      - Compute shaders
    ///       textures.bundle     - Textures (optional)
    ///       mod.json            - Mod metadata (optional)
    ///
    /// Usage:
    ///   ModLoader.Initialize();
    ///   var shader = ModLoader.LoadAsset<ComputeShader>("BorderDetection");
    ///   // Returns modded version if exists, null otherwise
    /// </summary>
    public static class ModLoader
    {
        private static readonly Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        private static readonly Dictionary<string, Object> assetCache = new Dictionary<string, Object>();
        private static bool isInitialized;
        private static string modsPath;

        /// <summary>
        /// Initialize the mod loader. Call once at game startup.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            modsPath = Path.Combine(Application.streamingAssetsPath, "Mods");

            if (!Directory.Exists(modsPath))
            {
                Directory.CreateDirectory(modsPath);
                ArchonLogger.Log("ModLoader: Created Mods folder", "core_modding");
            }

            LoadAllMods();
            isInitialized = true;
        }

        /// <summary>
        /// Load all mod bundles from the Mods folder
        /// </summary>
        private static void LoadAllMods()
        {
            if (!Directory.Exists(modsPath))
            {
                ArchonLogger.Log("ModLoader: No Mods folder found", "core_modding");
                return;
            }

            string[] modFolders = Directory.GetDirectories(modsPath);
            ArchonLogger.Log($"ModLoader: Found {modFolders.Length} mod folders", "core_modding");

            foreach (string modFolder in modFolders)
            {
                string modName = Path.GetFileName(modFolder);
                LoadModBundles(modName, modFolder);
            }
        }

        /// <summary>
        /// Load all bundles from a single mod folder
        /// </summary>
        private static void LoadModBundles(string modName, string modFolder)
        {
            string[] bundleFiles = Directory.GetFiles(modFolder, "*.bundle");

            if (bundleFiles.Length == 0)
            {
                ArchonLogger.Log($"ModLoader: Mod '{modName}' has no bundles", "core_modding");
                return;
            }

            foreach (string bundlePath in bundleFiles)
            {
                string bundleName = Path.GetFileNameWithoutExtension(bundlePath);
                string bundleKey = $"{modName}/{bundleName}";

                try
                {
                    AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (bundle != null)
                    {
                        loadedBundles[bundleKey] = bundle;
                        ArchonLogger.Log($"ModLoader: Loaded bundle '{bundleKey}'", "core_modding");

                        // Log contained assets
                        string[] assetNames = bundle.GetAllAssetNames();
                        foreach (string assetName in assetNames)
                        {
                            ArchonLogger.Log($"  - {assetName}", "core_modding");
                        }
                    }
                    else
                    {
                        ArchonLogger.LogWarning($"ModLoader: Failed to load bundle '{bundlePath}'", "core_modding");
                    }
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogError($"ModLoader: Error loading bundle '{bundlePath}': {e.Message}", "core_modding");
                }
            }
        }

        /// <summary>
        /// Try to load an asset from any loaded mod bundle.
        /// Returns null if not found in any mod.
        /// </summary>
        public static T LoadAsset<T>(string assetName) where T : Object
        {
            if (!isInitialized)
            {
                Initialize();
            }

            // Check cache first
            string cacheKey = $"{typeof(T).Name}/{assetName}";
            if (assetCache.TryGetValue(cacheKey, out Object cached))
            {
                return cached as T;
            }

            // Search all loaded bundles
            foreach (var kvp in loadedBundles)
            {
                AssetBundle bundle = kvp.Value;

                // Try exact name
                T asset = bundle.LoadAsset<T>(assetName);
                if (asset != null)
                {
                    assetCache[cacheKey] = asset;
                    ArchonLogger.Log($"ModLoader: Loaded '{assetName}' from '{kvp.Key}'", "core_modding");
                    return asset;
                }

                // Try with lowercase (Unity bundles often lowercase names)
                asset = bundle.LoadAsset<T>(assetName.ToLowerInvariant());
                if (asset != null)
                {
                    assetCache[cacheKey] = asset;
                    ArchonLogger.Log($"ModLoader: Loaded '{assetName}' from '{kvp.Key}'", "core_modding");
                    return asset;
                }
            }

            return null;
        }

        /// <summary>
        /// Load asset with fallback to Resources folder.
        /// Use this for assets that have default versions.
        /// </summary>
        public static T LoadAssetWithFallback<T>(string assetName, string resourcesPath) where T : Object
        {
            // Try mod first
            T modded = LoadAsset<T>(assetName);
            if (modded != null)
            {
                return modded;
            }

            // Fall back to Resources (fully qualified to avoid Core.Resources conflict)
            return UnityEngine.Resources.Load<T>(resourcesPath);
        }

        /// <summary>
        /// Check if any mod provides an asset
        /// </summary>
        public static bool HasModdedAsset<T>(string assetName) where T : Object
        {
            return LoadAsset<T>(assetName) != null;
        }

        /// <summary>
        /// Get list of loaded mod names
        /// </summary>
        public static string[] GetLoadedMods()
        {
            HashSet<string> modNames = new HashSet<string>();
            foreach (string bundleKey in loadedBundles.Keys)
            {
                string modName = bundleKey.Split('/')[0];
                modNames.Add(modName);
            }
            return new List<string>(modNames).ToArray();
        }

        /// <summary>
        /// Unload all mod bundles. Call on game exit or when reloading mods.
        /// </summary>
        public static void UnloadAll()
        {
            foreach (var bundle in loadedBundles.Values)
            {
                bundle.Unload(true);
            }
            loadedBundles.Clear();
            assetCache.Clear();
            isInitialized = false;
            ArchonLogger.Log("ModLoader: Unloaded all mods", "core_modding");
        }
    }
}
