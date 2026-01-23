using System;
using System.Collections.Generic;
using Core.Network;

namespace Archon.Network
{
    /// <summary>
    /// Detects desynchronization between host and clients via periodic checksum verification.
    /// When desync is detected, triggers automatic recovery via LateJoinHandler.
    /// </summary>
    public class DesyncDetector
    {
        private readonly NetworkManager networkManager;
        private readonly INetworkBridge networkBridge;

        // How often to verify checksums (in ticks)
        private readonly int checksumIntervalTicks;

        // Recent checksums for comparison (tick -> checksum)
        private readonly Dictionary<uint, uint> localChecksums = new();
        private readonly Dictionary<uint, uint> hostChecksums = new();  // Client only

        // Per-peer checksums (Host only: peerId -> (tick, checksum))
        private readonly Dictionary<int, (uint tick, uint checksum)> peerChecksums = new();

        // Keep checksums for this many ticks before pruning
        private const int ChecksumHistoryTicks = 300;

        /// <summary>
        /// Fired when desync is detected.
        /// Parameters: peerId (-1 for local/client), tick, localChecksum, remoteChecksum
        /// </summary>
        public event Action<int, uint, uint, uint> OnDesyncDetected;

        /// <summary>
        /// Fired when checksums match (for debugging/stats).
        /// </summary>
        public event Action<uint> OnChecksumVerified;

        public DesyncDetector(
            NetworkManager networkManager,
            INetworkBridge networkBridge,
            int checksumIntervalTicks = 60)  // Default: check every 60 ticks (~1 second at 60 ticks/sec)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            this.networkBridge = networkBridge ?? throw new ArgumentNullException(nameof(networkBridge));
            this.checksumIntervalTicks = checksumIntervalTicks;

            networkBridge.OnChecksumReceived += HandleChecksumReceived;
        }

        /// <summary>
        /// Called each tick to potentially send/verify checksums.
        /// </summary>
        /// <param name="currentTick">Current game tick</param>
        /// <param name="localChecksum">Checksum of local game state</param>
        public void OnTick(uint currentTick, uint localChecksum)
        {
            if (!networkManager.IsConnected) return;

            // Only check at intervals
            if (currentTick % checksumIntervalTicks != 0) return;

            // Store local checksum
            localChecksums[currentTick] = localChecksum;

            // Send checksum to network
            networkBridge.BroadcastChecksum(currentTick, localChecksum);

            // Prune old checksums
            PruneOldChecksums(currentTick);

            // If client, compare with any received host checksums
            if (!networkManager.IsHost)
            {
                CompareWithHostChecksum(currentTick, localChecksum);
            }
        }

        /// <summary>
        /// Handle checksum received from network.
        /// </summary>
        private void HandleChecksumReceived(int peerId, uint tick, uint checksum)
        {
            if (networkManager.IsHost)
            {
                // Host: store peer's checksum and compare with local
                peerChecksums[peerId] = (tick, checksum);

                if (localChecksums.TryGetValue(tick, out uint localChecksum))
                {
                    if (checksum != localChecksum)
                    {
                        ArchonLogger.LogWarning(
                            $"Desync detected! Peer {peerId} at tick {tick}. " +
                            $"Host: {localChecksum:X8}, Client: {checksum:X8}",
                            ArchonLogger.Systems.Network);

                        OnDesyncDetected?.Invoke(peerId, tick, localChecksum, checksum);
                    }
                    else
                    {
                        OnChecksumVerified?.Invoke(tick);
                    }
                }
            }
            else
            {
                // Client: store host's checksum for comparison
                hostChecksums[tick] = checksum;

                // Immediately compare if we have local checksum
                if (localChecksums.TryGetValue(tick, out uint localChecksum))
                {
                    if (checksum != localChecksum)
                    {
                        ArchonLogger.LogWarning(
                            $"Local desync detected at tick {tick}. " +
                            $"Host: {checksum:X8}, Local: {localChecksum:X8}",
                            ArchonLogger.Systems.Network);

                        OnDesyncDetected?.Invoke(-1, tick, localChecksum, checksum);
                    }
                    else
                    {
                        OnChecksumVerified?.Invoke(tick);
                    }
                }
            }
        }

        /// <summary>
        /// Client: Compare local checksum with host's checksum for a given tick.
        /// </summary>
        private void CompareWithHostChecksum(uint tick, uint localChecksum)
        {
            if (hostChecksums.TryGetValue(tick, out uint hostChecksum))
            {
                if (hostChecksum != localChecksum)
                {
                    ArchonLogger.LogWarning(
                        $"Local desync detected at tick {tick}. " +
                        $"Host: {hostChecksum:X8}, Local: {localChecksum:X8}",
                        ArchonLogger.Systems.Network);

                    OnDesyncDetected?.Invoke(-1, tick, localChecksum, hostChecksum);
                }
            }
        }

        /// <summary>
        /// Remove checksums older than history limit.
        /// </summary>
        private void PruneOldChecksums(uint currentTick)
        {
            uint cutoff = currentTick > ChecksumHistoryTicks ? currentTick - ChecksumHistoryTicks : 0;

            var toRemove = new List<uint>();
            foreach (var tick in localChecksums.Keys)
            {
                if (tick < cutoff) toRemove.Add(tick);
            }
            foreach (var tick in toRemove)
            {
                localChecksums.Remove(tick);
            }

            toRemove.Clear();
            foreach (var tick in hostChecksums.Keys)
            {
                if (tick < cutoff) toRemove.Add(tick);
            }
            foreach (var tick in toRemove)
            {
                hostChecksums.Remove(tick);
            }
        }

        /// <summary>
        /// Get the last known checksum for a peer (host only).
        /// </summary>
        public (uint tick, uint checksum)? GetPeerChecksum(int peerId)
        {
            if (peerChecksums.TryGetValue(peerId, out var result))
                return result;
            return null;
        }

        /// <summary>
        /// Get statistics about checksum verification.
        /// </summary>
        public (int localCount, int hostCount, int peerCount) GetStats()
        {
            return (localChecksums.Count, hostChecksums.Count, peerChecksums.Count);
        }

        /// <summary>
        /// Detach event handlers.
        /// </summary>
        public void Detach()
        {
            networkBridge.OnChecksumReceived -= HandleChecksumReceived;
        }
    }
}
