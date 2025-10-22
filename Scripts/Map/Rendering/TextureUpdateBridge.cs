using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Systems;
using Map.Province;

namespace Map.Rendering
{
    /// <summary>
    /// Simple bridge component that listens to province change events
    /// and updates textures via MapTexturePopulator
    /// Replaces the complex SimulationTextureUpdater with a lightweight event-driven approach
    /// </summary>
    public class TextureUpdateBridge : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logUpdates = false;
        [SerializeField] private float batchUpdateDelay = 0.1f; // Batch updates for performance

        // References
        private GameState gameState;
        private MapTextureManager textureManager;
        private MapTexturePopulator texturePopulator;
        private ProvinceMapping provinceMapping;

        // Batching for performance
        private HashSet<ushort> pendingProvinceUpdates = new HashSet<ushort>();
        private float lastBatchTime;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize the bridge with required components
        /// </summary>
        public void Initialize(GameState gameState, MapTextureManager textureManager,
                             MapTexturePopulator texturePopulator, ProvinceMapping provinceMapping)
        {
            this.gameState = gameState;
            this.textureManager = textureManager;
            this.texturePopulator = texturePopulator;
            this.provinceMapping = provinceMapping;

            if (gameState?.EventBus != null)
            {
                // Subscribe to province ownership changes
                gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnProvinceOwnershipChanged);

                isInitialized = true;

                if (logUpdates)
                {
                    ArchonLogger.LogMapRendering("TextureUpdateBridge: Initialized and subscribed to province events");
                }
            }
            else
            {
                ArchonLogger.LogMapRenderingError("TextureUpdateBridge: GameState or EventBus not available");
            }
        }

        void Update()
        {
            // Process batched updates
            if (isInitialized && pendingProvinceUpdates.Count > 0 &&
                Time.time - lastBatchTime > batchUpdateDelay)
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

            if (logUpdates)
            {
                ArchonLogger.LogMapRendering($"TextureUpdateBridge: Province {ownershipEvent.ProvinceId} ownership changed: {ownershipEvent.OldOwner} → {ownershipEvent.NewOwner}");
            }
        }

        /// <summary>
        /// Process all pending province updates in a batch
        /// </summary>
        private void ProcessPendingUpdates()
        {
            if (pendingProvinceUpdates.Count == 0) return;

            var startTime = Time.realtimeSinceStartup;

            // Convert to array for MapTexturePopulator
            var changedProvinces = new ushort[pendingProvinceUpdates.Count];
            pendingProvinceUpdates.CopyTo(changedProvinces);

            // Call the existing update method
            texturePopulator.UpdateSimulationData(textureManager, provinceMapping, gameState, changedProvinces);

            // Apply texture changes
            textureManager.ApplyTextureChanges();

            // Clear the batch
            pendingProvinceUpdates.Clear();

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;

            if (logUpdates)
            {
                ArchonLogger.LogMapRendering($"TextureUpdateBridge: Updated {changedProvinces.Length} provinces in {updateTime:F2}ms");
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