using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Map.Loading.Data;
using Core.Modding;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Generates terrain type texture (R8) from terrain color texture (RGBA32)
    /// Maps terrain colors back to terrain type indices using GPU compute shader
    /// Purpose: Enables terrain splatting for detail texture selection
    ///
    /// Architecture:
    /// - Input: ProvinceTerrainTexture (RGBA32) with terrain colors
    /// - Output: TerrainTypeTexture (R8) with terrain type indices (0-255)
    /// - Method: GPU compute shader with color lookup buffer
    /// </summary>
    public static class TerrainTypeTextureGenerator
    {
        private const int THREAD_GROUP_SIZE = 8;

        /// <summary>
        /// Generate terrain type texture from terrain color texture using GPU compute shader
        /// </summary>
        /// <param name="terrainColorTexture">Source terrain texture (RGBA32)</param>
        /// <param name="logProgress">Enable progress logging</param>
        /// <returns>Terrain type texture (R8 format, 0-255 terrain indices)</returns>
        public static Texture2D GenerateTerrainTypeTexture(Texture2D terrainColorTexture, bool logProgress = true)
        {
            if (terrainColorTexture == null)
            {
                ArchonLogger.LogError("TerrainTypeTextureGenerator: Cannot generate - terrain color texture is null", "map_initialization");
                return null;
            }

            int width = terrainColorTexture.width;
            int height = terrainColorTexture.height;

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainTypeTextureGenerator: Starting GPU generation {width}x{height}", "map_initialization");
            }

            float startTime = Time.realtimeSinceStartup;

            // Load compute shader
            ComputeShader computeShader = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "TerrainTypeGenerator",
                "Shaders/TerrainTypeGenerator"
            );

            if (computeShader == null)
            {
                ArchonLogger.LogError("TerrainTypeTextureGenerator: Compute shader not found, falling back to CPU", "map_initialization");
                return GenerateTerrainTypeTextureCPU(terrainColorTexture, logProgress);
            }

            int kernel = computeShader.FindKernel("GenerateTerrainTypes");

            // Build lookup buffers from TerrainColorMapper
            BuildLookupBuffers(out uint[] colorKeys, out uint[] terrainIndices, out int terrainTypeCount);

            if (terrainTypeCount == 0)
            {
                ArchonLogger.LogWarning("TerrainTypeTextureGenerator: No terrain types registered, falling back to CPU", "map_initialization");
                return GenerateTerrainTypeTextureCPU(terrainColorTexture, logProgress);
            }

            // Create temporary RenderTexture for compute shader output (R8_UNorm)
            RenderTexture outputRT = new RenderTexture(width, height, 0, GraphicsFormat.R8_UNorm);
            outputRT.enableRandomWrite = true;
            outputRT.filterMode = FilterMode.Point;
            outputRT.wrapMode = TextureWrapMode.Clamp;
            outputRT.Create();

            // Create compute buffers
            ComputeBuffer colorKeyBuffer = new ComputeBuffer(terrainTypeCount, sizeof(uint));
            ComputeBuffer terrainIndexBuffer = new ComputeBuffer(terrainTypeCount, sizeof(uint));

            try
            {
                // Upload lookup data to GPU
                colorKeyBuffer.SetData(colorKeys);
                terrainIndexBuffer.SetData(terrainIndices);

                // Bind inputs
                computeShader.SetTexture(kernel, "TerrainColorTexture", terrainColorTexture);
                computeShader.SetTexture(kernel, "OutputTexture", outputRT);
                computeShader.SetBuffer(kernel, "ColorKeyBuffer", colorKeyBuffer);
                computeShader.SetBuffer(kernel, "TerrainIndexBuffer", terrainIndexBuffer);

                // Set parameters
                computeShader.SetInt("MapWidth", width);
                computeShader.SetInt("MapHeight", height);
                computeShader.SetInt("TerrainTypeCount", terrainTypeCount);
                computeShader.SetFloat("NoTerrainMarker", 255.0f / 255.0f); // 1.0 in R8_UNorm = 255

                // Dispatch
                int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                // Copy RenderTexture result to Texture2D (consumers expect Texture2D)
                Texture2D terrainTypeTexture = new Texture2D(
                    width,
                    height,
                    GraphicsFormat.R8_UNorm,
                    TextureCreationFlags.None
                );
                terrainTypeTexture.name = "TerrainType_Texture";
                terrainTypeTexture.filterMode = FilterMode.Point;
                terrainTypeTexture.wrapMode = TextureWrapMode.Clamp;
                terrainTypeTexture.anisoLevel = 0;

                // GPU copy: RenderTexture → Texture2D
                RenderTexture previousRT = RenderTexture.active;
                RenderTexture.active = outputRT;
                terrainTypeTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                terrainTypeTexture.Apply(false);
                RenderTexture.active = previousRT;

                float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainTypeTextureGenerator: GPU generation complete in {elapsed:F2}ms ({terrainTypeCount} terrain types)", "map_initialization");
                }

                return terrainTypeTexture;
            }
            finally
            {
                // Release GPU resources
                colorKeyBuffer.Release();
                terrainIndexBuffer.Release();
                outputRT.Release();
            }
        }

        /// <summary>
        /// Build parallel arrays for GPU lookup: colorKeys[i] and terrainIndices[i]
        /// </summary>
        private static void BuildLookupBuffers(out uint[] colorKeys, out uint[] terrainIndices, out int count)
        {
            var registeredIndices = TerrainColorMapper.GetRegisteredIndices();
            var keyList = new System.Collections.Generic.List<uint>();
            var indexList = new System.Collections.Generic.List<uint>();

            foreach (byte index in registeredIndices)
            {
                Color32 color = TerrainColorMapper.GetTerrainColor(index);
                uint key = ((uint)color.r << 16) | ((uint)color.g << 8) | color.b;

                keyList.Add(key);
                indexList.Add(index);
            }

            colorKeys = keyList.ToArray();
            terrainIndices = indexList.ToArray();
            count = keyList.Count;
        }

        /// <summary>
        /// CPU fallback for when compute shader is unavailable
        /// </summary>
        private static Texture2D GenerateTerrainTypeTextureCPU(Texture2D terrainColorTexture, bool logProgress)
        {
            int width = terrainColorTexture.width;
            int height = terrainColorTexture.height;

            if (logProgress)
            {
                ArchonLogger.LogWarning($"TerrainTypeTextureGenerator: Using CPU fallback for {width}x{height}", "map_initialization");
            }

            var terrainTypeTexture = new Texture2D(
                width,
                height,
                GraphicsFormat.R8_UNorm,
                TextureCreationFlags.None
            );
            terrainTypeTexture.name = "TerrainType_Texture";
            terrainTypeTexture.filterMode = FilterMode.Point;
            terrainTypeTexture.wrapMode = TextureWrapMode.Clamp;
            terrainTypeTexture.anisoLevel = 0;

            Color32[] terrainColors = terrainColorTexture.GetPixels32();
            byte[] terrainTypePixels = new byte[width * height];

            var colorToIndexMap = new System.Collections.Generic.Dictionary<int, byte>();
            foreach (byte index in TerrainColorMapper.GetRegisteredIndices())
            {
                Color32 color = TerrainColorMapper.GetTerrainColor(index);
                int key = (color.r << 16) | (color.g << 8) | color.b;
                if (!colorToIndexMap.ContainsKey(key))
                {
                    colorToIndexMap[key] = index;
                }
            }

            byte noTerrainMarker = 255;
            for (int i = 0; i < terrainColors.Length; i++)
            {
                Color32 color = terrainColors[i];
                int key = (color.r << 16) | (color.g << 8) | color.b;

                terrainTypePixels[i] = colorToIndexMap.TryGetValue(key, out byte terrainType)
                    ? terrainType
                    : noTerrainMarker;
            }

            terrainTypeTexture.SetPixelData(terrainTypePixels, 0);
            terrainTypeTexture.Apply(false);

            return terrainTypeTexture;
        }
    }
}
