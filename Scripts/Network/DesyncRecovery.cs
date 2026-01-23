using System;

namespace Archon.Network
{
    /// <summary>
    /// Handles automatic recovery from desynchronization.
    /// Coordinates with LateJoinHandler to resync desynced clients.
    ///
    /// Key differentiator from Paradox games:
    /// - Automatic detection and recovery (1-3 seconds)
    /// - No manual rehost required
    /// - Seamless player experience
    /// </summary>
    public class DesyncRecovery
    {
        private readonly NetworkManager networkManager;
        private readonly DesyncDetector desyncDetector;
        private readonly LateJoinHandler lateJoinHandler;

        private bool isResyncing;
        private uint resyncStartTick;

        /// <summary>
        /// Fired when desync is detected and recovery begins.
        /// UI should show "Resyncing..." notification.
        /// </summary>
        public event Action OnDesyncDetected;

        /// <summary>
        /// Fired when resync completes successfully.
        /// UI should hide notification.
        /// </summary>
        public event Action OnResyncComplete;

        /// <summary>
        /// Fired when resync fails (rare, may require manual intervention).
        /// </summary>
        public event Action<string> OnResyncFailed;

        /// <summary>
        /// Whether we are currently resyncing.
        /// </summary>
        public bool IsResyncing => isResyncing;

        public DesyncRecovery(
            NetworkManager networkManager,
            DesyncDetector desyncDetector,
            LateJoinHandler lateJoinHandler)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            this.desyncDetector = desyncDetector ?? throw new ArgumentNullException(nameof(desyncDetector));
            this.lateJoinHandler = lateJoinHandler ?? throw new ArgumentNullException(nameof(lateJoinHandler));

            // Subscribe to desync events
            desyncDetector.OnDesyncDetected += HandleDesyncDetected;
            lateJoinHandler.OnStateSyncComplete += HandleStateSyncComplete;
            lateJoinHandler.OnStateSyncFailed += HandleStateSyncFailed;
        }

        /// <summary>
        /// Handle desync detection.
        /// </summary>
        private void HandleDesyncDetected(int peerId, uint tick, uint localChecksum, uint remoteChecksum)
        {
            if (networkManager.IsHost)
            {
                // Host: trigger resync for the desynced client
                RecoverClient(peerId, tick);
            }
            else
            {
                // Client: we are desynced, request resync from host
                RequestResync(tick);
            }
        }

        /// <summary>
        /// Host: Resync a desynced client.
        /// </summary>
        private void RecoverClient(int peerId, uint desyncTick)
        {
            ArchonLogger.Log($"Initiating resync for peer {peerId} (desync at tick {desyncTick})", ArchonLogger.Systems.Network);

            // Mark peer as resyncing
            if (networkManager.Peers.TryGetValue(peerId, out var peer))
            {
                peer.State = PeerState.Resyncing;
            }

            // Use late join handler to send full state
            lateJoinHandler.HandleNewClient(peerId);

            // Log for debugging patterns
            LogDesyncEvent(peerId, desyncTick);
        }

        /// <summary>
        /// Client: Request resync from host.
        /// </summary>
        private void RequestResync(uint desyncTick)
        {
            if (isResyncing)
            {
                ArchonLogger.Log("Already resyncing, ignoring additional desync", ArchonLogger.Systems.Network);
                return;
            }

            ArchonLogger.Log($"Requesting resync (desync at tick {desyncTick})", ArchonLogger.Systems.Network);

            isResyncing = true;
            resyncStartTick = desyncTick;

            // Notify UI
            OnDesyncDetected?.Invoke();

            // The host will detect the desync via checksum comparison and trigger resync
            // We just wait for state sync to arrive
        }

        /// <summary>
        /// Handle successful state sync (resync complete).
        /// </summary>
        private void HandleStateSyncComplete(uint tick)
        {
            if (!isResyncing && networkManager.IsHost) return;

            ArchonLogger.Log($"Resync complete at tick {tick}", ArchonLogger.Systems.Network);

            isResyncing = false;
            OnResyncComplete?.Invoke();
        }

        /// <summary>
        /// Handle failed state sync.
        /// </summary>
        private void HandleStateSyncFailed(int peerId, string error)
        {
            ArchonLogger.LogError($"Resync failed for peer {peerId}: {error}", ArchonLogger.Systems.Network);

            if (!networkManager.IsHost && isResyncing)
            {
                isResyncing = false;
                OnResyncFailed?.Invoke(error);
            }
        }

        /// <summary>
        /// Log desync event for debugging patterns.
        /// </summary>
        private void LogDesyncEvent(int peerId, uint tick)
        {
            // In debug builds, this could dump full state for comparison
            // For now, just log the event
            ArchonLogger.Log(
                $"[DESYNC] Peer={peerId}, Tick={tick}, Time={UnityEngine.Time.realtimeSinceStartup:F2}s",
                ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Get recovery statistics.
        /// </summary>
        public (bool isResyncing, uint resyncStartTick) GetStatus()
        {
            return (isResyncing, resyncStartTick);
        }

        /// <summary>
        /// Detach event handlers.
        /// </summary>
        public void Detach()
        {
            desyncDetector.OnDesyncDetected -= HandleDesyncDetected;
            lateJoinHandler.OnStateSyncComplete -= HandleStateSyncComplete;
            lateJoinHandler.OnStateSyncFailed -= HandleStateSyncFailed;
        }
    }
}
