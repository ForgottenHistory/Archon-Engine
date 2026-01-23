using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Archon.Network
{
    /// <summary>
    /// High-level network coordinator. Sits between game logic and transport.
    /// Handles peer management, message routing, and command synchronization.
    /// </summary>
    public class NetworkManager : IDisposable
    {
        private INetworkTransport transport;
        private readonly Dictionary<int, NetworkPeer> peers = new();
        private readonly List<byte[]> pendingOutboundMessages = new();

        private int localPeerId = -1;
        private int hostPeerId = 0;  // Host is always peer 0
        private int nextPeerId = 1;  // Next ID to assign to connecting clients

        private bool disposed;

        /// <summary>Whether we are the host.</summary>
        public bool IsHost => transport?.IsHost ?? false;

        /// <summary>Whether we are connected (as host or client).</summary>
        public bool IsConnected => transport?.IsRunning ?? false;

        /// <summary>Our local peer ID (-1 if not connected).</summary>
        public int LocalPeerId => localPeerId;

        /// <summary>All connected peers (read-only).</summary>
        public IReadOnlyDictionary<int, NetworkPeer> Peers => peers;

        /// <summary>Fired when a peer fully connects and synchronizes.</summary>
        public event Action<NetworkPeer> OnPeerConnected;

        /// <summary>Fired when a peer disconnects.</summary>
        public event Action<NetworkPeer> OnPeerDisconnected;

        /// <summary>Fired when a command batch is received (from host if client, from any peer if host).</summary>
        public event Action<int, byte[]> OnCommandBatchReceived;

        /// <summary>Fired when a full state sync is received (clients only).</summary>
        public event Action<byte[], uint> OnStateSyncReceived;

        /// <summary>Fired when a checksum mismatch is detected.</summary>
        public event Action<int, uint, uint, uint> OnDesyncDetected;  // peerId, tick, expected, actual

        /// <summary>Fired when game speed changes (from host).</summary>
        public event Action<byte> OnGameSpeedChanged;

        /// <summary>
        /// Initialize with a transport implementation.
        /// </summary>
        public void Initialize(INetworkTransport networkTransport)
        {
            transport = networkTransport ?? throw new ArgumentNullException(nameof(networkTransport));

            transport.OnClientConnected += HandleClientConnected;
            transport.OnClientDisconnected += HandleClientDisconnected;
            transport.OnDataReceived += HandleDataReceived;

            ArchonLogger.Log("NetworkManager initialized", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Start hosting a game session.
        /// </summary>
        public void Host(int port)
        {
            if (transport == null)
                throw new InvalidOperationException("NetworkManager not initialized");

            transport.StartHost(port);
            localPeerId = hostPeerId;

            // Add ourselves as a peer
            var selfPeer = new NetworkPeer(localPeerId)
            {
                State = PeerState.Connected,
                DisplayName = "Host"
            };
            peers[localPeerId] = selfPeer;

            ArchonLogger.Log($"Hosting on port {port}", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Connect to a host.
        /// </summary>
        public void Connect(string address, int port)
        {
            if (transport == null)
                throw new InvalidOperationException("NetworkManager not initialized");

            transport.Connect(address, port);

            ArchonLogger.Log($"Connecting to {address}:{port}", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Disconnect from the session.
        /// </summary>
        public void Disconnect()
        {
            if (transport == null) return;

            if (IsHost)
            {
                transport.StopHost();
            }
            else
            {
                transport.Disconnect();
            }

            peers.Clear();
            localPeerId = -1;

            ArchonLogger.Log("Disconnected", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Must be called each frame to process network events.
        /// </summary>
        public void Poll()
        {
            transport?.Poll();
        }

        /// <summary>
        /// Send a command batch to the appropriate destination.
        /// If host: broadcast to all clients.
        /// If client: send to host for validation.
        /// </summary>
        public void SendCommandBatch(byte[] commandData, uint tick)
        {
            if (!IsConnected) return;

            var message = CreateMessage(NetworkMessageType.CommandBatch, tick, commandData);

            if (IsHost)
            {
                // Broadcast to all clients
                transport.SendToAllExcept(localPeerId, message, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                // Send to host for validation
                transport.Send(hostPeerId, message, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Send a checksum for desync detection.
        /// </summary>
        public void SendChecksum(uint tick, uint checksum)
        {
            if (!IsConnected) return;

            var checksumMsg = new ChecksumMessage { Tick = tick, Checksum = checksum };
            var data = StructToBytes(checksumMsg);
            var message = CreateMessage(NetworkMessageType.ChecksumResponse, tick, data);

            if (IsHost)
            {
                // Host broadcasts authoritative checksum to all
                transport.SendToAll(message, DeliveryMethod.ReliableUnordered);
            }
            else
            {
                // Client sends checksum to host
                transport.Send(hostPeerId, message, DeliveryMethod.ReliableUnordered);
            }
        }

        /// <summary>
        /// Send full state to a specific peer (for late join or desync recovery).
        /// Host only.
        /// </summary>
        public void SendStateSync(int peerId, byte[] stateData, uint tick)
        {
            if (!IsHost)
            {
                ArchonLogger.LogWarning("Only host can send state sync", ArchonLogger.Systems.Network);
                return;
            }

            var message = CreateMessage(NetworkMessageType.StateSync, tick, stateData);
            transport.Send(peerId, message, DeliveryMethod.ReliableOrdered);

            ArchonLogger.Log($"Sent state sync to peer {peerId} ({stateData.Length} bytes)", ArchonLogger.Systems.Network);
        }

        /// <summary>
        /// Broadcast game speed change. Host only.
        /// </summary>
        public void SendGameSpeedChange(byte speedLevel, uint tick)
        {
            if (!IsHost) return;

            var speedMsg = new GameSpeedMessage { SpeedLevel = speedLevel };
            var data = StructToBytes(speedMsg);
            var message = CreateMessage(NetworkMessageType.GameSpeedChange, tick, data);

            transport.SendToAll(message, DeliveryMethod.ReliableOrdered);
        }

        private void HandleClientConnected(int transportId)
        {
            if (!IsHost) return;

            int peerId = nextPeerId++;
            var peer = new NetworkPeer(peerId)
            {
                State = PeerState.Connecting,
                DisplayName = $"Player {peerId}"
            };
            peers[peerId] = peer;

            ArchonLogger.Log($"Client connected, assigned peer ID {peerId}", ArchonLogger.Systems.Network);

            // Note: Full connection completes after handshake and state sync
        }

        private void HandleClientDisconnected(int transportId)
        {
            // Find peer by transport ID (we'd need a mapping in real impl)
            // For now, simplified - assumes transportId == peerId
            if (peers.TryGetValue(transportId, out var peer))
            {
                peer.State = PeerState.Disconnected;
                peers.Remove(transportId);

                ArchonLogger.Log($"Peer {transportId} disconnected", ArchonLogger.Systems.Network);
                OnPeerDisconnected?.Invoke(peer);
            }
        }

        private void HandleDataReceived(int peerId, byte[] data)
        {
            if (data.Length < NetworkMessageHeader.Size)
            {
                ArchonLogger.LogWarning($"Received undersized message from peer {peerId}", ArchonLogger.Systems.Network);
                return;
            }

            var header = BytesToStruct<NetworkMessageHeader>(data, 0);
            var payload = new byte[header.PayloadSize];
            if (header.PayloadSize > 0)
            {
                Array.Copy(data, NetworkMessageHeader.Size, payload, 0, header.PayloadSize);
            }

            switch (header.Type)
            {
                case NetworkMessageType.Handshake:
                    HandleHandshake(peerId, payload);
                    break;

                case NetworkMessageType.HandshakeResponse:
                    HandleHandshakeResponse(payload);
                    break;

                case NetworkMessageType.CommandBatch:
                    HandleCommandBatch(peerId, payload, header.Tick);
                    break;

                case NetworkMessageType.StateSync:
                    HandleStateSync(payload, header.Tick);
                    break;

                case NetworkMessageType.ChecksumResponse:
                    HandleChecksum(peerId, payload);
                    break;

                case NetworkMessageType.GameSpeedChange:
                    HandleGameSpeedChange(payload);
                    break;

                case NetworkMessageType.Heartbeat:
                    HandleHeartbeat(peerId, header.Tick);
                    break;

                default:
                    ArchonLogger.LogWarning($"Unknown message type {header.Type} from peer {peerId}", ArchonLogger.Systems.Network);
                    break;
            }
        }

        private void HandleHandshake(int peerId, byte[] payload)
        {
            if (!IsHost) return;

            var handshake = BytesToStruct<HandshakeMessage>(payload, 0);

            // Validate version
            byte rejectReason = 0;
            if (handshake.ProtocolVersion != HandshakeMessage.CurrentProtocolVersion)
            {
                rejectReason = (byte)HandshakeRejectReason.VersionMismatch;
            }

            var response = new HandshakeResponseMessage
            {
                Accepted = (byte)(rejectReason == 0 ? 1 : 0),
                RejectReason = rejectReason,
                AssignedPeerId = peerId,
                CurrentTick = 0  // TODO: Get from game state
            };

            var responseData = StructToBytes(response);
            var message = CreateMessage(NetworkMessageType.HandshakeResponse, 0, responseData);
            transport.Send(peerId, message, DeliveryMethod.ReliableOrdered);

            if (rejectReason == 0 && peers.TryGetValue(peerId, out var peer))
            {
                peer.State = PeerState.Synchronizing;
                ArchonLogger.Log($"Handshake accepted for peer {peerId}", ArchonLogger.Systems.Network);

                // TODO: Trigger state sync to this peer
            }
        }

        private void HandleHandshakeResponse(byte[] payload)
        {
            if (IsHost) return;

            var response = BytesToStruct<HandshakeResponseMessage>(payload, 0);

            if (response.Accepted == 1)
            {
                localPeerId = response.AssignedPeerId;

                var selfPeer = new NetworkPeer(localPeerId)
                {
                    State = PeerState.Synchronizing,
                    DisplayName = "Local"
                };
                peers[localPeerId] = selfPeer;

                ArchonLogger.Log($"Connected to host, assigned peer ID {localPeerId}", ArchonLogger.Systems.Network);
            }
            else
            {
                ArchonLogger.LogWarning($"Connection rejected: {(HandshakeRejectReason)response.RejectReason}", ArchonLogger.Systems.Network);
                Disconnect();
            }
        }

        private void HandleCommandBatch(int peerId, byte[] payload, uint tick)
        {
            if (IsHost)
            {
                // Validate sender can send commands
                if (!peers.TryGetValue(peerId, out var peer) || !peer.CanSendCommands)
                {
                    ArchonLogger.LogWarning($"Ignoring commands from peer {peerId} (not ready)", ArchonLogger.Systems.Network);
                    return;
                }

                // Forward to game logic for validation and execution
                OnCommandBatchReceived?.Invoke(peerId, payload);
            }
            else
            {
                // Client receives confirmed commands from host
                OnCommandBatchReceived?.Invoke(peerId, payload);
            }
        }

        private void HandleStateSync(byte[] payload, uint tick)
        {
            if (IsHost) return;  // Host doesn't receive state sync

            ArchonLogger.Log($"Received state sync ({payload.Length} bytes) at tick {tick}", ArchonLogger.Systems.Network);

            OnStateSyncReceived?.Invoke(payload, tick);

            // Mark ourselves as connected
            if (peers.TryGetValue(localPeerId, out var peer))
            {
                peer.State = PeerState.Connected;
            }
        }

        private void HandleChecksum(int peerId, byte[] payload)
        {
            var checksumMsg = BytesToStruct<ChecksumMessage>(payload, 0);

            if (IsHost)
            {
                // Store client's checksum for comparison
                if (peers.TryGetValue(peerId, out var peer))
                {
                    peer.LastChecksum = checksumMsg.Checksum;
                    peer.LastChecksumTick = checksumMsg.Tick;
                }
            }
            else
            {
                // Compare host's checksum with our own
                // Game layer will call back with local checksum for comparison
            }
        }

        private void HandleGameSpeedChange(byte[] payload)
        {
            var speedMsg = BytesToStruct<GameSpeedMessage>(payload, 0);
            OnGameSpeedChanged?.Invoke(speedMsg.SpeedLevel);
        }

        private void HandleHeartbeat(int peerId, uint tick)
        {
            if (peers.TryGetValue(peerId, out var peer))
            {
                peer.LastHeartbeatTick = tick;
            }
        }

        private byte[] CreateMessage(NetworkMessageType type, uint tick, byte[] payload)
        {
            var header = new NetworkMessageHeader(type, tick, (ushort)(payload?.Length ?? 0));
            var headerBytes = StructToBytes(header);

            var message = new byte[NetworkMessageHeader.Size + (payload?.Length ?? 0)];
            Array.Copy(headerBytes, 0, message, 0, NetworkMessageHeader.Size);

            if (payload != null && payload.Length > 0)
            {
                Array.Copy(payload, 0, message, NetworkMessageHeader.Size, payload.Length);
            }

            return message;
        }

        private static byte[] StructToBytes<T>(T value) where T : unmanaged
        {
            int size = Marshal.SizeOf<T>();
            var bytes = new byte[size];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                handle.Free();
            }
            return bytes;
        }

        private static T BytesToStruct<T>(byte[] data, int offset) where T : unmanaged
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject() + offset);
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Disconnect();

            if (transport != null)
            {
                transport.OnClientConnected -= HandleClientConnected;
                transport.OnClientDisconnected -= HandleClientDisconnected;
                transport.OnDataReceived -= HandleDataReceived;
                transport.Dispose();
                transport = null;
            }
        }
    }
}
