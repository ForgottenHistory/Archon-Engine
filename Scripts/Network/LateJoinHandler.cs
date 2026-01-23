using System;
using System.Collections.Generic;
using Core.Network;
using Core.Data;
using Core.Systems;

namespace Archon.Network
{
    /// <summary>
    /// Handles state synchronization for players joining mid-game.
    /// Host-side: serializes and sends full state to joining player.
    /// Client-side: receives and applies state, then resumes normal play.
    /// </summary>
    public class LateJoinHandler
    {
        private readonly NetworkManager networkManager;
        private readonly INetworkBridge networkBridge;

        // Pending sync requests (peerId -> request time)
        private readonly Dictionary<int, float> pendingSyncRequests = new();

        // Chunk size for large state transfers (32KB)
        private const int ChunkSize = 32 * 1024;

        /// <summary>
        /// Fired when a late joiner needs full state (host should provide simulation).
        /// </summary>
        public event Func<ProvinceSimulation> OnSimulationRequested;

        /// <summary>
        /// Fired when full state has been received and applied (client-side).
        /// </summary>
        public event Action<uint> OnStateSyncComplete;  // tick

        /// <summary>
        /// Fired when state sync fails.
        /// </summary>
        public event Action<int, string> OnStateSyncFailed;  // peerId, error

        public LateJoinHandler(NetworkManager networkManager, INetworkBridge networkBridge)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            this.networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));

            // Subscribe to relevant events
            networkBridge.OnStateSyncRequested += HandleStateSyncRequested;
            networkBridge.OnStateReceived += HandleStateReceived;
        }

        /// <summary>
        /// Host: Handle a new client that needs state sync.
        /// Called when a client connects and completes handshake.
        /// </summary>
        public void HandleNewClient(int peerId)
        {
            if (!networkManager.IsHost)
            {
                ArchonLogger.LogWarning("Only host can handle new clients", ArchonLogger.Systems.Network);
                return;
            }

            ArchonLogger.Log($"Preparing state sync for peer {peerId}", ArchonLogger.Systems.Network);
            pendingSyncRequests[peerId] = UnityEngine.Time.realtimeSinceStartup;

            // Request simulation from game layer
            var simulation = OnSimulationRequested?.Invoke();
            if (simulation == null)
            {
                ArchonLogger.LogError($"Failed to get simulation for state sync to peer {peerId}", ArchonLogger.Systems.Network);
                OnStateSyncFailed?.Invoke(peerId, "Simulation not available");
                return;
            }

            SendFullState(peerId, simulation);
        }

        /// <summary>
        /// Host: Serialize and send full state to a peer.
        /// </summary>
        private void SendFullState(int peerId, ProvinceSimulation simulation)
        {
            try
            {
                // Serialize full state
                byte[] stateData = ProvinceStateSerializer.SerializeFullState(simulation);
                uint currentTick = 0;  // TODO: Get from CommandProcessor or TimeManager

                ArchonLogger.Log($"Sending state sync to peer {peerId}: {stateData.Length} bytes", ArchonLogger.Systems.Network);

                // For now, send as single message
                // TODO: Implement chunked transfer for large states
                networkBridge.SendStateToPeer(peerId, stateData, currentTick);

                pendingSyncRequests.Remove(peerId);
                ArchonLogger.Log($"State sync sent to peer {peerId}", ArchonLogger.Systems.Network);
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"Failed to send state sync to peer {peerId}: {ex.Message}", ArchonLogger.Systems.Network);
                OnStateSyncFailed?.Invoke(peerId, ex.Message);
            }
        }

        /// <summary>
        /// Host: Handle state sync request from INetworkBridge.
        /// </summary>
        private void HandleStateSyncRequested(int peerId)
        {
            HandleNewClient(peerId);
        }

        /// <summary>
        /// Client: Handle received state from host.
        /// </summary>
        private void HandleStateReceived(byte[] stateData, uint tick)
        {
            if (networkManager.IsHost)
            {
                ArchonLogger.LogWarning("Host received state sync - ignoring", ArchonLogger.Systems.Network);
                return;
            }

            ArchonLogger.Log($"Received state sync: {stateData.Length} bytes at tick {tick}", ArchonLogger.Systems.Network);

            // Validate the data
            if (!ProvinceStateSerializer.ValidateSerializedData(stateData, out string error))
            {
                ArchonLogger.LogError($"State sync validation failed: {error}", ArchonLogger.Systems.Network);
                OnStateSyncFailed?.Invoke(-1, error);
                return;
            }

            // Get stats for logging
            var stats = ProvinceStateSerializer.GetSerializationStats(stateData);
            ArchonLogger.Log($"State sync stats: {stats}", ArchonLogger.Systems.Network);

            // The actual deserialization and application will be handled by the game layer
            // We just notify that state is ready
            OnStateSyncComplete?.Invoke(tick);
        }

        /// <summary>
        /// Check for timed-out sync requests.
        /// </summary>
        public void Update()
        {
            if (!networkManager.IsHost) return;

            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            const float SyncTimeout = 30f;  // 30 second timeout

            var timedOut = new List<int>();
            foreach (var kvp in pendingSyncRequests)
            {
                if (currentTime - kvp.Value > SyncTimeout)
                {
                    timedOut.Add(kvp.Key);
                }
            }

            foreach (int peerId in timedOut)
            {
                ArchonLogger.LogWarning($"State sync timed out for peer {peerId}", ArchonLogger.Systems.Network);
                pendingSyncRequests.Remove(peerId);
                OnStateSyncFailed?.Invoke(peerId, "Sync timeout");
            }
        }

        /// <summary>
        /// Detach event handlers.
        /// </summary>
        public void Detach()
        {
            networkBridge.OnStateSyncRequested -= HandleStateSyncRequested;
            networkBridge.OnStateReceived -= HandleStateReceived;
        }
    }
}
