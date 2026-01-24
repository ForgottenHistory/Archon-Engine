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
        // Note: Peer IDs are assigned by the transport layer (DirectTransport.nextPeerId)

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

        /// <summary>Fired when lobby state updates (player list, ready states).</summary>
        public event Action<LobbyUpdateHeader, LobbyPlayerSlot[]> OnLobbyUpdated;

        /// <summary>Fired when host starts the game.</summary>
        public event Action OnGameStarted;

        // Lobby state (host-authoritative)
        private LobbyGameState lobbyState = LobbyGameState.Waiting;
        private readonly Dictionary<int, LobbyPlayerSlot> lobbyPlayers = new();

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

            // Initialize lobby with host
            lobbyState = LobbyGameState.Waiting;
            lobbyPlayers.Clear();
            lobbyPlayers[localPeerId] = new LobbyPlayerSlot
            {
                PeerId = localPeerId,
                CountryId = 0,
                IsReady = 0,
                IsHost = 1
            };

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

        #region Lobby Methods

        /// <summary>
        /// Set local player's ready state.
        /// </summary>
        public void SetReady(bool isReady)
        {
            if (!IsConnected) return;

            var readyMsg = new PlayerReadyMessage { IsReady = (byte)(isReady ? 1 : 0) };
            var data = StructToBytes(readyMsg);
            var message = CreateMessage(NetworkMessageType.PlayerReady, 0, data);

            if (IsHost)
            {
                // Host updates locally and broadcasts
                if (lobbyPlayers.TryGetValue(localPeerId, out var slot))
                {
                    slot.IsReady = readyMsg.IsReady;
                    lobbyPlayers[localPeerId] = slot;
                    BroadcastLobbyUpdate();
                }
            }
            else
            {
                // Client sends to host
                transport.Send(hostPeerId, message, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Set local player's country selection.
        /// </summary>
        public void SetCountrySelection(ushort countryId)
        {
            if (!IsConnected) return;

            var countryMsg = new PlayerCountryMessage { CountryId = countryId };
            var data = StructToBytes(countryMsg);
            var message = CreateMessage(NetworkMessageType.PlayerCountrySelected, 0, data);

            if (IsHost)
            {
                // Host updates locally and broadcasts
                if (lobbyPlayers.TryGetValue(localPeerId, out var slot))
                {
                    slot.CountryId = countryId;
                    lobbyPlayers[localPeerId] = slot;
                    BroadcastLobbyUpdate();
                }
            }
            else
            {
                // Client sends to host
                transport.Send(hostPeerId, message, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>
        /// Start the game. Host only.
        /// </summary>
        public void StartGame()
        {
            if (!IsHost)
            {
                ArchonLogger.LogWarning("Only host can start the game", ArchonLogger.Systems.Network);
                return;
            }

            if (lobbyState != LobbyGameState.Waiting)
            {
                ArchonLogger.LogWarning("Game already starting or started", ArchonLogger.Systems.Network);
                return;
            }

            lobbyState = LobbyGameState.Starting;
            BroadcastLobbyUpdate();

            // Send game start message
            var message = CreateMessage(NetworkMessageType.GameStart, 0, Array.Empty<byte>());
            transport.SendToAll(message, DeliveryMethod.ReliableOrdered);

            lobbyState = LobbyGameState.InGame;

            ArchonLogger.Log("Host started the game", ArchonLogger.Systems.Network);
            OnGameStarted?.Invoke();
        }

        /// <summary>
        /// Get current lobby players.
        /// </summary>
        public LobbyPlayerSlot[] GetLobbyPlayers()
        {
            var slots = new LobbyPlayerSlot[lobbyPlayers.Count];
            int i = 0;
            foreach (var slot in lobbyPlayers.Values)
            {
                slots[i++] = slot;
            }
            return slots;
        }

        /// <summary>
        /// Check if a country is already selected by another player.
        /// </summary>
        public bool IsCountryTaken(ushort countryId, out int takenByPeerId)
        {
            foreach (var kvp in lobbyPlayers)
            {
                if (kvp.Value.CountryId == countryId && kvp.Key != localPeerId)
                {
                    takenByPeerId = kvp.Key;
                    return true;
                }
            }
            takenByPeerId = -1;
            return false;
        }

        private void BroadcastLobbyUpdate()
        {
            if (!IsHost) return;

            // Build lobby update message
            var header = new LobbyUpdateHeader
            {
                PlayerCount = (byte)lobbyPlayers.Count,
                LobbyState = (byte)lobbyState
            };

            // Serialize header + all player slots
            var headerBytes = StructToBytes(header);
            var slotsBytes = new byte[lobbyPlayers.Count * LobbyPlayerSlot.Size];
            int offset = 0;
            foreach (var slot in lobbyPlayers.Values)
            {
                var slotBytes = StructToBytes(slot);
                Array.Copy(slotBytes, 0, slotsBytes, offset, LobbyPlayerSlot.Size);
                offset += LobbyPlayerSlot.Size;
            }

            var payload = new byte[headerBytes.Length + slotsBytes.Length];
            Array.Copy(headerBytes, 0, payload, 0, headerBytes.Length);
            Array.Copy(slotsBytes, 0, payload, headerBytes.Length, slotsBytes.Length);

            var message = CreateMessage(NetworkMessageType.LobbyUpdate, 0, payload);
            transport.SendToAll(message, DeliveryMethod.ReliableOrdered);

            // Also notify local UI
            OnLobbyUpdated?.Invoke(header, GetLobbyPlayers());
        }

        #endregion

        private void HandleClientConnected(int transportPeerId)
        {
            if (IsHost)
            {
                // Host: a new client connected at transport level
                // Use the transport's peer ID directly (transport already assigned it)
                var peer = new NetworkPeer(transportPeerId)
                {
                    State = PeerState.Connecting,
                    DisplayName = $"Player {transportPeerId}"
                };
                peers[transportPeerId] = peer;

                ArchonLogger.Log($"Client connected with peer ID {transportPeerId}", ArchonLogger.Systems.Network);

                // Note: Full connection completes after handshake and state sync
            }
            else
            {
                // Client: we connected to host, send handshake
                ArchonLogger.Log("Connected to host, sending handshake", ArchonLogger.Systems.Network);
                SendHandshake();
            }
        }

        /// <summary>
        /// Send handshake message to host. Called when client connects.
        /// </summary>
        private void SendHandshake()
        {
            var handshake = new HandshakeMessage
            {
                ProtocolVersion = HandshakeMessage.CurrentProtocolVersion,
                GameVersion = 1  // TODO: Get from build info
            };

            var data = StructToBytes(handshake);
            var message = CreateMessage(NetworkMessageType.Handshake, 0, data);
            transport.Send(0, message, DeliveryMethod.ReliableOrdered);
        }

        private void HandleClientDisconnected(int transportId)
        {
            // Find peer by transport ID (we'd need a mapping in real impl)
            // For now, simplified - assumes transportId == peerId
            if (peers.TryGetValue(transportId, out var peer))
            {
                peer.State = PeerState.Disconnected;
                peers.Remove(transportId);

                // Remove from lobby
                if (lobbyPlayers.ContainsKey(transportId))
                {
                    lobbyPlayers.Remove(transportId);
                    if (IsHost && lobbyState == LobbyGameState.Waiting)
                    {
                        BroadcastLobbyUpdate();
                    }
                }

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

                case NetworkMessageType.LobbyUpdate:
                    HandleLobbyUpdate(payload);
                    break;

                case NetworkMessageType.PlayerReady:
                    HandlePlayerReady(peerId, payload);
                    break;

                case NetworkMessageType.PlayerCountrySelected:
                    HandlePlayerCountry(peerId, payload);
                    break;

                case NetworkMessageType.GameStart:
                    HandleGameStart();
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
                peer.State = PeerState.Connected;
                ArchonLogger.Log($"Handshake accepted for peer {peerId}", ArchonLogger.Systems.Network);

                // Add to lobby
                lobbyPlayers[peerId] = new LobbyPlayerSlot
                {
                    PeerId = peerId,
                    CountryId = 0,
                    IsReady = 0,
                    IsHost = 0
                };

                // Notify local and broadcast to all
                OnPeerConnected?.Invoke(peer);
                BroadcastLobbyUpdate();
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

        private void HandleLobbyUpdate(byte[] payload)
        {
            if (IsHost) return;  // Host doesn't receive lobby updates

            if (payload.Length < LobbyUpdateHeader.Size)
            {
                ArchonLogger.LogWarning("Received undersized lobby update", ArchonLogger.Systems.Network);
                return;
            }

            var header = BytesToStruct<LobbyUpdateHeader>(payload, 0);
            lobbyState = (LobbyGameState)header.LobbyState;

            // Parse player slots
            lobbyPlayers.Clear();
            int offset = LobbyUpdateHeader.Size;
            for (int i = 0; i < header.PlayerCount && offset + LobbyPlayerSlot.Size <= payload.Length; i++)
            {
                var slot = BytesToStruct<LobbyPlayerSlot>(payload, offset);
                lobbyPlayers[slot.PeerId] = slot;
                offset += LobbyPlayerSlot.Size;
            }

            ArchonLogger.Log($"Lobby update: {header.PlayerCount} players, state={lobbyState}", ArchonLogger.Systems.Network);
            OnLobbyUpdated?.Invoke(header, GetLobbyPlayers());
        }

        private void HandlePlayerReady(int peerId, byte[] payload)
        {
            if (!IsHost) return;

            var readyMsg = BytesToStruct<PlayerReadyMessage>(payload, 0);

            if (lobbyPlayers.TryGetValue(peerId, out var slot))
            {
                slot.IsReady = readyMsg.IsReady;
                lobbyPlayers[peerId] = slot;
                ArchonLogger.Log($"Player {peerId} ready={readyMsg.IsReady}", ArchonLogger.Systems.Network);
                BroadcastLobbyUpdate();
            }
        }

        private void HandlePlayerCountry(int peerId, byte[] payload)
        {
            if (!IsHost) return;

            var countryMsg = BytesToStruct<PlayerCountryMessage>(payload, 0);

            // Check if country is already taken by another player
            if (countryMsg.CountryId != 0 && IsCountryTaken(countryMsg.CountryId, out int takenBy) && takenBy != peerId)
            {
                ArchonLogger.LogWarning($"Player {peerId} tried to select country {countryMsg.CountryId} already taken by {takenBy}", ArchonLogger.Systems.Network);
                // Could send rejection message here
                return;
            }

            if (lobbyPlayers.TryGetValue(peerId, out var slot))
            {
                slot.CountryId = countryMsg.CountryId;
                lobbyPlayers[peerId] = slot;
                ArchonLogger.Log($"Player {peerId} selected country {countryMsg.CountryId}", ArchonLogger.Systems.Network);
                BroadcastLobbyUpdate();
            }
        }

        private void HandleGameStart()
        {
            if (IsHost) return;  // Host initiates, doesn't receive

            lobbyState = LobbyGameState.InGame;
            ArchonLogger.Log("Game started by host", ArchonLogger.Systems.Network);
            OnGameStarted?.Invoke();
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
