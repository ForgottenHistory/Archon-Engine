using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace Archon.Network
{
    /// <summary>
    /// INetworkTransport implementation using Unity Transport Package.
    /// For development and LAN play. Requires port forwarding for internet.
    ///
    /// Install via Package Manager: com.unity.transport
    /// </summary>
    public class DirectTransport : INetworkTransport
    {
        private NetworkDriver driver;
        private NativeList<NetworkConnection> connections;
        private NetworkConnection clientConnection;

        private readonly Dictionary<NetworkConnection, int> connectionToPeerId = new();
        private readonly Dictionary<int, NetworkConnection> peerIdToConnection = new();
        private int nextPeerId = 1;

        private bool isHost;
        private bool isRunning;
        private bool disposed;

        // Buffer for receiving data
        private NativeArray<byte> receiveBuffer;
        private const int MaxPacketSize = 64 * 1024; // 64KB max packet

        public bool IsRunning => isRunning;
        public bool IsHost => isHost;

        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;
        public event Action<int, byte[]> OnDataReceived;

        public DirectTransport()
        {
            receiveBuffer = new NativeArray<byte>(MaxPacketSize, Allocator.Persistent);
        }

        public void StartHost(int port)
        {
            if (isRunning)
            {
                ArchonLogger.LogWarning("Transport already running", ArchonLogger.Systems.Network);
                return;
            }

            // Configure network settings for faster connection
            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(
                connectTimeoutMS: 500,
                maxConnectAttempts: 20,
                disconnectTimeoutMS: 10000
            );

            driver = NetworkDriver.Create(settings);
            connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

            var endpoint = NetworkEndpoint.AnyIpv4.WithPort((ushort)port);
            if (driver.Bind(endpoint) != 0)
            {
                ArchonLogger.LogError($"Failed to bind to port {port}", ArchonLogger.Systems.Network);
                CleanupDriver();
                return;
            }

            driver.Listen();
            isHost = true;
            isRunning = true;

            ArchonLogger.Log($"DirectTransport hosting on port {port}", ArchonLogger.Systems.Network);
        }

        public void StopHost()
        {
            if (!isRunning || !isHost) return;

            // Disconnect all clients
            for (int i = 0; i < connections.Length; i++)
            {
                if (connections[i].IsCreated)
                {
                    driver.Disconnect(connections[i]);
                }
            }

            CleanupDriver();
            connectionToPeerId.Clear();
            peerIdToConnection.Clear();
            isHost = false;
            isRunning = false;

            ArchonLogger.Log("DirectTransport stopped hosting", ArchonLogger.Systems.Network);
        }

        public void Connect(string address, int port)
        {
            if (isRunning)
            {
                ArchonLogger.LogWarning("Transport already running", ArchonLogger.Systems.Network);
                return;
            }

            // Configure network settings for faster connection
            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(
                connectTimeoutMS: 500,
                maxConnectAttempts: 20,
                disconnectTimeoutMS: 10000
            );

            driver = NetworkDriver.Create(settings);
            // Note: Do NOT call Bind() on client - Unity Transport auto-binds to ephemeral port

            // Handle localhost specially - NetworkEndpoint.Parse doesn't recognize it
            NetworkEndpoint endpoint;
            if (address.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                address == "127.0.0.1")
            {
                endpoint = NetworkEndpoint.LoopbackIpv4.WithPort((ushort)port);
            }
            else
            {
                endpoint = NetworkEndpoint.Parse(address, (ushort)port);
            }

            if (!endpoint.IsValid)
            {
                ArchonLogger.LogError($"Invalid endpoint: {address}:{port}", ArchonLogger.Systems.Network);
                CleanupDriver();
                return;
            }

            clientConnection = driver.Connect(endpoint);
            isHost = false;
            isRunning = true;

            ArchonLogger.Log($"DirectTransport connecting to {address}:{port}", ArchonLogger.Systems.Network);
        }

        public void Disconnect()
        {
            if (!isRunning || isHost) return;

            if (clientConnection.IsCreated)
            {
                driver.Disconnect(clientConnection);
            }

            CleanupDriver();
            clientConnection = default;
            isRunning = false;

            ArchonLogger.Log("DirectTransport disconnected", ArchonLogger.Systems.Network);
        }

        public void Send(int peerId, byte[] data, DeliveryMethod method)
        {
            if (!isRunning) return;

            NetworkConnection connection;
            if (isHost)
            {
                if (!peerIdToConnection.TryGetValue(peerId, out connection))
                {
                    ArchonLogger.LogWarning($"Unknown peer ID: {peerId}", ArchonLogger.Systems.Network);
                    return;
                }
            }
            else
            {
                connection = clientConnection;
            }

            if (!connection.IsCreated) return;

            var pipeline = GetPipeline(method);
            SendData(connection, data, pipeline);
        }

        public void SendToAll(byte[] data, DeliveryMethod method)
        {
            if (!isRunning || !isHost) return;

            var pipeline = GetPipeline(method);
            for (int i = 0; i < connections.Length; i++)
            {
                if (connections[i].IsCreated)
                {
                    SendData(connections[i], data, pipeline);
                }
            }
        }

        public void SendToAllExcept(int excludePeerId, byte[] data, DeliveryMethod method)
        {
            if (!isRunning || !isHost) return;

            var pipeline = GetPipeline(method);
            for (int i = 0; i < connections.Length; i++)
            {
                if (!connections[i].IsCreated) continue;

                if (connectionToPeerId.TryGetValue(connections[i], out int peerId) && peerId != excludePeerId)
                {
                    SendData(connections[i], data, pipeline);
                }
            }
        }

        public void Poll()
        {
            if (!isRunning) return;

            driver.ScheduleUpdate().Complete();

            if (isHost)
            {
                PollHost();
            }
            else
            {
                PollClient();
            }
        }

        private void PollHost()
        {
            // Accept new connections
            NetworkConnection newConnection;
            while ((newConnection = driver.Accept()) != default)
            {
                connections.Add(newConnection);

                int peerId = nextPeerId++;
                connectionToPeerId[newConnection] = peerId;
                peerIdToConnection[peerId] = newConnection;

                ArchonLogger.Log($"Client connected, assigned peer ID {peerId}", ArchonLogger.Systems.Network);
                OnClientConnected?.Invoke(peerId);
            }

            // Process events for all connections
            for (int i = 0; i < connections.Length; i++)
            {
                if (!connections[i].IsCreated) continue;

                ProcessConnectionEvents(connections[i]);
            }

            // Clean up stale connections
            for (int i = connections.Length - 1; i >= 0; i--)
            {
                if (!connections[i].IsCreated)
                {
                    connections.RemoveAtSwapBack(i);
                }
            }
        }

        private void PollClient()
        {
            if (!clientConnection.IsCreated)
            {
                return;
            }

            // Client must use connection.PopEvent(), not driver.PopEventForConnection()
            // This is a key difference from server-side event handling
            NetworkEvent.Type eventType;
            while ((eventType = clientConnection.PopEvent(driver, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    case NetworkEvent.Type.Connect:
                        ArchonLogger.Log("Connected to host", ArchonLogger.Systems.Network);
                        OnClientConnected?.Invoke(0); // 0 = host from client perspective
                        break;

                    case NetworkEvent.Type.Data:
                        HandleReceivedData(clientConnection, ref reader);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        ArchonLogger.Log("Disconnected from host", ArchonLogger.Systems.Network);
                        clientConnection = default;
                        break;
                }
            }

            if (!clientConnection.IsCreated)
            {
                // Connection was lost
                isRunning = false;
                OnClientDisconnected?.Invoke(0);
            }
        }

        private void ProcessConnectionEvents(NetworkConnection connection)
        {
            NetworkEvent.Type eventType;
            while ((eventType = driver.PopEventForConnection(connection, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    case NetworkEvent.Type.Connect:
                        ArchonLogger.Log("Connected to host", ArchonLogger.Systems.Network);
                        // Client connected to host - send handshake
                        OnClientConnected?.Invoke(0); // 0 = host from client perspective
                        break;

                    case NetworkEvent.Type.Data:
                        HandleReceivedData(connection, ref reader);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        HandleDisconnect(connection);
                        break;
                }
            }
        }

        private void HandleReceivedData(NetworkConnection connection, ref DataStreamReader reader)
        {
            int length = reader.Length;
            if (length > MaxPacketSize)
            {
                ArchonLogger.LogWarning($"Received oversized packet: {length} bytes", ArchonLogger.Systems.Network);
                return;
            }

            // Read into NativeArray first, then copy to managed array
            var nativeData = new NativeArray<byte>(length, Allocator.Temp);
            reader.ReadBytes(nativeData);

            var data = new byte[length];
            nativeData.CopyTo(data);
            nativeData.Dispose();

            int peerId = 0;
            if (isHost && connectionToPeerId.TryGetValue(connection, out int id))
            {
                peerId = id;
            }

            OnDataReceived?.Invoke(peerId, data);
        }

        private void HandleDisconnect(NetworkConnection connection)
        {
            if (isHost)
            {
                if (connectionToPeerId.TryGetValue(connection, out int peerId))
                {
                    ArchonLogger.Log($"Peer {peerId} disconnected", ArchonLogger.Systems.Network);
                    connectionToPeerId.Remove(connection);
                    peerIdToConnection.Remove(peerId);
                    OnClientDisconnected?.Invoke(peerId);
                }
            }
            else
            {
                ArchonLogger.Log("Disconnected from host", ArchonLogger.Systems.Network);
                clientConnection = default;
            }
        }

        private void SendData(NetworkConnection connection, byte[] data, NetworkPipeline pipeline)
        {
            var status = driver.BeginSend(pipeline, connection, out var writer);
            if (status != (int)Unity.Networking.Transport.Error.StatusCode.Success)
            {
                ArchonLogger.LogWarning($"BeginSend failed with status {status}", ArchonLogger.Systems.Network);
                return;
            }

            var nativeData = new NativeArray<byte>(data, Allocator.Temp);
            writer.WriteBytes(nativeData);
            nativeData.Dispose();

            driver.EndSend(writer);
        }

        private NetworkPipeline GetPipeline(DeliveryMethod method)
        {
            // Unity Transport uses pipelines for reliability
            // Default pipeline (NetworkPipeline.Null) is unreliable
            // For reliable delivery, we'd need to create pipelines at startup
            // Simplified for now - all traffic uses default
            // TODO: Create reliable pipeline at startup for ReliableOrdered
            return NetworkPipeline.Null;
        }

        private void CleanupDriver()
        {
            if (connections.IsCreated)
            {
                connections.Dispose();
            }

            if (driver.IsCreated)
            {
                driver.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (isRunning)
            {
                if (isHost)
                    StopHost();
                else
                    Disconnect();
            }

            CleanupDriver();

            if (receiveBuffer.IsCreated)
            {
                receiveBuffer.Dispose();
            }
        }
    }
}
