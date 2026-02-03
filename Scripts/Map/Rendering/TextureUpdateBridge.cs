using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Core;
using Core.Systems;
using Map.Province;

namespace Map.Rendering
{
    /// <summary>
    /// Simple bridge component that listens to province change events
    /// and updates textures via MapTexturePopulator.
    /// Replaces the complex SimulationTextureUpdater with a lightweight event-driven approach.
    ///
    /// Configuration comes from GameSettings - no Inspector assignments needed.
    /// </summary>
    public class TextureUpdateBridge : MonoBehaviour
    {
        // Logging and timing from GameSettings
        private bool LogUpdates => GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false;
        private float BatchUpdateDelay => GameSettings.Instance?.TextureUpdateBatchDelay ?? 0.1f;

        // References
        private GameState gameState;
        private MapTextureManager textureManager;
        private MapTexturePopulator texturePopulator;
        private ProvinceMapping provinceMapping;
        private OwnerTextureDispatcher ownerTextureDispatcher;
        private BorderComputeDispatcher borderDispatcher;

        // Batching for performance
        private HashSet<ushort> pendingProvinceUpdates = new HashSet<ushort>();
        private float lastBatchTime;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize the bridge with required components.
        /// </summary>
        public void Initialize(
            GameState gameState,
            MapTextureManager textureManager,
            MapTexturePopulator texturePopulator,
            ProvinceMapping provinceMapping,
            OwnerTextureDispatcher ownerDispatcher = null,
            BorderComputeDispatcher borderDispatcher = null)
        {
            this.gameState = gameState;
            this.textureManager = textureManager;
            this.texturePopulator = texturePopulator;
            this.provinceMapping = provinceMapping;
            this.ownerTextureDispatcher = ownerDispatcher;
            this.borderDispatcher = borderDispatcher;

            if (gameState?.EventBus != null)
            {
                // Subscribe to province ownership changes
                gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnProvinceOwnershipChanged);

                isInitialized = true;

                if (LogUpdates)
                {
                    ArchonLogger.Log("TextureUpdateBridge: Initialized and subscribed to province events", "map_rendering");
                }
            }
            else
            {
                ArchonLogger.LogError("TextureUpdateBridge: GameState or EventBus not available", "map_rendering");
            }
        }

        void Update()
        {
            // Process batched updates
            if (isInitialized && pendingProvinceUpdates.Count > 0 &&
                Time.time - lastBatchTime > BatchUpdateDelay)
            {
                ProcessPendingUpdates();
            }
        }

        /// <summary>
        /// Handle province ownership change events
        /// </summary>
        private void OnProvinceOwnershipChanged(ProvinceOwnershipChangedEvent ownershipEvent)
        {
            if (!isInitialized) return;

            // Add to batch for performance
            pendingProvinceUpdates.Add(ownershipEvent.ProvinceId);
            lastBatchTime = Time.time;

            if (LogUpdates)
            {
                ArchonLogger.Log($"TextureUpdateBridge: Province {ownershipEvent.ProvinceId} ownership changed: {ownershipEvent.OldOwner} → {ownershipEvent.NewOwner}", "map_rendering");
            }
        }

        /// <summary>
        /// Process all pending province updates in a batch.
        /// Uses CommandBuffer + ExecuteCommandBufferAsync to avoid blocking the main thread
        /// on GPU compute shader dispatches (eliminates Semaphore.WaitForSignal stalls).
        /// </summary>
        private void ProcessPendingUpdates()
        {
            if (pendingProvinceUpdates.Count == 0) return;

            var startTime = Time.realtimeSinceStartup;

            // Convert to array for MapTexturePopulator
            var changedProvinces = new ushort[pendingProvinceUpdates.Count];
            pendingProvinceUpdates.CopyTo(changedProvinces);

            // Note: UpdateSimulationData is a no-op (owner texture handled by compute shader below).
            // ApplyTextureChanges is NOT called here — no CPU-side textures change during runtime
            // province updates. The owner texture is a RenderTexture updated by GPU compute shader.
            // Calling Texture2D.Apply() on unchanged textures forces a GPU pipeline sync (Semaphore stall).

            // Build a command buffer for all GPU compute work — non-blocking
            var cmd = new CommandBuffer { name = "ProvinceTextureUpdate" };
            cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

            // Update owner texture incrementally (only changed provinces)
            if (ownerTextureDispatcher != null && gameState?.ProvinceQueries != null)
            {
                ownerTextureDispatcher.UpdateOwnerTexture(gameState.ProvinceQueries, changedProvinces, cmd);
            }

            // Update borders incrementally (only pixels of changed provinces + neighbors)
            if (borderDispatcher != null)
            {
                borderDispatcher.DetectBordersIndexed(changedProvinces, cmd);
            }

            // Submit to GPU async compute queue — main thread does NOT wait for completion
            Graphics.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Default);
            cmd.Release();

            // Clear the batch
            pendingProvinceUpdates.Clear();

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;

            if (LogUpdates)
            {
                ArchonLogger.Log($"TextureUpdateBridge: Updated {changedProvinces.Length} provinces in {updateTime:F2}ms", "map_rendering");
            }
        }

        /// <summary>
        /// Force immediate update of all pending changes
        /// </summary>
        public void ForceUpdate()
        {
            if (pendingProvinceUpdates.Count > 0)
            {
                ProcessPendingUpdates();
            }
        }

        void OnDestroy()
        {
            // Unsubscribe from events
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Unsubscribe<ProvinceOwnershipChangedEvent>(OnProvinceOwnershipChanged);
            }
        }
    }
}