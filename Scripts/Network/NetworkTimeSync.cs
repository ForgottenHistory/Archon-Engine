using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Systems;

namespace Archon.Network
{
    /// <summary>
    /// Synchronizes game time across multiplayer clients.
    ///
    /// Host behavior:
    /// - Runs TimeManager normally
    /// - Broadcasts tick sync to clients periodically
    /// - Monitors client ack messages for lag detection
    /// - Auto-adjusts speed if clients can't keep up (Paradox-style)
    ///
    /// Client behavior:
    /// - TimeManager is paused locally
    /// - Receives tick sync from host
    /// - Uses SynchronizeToTick to catch up
    /// - Sends ack messages back to host
    /// </summary>
    public class NetworkTimeSync : IDisposable
    {
        // Configuration
        private const int TICK_SYNC_INTERVAL = 4;           // Send sync every N ticks
        private const int ACK_INTERVAL_TICKS = 8;           // Send ack every N ticks
        private const int SPEED_ADJUST_CHECK_INTERVAL = 24; // Check speed adjustment every N ticks
        private const ushort MAX_TICKS_BEHIND_THRESHOLD = 8;// If client is this far behind, slow down
        private const float SPEED_REDUCTION_COOLDOWN = 2f;  // Seconds before allowing speed increase again

        private readonly NetworkManager networkManager;
        private readonly TimeManager timeManager;
        private readonly bool logProgress;

        // Host state
        private readonly Dictionary<int, ClientSyncState> clientStates = new();
        private int requestedSpeed;           // Speed player requested
        private int effectiveSpeed;           // Actual speed (may be lower due to slow clients)
        private float lastSpeedReductionTime;
        private ulong lastTickSyncSent;

        // Client state
        private ulong lastReceivedHostTick;
        private ulong lastAckSentTick;
        private bool isClientSyncing;

        private bool disposed;

        /// <summary>
        /// Current effective speed (may be lower than requested if clients are lagging).
        /// </summary>
        public int EffectiveSpeed => effectiveSpeed;

        /// <summary>
        /// Speed the player/host requested.
        /// </summary>
        public int RequestedSpeed => requestedSpeed;

        /// <summary>
        /// True if speed was auto-reduced due to slow clients.
        /// </summary>
        public bool IsSpeedReduced => effectiveSpeed < requestedSpeed;

        public NetworkTimeSync(NetworkManager networkManager, TimeManager timeManager, bool log = true)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            this.timeManager = timeManager ?? throw new ArgumentNullException(nameof(timeManager));
            this.logProgress = log;

            requestedSpeed = timeManager.GameSpeed;
            effectiveSpeed = requestedSpeed;

            // Subscribe to network events
            networkManager.OnTickSyncReceived += HandleTickSync;
            networkManager.OnTickAckReceived += HandleTickAck;
            networkManager.OnPeerConnected += HandlePeerConnected;
            networkManager.OnPeerDisconnected += HandlePeerDisconnected;

            // Subscribe to time events (for host to broadcast)
            timeManager.OnHourlyTick += HandleHostTick;

            if (logProgress)
            {
                ArchonLogger.Log("NetworkTimeSync: Initialized", ArchonLogger.Systems.Network);
            }
        }

        /// <summary>
        /// Call this when entering multiplayer mode.
        /// Configures TimeManager based on host/client role.
        /// </summary>
        public void StartMultiplayerSync()
        {
            if (networkManager.IsHost)
            {
                // Host runs time normally
                if (logProgress)
                    ArchonLogger.Log("NetworkTimeSync: Host mode - running time locally", ArchonLogger.Systems.Network);
            }
            else
            {
                // Client pauses local time, waits for sync from host
                timeManager.PauseTime();
                isClientSyncing = true;

                if (logProgress)
                    ArchonLogger.Log("NetworkTimeSync: Client mode - waiting for host sync", ArchonLogger.Systems.Network);
            }
        }

        /// <summary>
        /// Set game speed. Host broadcasts to clients.
        /// </summary>
        public void SetSpeed(int speed)
        {
            if (!networkManager.IsHost)
            {
                ArchonLogger.LogWarning("NetworkTimeSync: Only host can set speed", ArchonLogger.Systems.Network);
                return;
            }

            requestedSpeed = speed;

            // Check if we need to reduce due to slow clients
            effectiveSpeed = CalculateEffectiveSpeed();

            // Apply to TimeManager
            timeManager.SetSpeed(effectiveSpeed);

            // Broadcast to clients
            networkManager.SendGameSpeedChange((byte)effectiveSpeed, (uint)timeManager.CurrentTick);

            if (logProgress)
            {
                if (effectiveSpeed < requestedSpeed)
                    ArchonLogger.Log($"NetworkTimeSync: Speed set to {effectiveSpeed}x (requested {requestedSpeed}x, reduced for slow clients)", ArchonLogger.Systems.Network);
                else
                    ArchonLogger.Log($"NetworkTimeSync: Speed set to {effectiveSpeed}x", ArchonLogger.Systems.Network);
            }
        }

        /// <summary>
        /// Toggle pause state. Host only.
        /// </summary>
        public void TogglePause()
        {
            if (!networkManager.IsHost)
            {
                ArchonLogger.LogWarning("NetworkTimeSync: Only host can toggle pause", ArchonLogger.Systems.Network);
                return;
            }

            timeManager.TogglePause();

            // Broadcast current state
            networkManager.SendGameSpeedChange(
                (byte)(timeManager.IsPaused ? 0 : effectiveSpeed),
                (uint)timeManager.CurrentTick
            );
        }

        #region Host Logic

        private void HandleHostTick(int hour)
        {
            if (!networkManager.IsHost) return;
            if (!networkManager.IsConnected) return;

            ulong currentTick = timeManager.CurrentTick;

            // Broadcast tick sync periodically
            if (currentTick - lastTickSyncSent >= TICK_SYNC_INTERVAL)
            {
                networkManager.SendTickSync(
                    currentTick,
                    (byte)effectiveSpeed,
                    timeManager.IsPaused
                );
                lastTickSyncSent = currentTick;
            }

            // Check if we should adjust speed
            if (currentTick % SPEED_ADJUST_CHECK_INTERVAL == 0)
            {
                CheckAndAdjustSpeed();
            }
        }

        private void HandleTickAck(int peerId, TickAckMessage ack)
        {
            if (!clientStates.TryGetValue(peerId, out var state))
            {
                state = new ClientSyncState();
                clientStates[peerId] = state;
            }

            state.LastAckedTick = ack.AcknowledgedTick;
            state.TicksBehind = ack.TicksBehind;
            state.LastAckTime = Time.unscaledTime;
        }

        private void HandlePeerConnected(NetworkPeer peer)
        {
            if (!networkManager.IsHost) return;

            clientStates[peer.PeerId] = new ClientSyncState
            {
                LastAckedTick = timeManager.CurrentTick,
                TicksBehind = 0,
                LastAckTime = Time.unscaledTime
            };

            if (logProgress)
                ArchonLogger.Log($"NetworkTimeSync: Tracking new peer {peer.PeerId}", ArchonLogger.Systems.Network);
        }

        private void HandlePeerDisconnected(NetworkPeer peer)
        {
            clientStates.Remove(peer.PeerId);

            // Recalculate speed - slow client may have left
            if (networkManager.IsHost && IsSpeedReduced)
            {
                int newEffective = CalculateEffectiveSpeed();
                if (newEffective > effectiveSpeed)
                {
                    effectiveSpeed = newEffective;
                    timeManager.SetSpeed(effectiveSpeed);
                    networkManager.SendGameSpeedChange((byte)effectiveSpeed, (uint)timeManager.CurrentTick);

                    if (logProgress)
                        ArchonLogger.Log($"NetworkTimeSync: Speed increased to {effectiveSpeed}x (slow client disconnected)", ArchonLogger.Systems.Network);
                }
            }
        }

        private void CheckAndAdjustSpeed()
        {
            int newEffective = CalculateEffectiveSpeed();

            if (newEffective < effectiveSpeed)
            {
                // Slow down immediately
                effectiveSpeed = newEffective;
                lastSpeedReductionTime = Time.unscaledTime;
                timeManager.SetSpeed(effectiveSpeed);
                networkManager.SendGameSpeedChange((byte)effectiveSpeed, (uint)timeManager.CurrentTick);

                if (logProgress)
                    ArchonLogger.Log($"NetworkTimeSync: Speed reduced to {effectiveSpeed}x (clients lagging)", ArchonLogger.Systems.Network);
            }
            else if (newEffective > effectiveSpeed)
            {
                // Only speed up after cooldown
                if (Time.unscaledTime - lastSpeedReductionTime >= SPEED_REDUCTION_COOLDOWN)
                {
                    effectiveSpeed = newEffective;
                    timeManager.SetSpeed(effectiveSpeed);
                    networkManager.SendGameSpeedChange((byte)effectiveSpeed, (uint)timeManager.CurrentTick);

                    if (logProgress)
                        ArchonLogger.Log($"NetworkTimeSync: Speed increased to {effectiveSpeed}x", ArchonLogger.Systems.Network);
                }
            }
        }

        private int CalculateEffectiveSpeed()
        {
            if (clientStates.Count == 0)
                return requestedSpeed;

            // Find the slowest client
            ushort maxTicksBehind = 0;
            foreach (var state in clientStates.Values)
            {
                if (state.TicksBehind > maxTicksBehind)
                    maxTicksBehind = state.TicksBehind;
            }

            // If any client is too far behind, reduce speed
            if (maxTicksBehind >= MAX_TICKS_BEHIND_THRESHOLD)
            {
                // Reduce speed proportionally
                int reduction = (maxTicksBehind / MAX_TICKS_BEHIND_THRESHOLD);
                int newSpeed = Math.Max(1, requestedSpeed - reduction);
                return newSpeed;
            }

            return requestedSpeed;
        }

        #endregion

        #region Client Logic

        private void HandleTickSync(TickSyncMessage sync)
        {
            if (networkManager.IsHost) return;

            lastReceivedHostTick = sync.CurrentTick;
            ulong localTick = timeManager.CurrentTick;

            // Calculate how far behind we are
            ushort ticksBehind = 0;
            if (sync.CurrentTick > localTick)
            {
                ulong diff = sync.CurrentTick - localTick;
                ticksBehind = (ushort)Math.Min(diff, ushort.MaxValue);
            }

            // Catch up to host tick
            if (ticksBehind > 0)
            {
                timeManager.SynchronizeToTick(sync.CurrentTick);
            }

            // Update local pause/speed state to match host
            bool hostPaused = sync.IsPaused != 0;
            if (hostPaused && !timeManager.IsPaused)
            {
                timeManager.PauseTime();
            }
            else if (!hostPaused && timeManager.IsPaused && isClientSyncing)
            {
                // Start time once we've synced
                timeManager.StartTime();
                isClientSyncing = false;
            }

            // Send ack back to host periodically
            if (timeManager.CurrentTick - lastAckSentTick >= ACK_INTERVAL_TICKS)
            {
                networkManager.SendTickAck(timeManager.CurrentTick, ticksBehind);
                lastAckSentTick = timeManager.CurrentTick;
            }
        }

        #endregion

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            networkManager.OnTickSyncReceived -= HandleTickSync;
            networkManager.OnTickAckReceived -= HandleTickAck;
            networkManager.OnPeerConnected -= HandlePeerConnected;
            networkManager.OnPeerDisconnected -= HandlePeerDisconnected;
            timeManager.OnHourlyTick -= HandleHostTick;

            if (logProgress)
                ArchonLogger.Log("NetworkTimeSync: Disposed", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Per-client sync state tracking (host only).
        /// </summary>
        private class ClientSyncState
        {
            public ulong LastAckedTick;
            public ushort TicksBehind;
            public float LastAckTime;
        }
    }
}
