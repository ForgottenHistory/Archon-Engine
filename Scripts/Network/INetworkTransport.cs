using System;

namespace Archon.Network
{
    /// <summary>
    /// Transport-agnostic network interface.
    /// Implementations handle the actual network I/O.
    /// </summary>
    public interface INetworkTransport : IDisposable
    {
        /// <summary>Whether the transport is currently running (hosting or connected).</summary>
        bool IsRunning { get; }

        /// <summary>Whether this instance is the host/server.</summary>
        bool IsHost { get; }

        /// <summary>Fired when a client connects (host only). Parameter is peerId.</summary>
        event Action<int> OnClientConnected;

        /// <summary>Fired when a client disconnects. Parameter is peerId.</summary>
        event Action<int> OnClientDisconnected;

        /// <summary>Fired when data is received. Parameters are peerId and data.</summary>
        event Action<int, byte[]> OnDataReceived;

        /// <summary>Start hosting on the specified port.</summary>
        void StartHost(int port);

        /// <summary>Stop hosting and disconnect all clients.</summary>
        void StopHost();

        /// <summary>Connect to a host at the specified address and port.</summary>
        void Connect(string address, int port);

        /// <summary>Disconnect from the current host.</summary>
        void Disconnect();

        /// <summary>Send data to a specific peer.</summary>
        void Send(int peerId, byte[] data, DeliveryMethod method);

        /// <summary>Send data to all connected peers.</summary>
        void SendToAll(byte[] data, DeliveryMethod method);

        /// <summary>Send data to all peers except the specified one.</summary>
        void SendToAllExcept(int excludePeerId, byte[] data, DeliveryMethod method);

        /// <summary>Must be called each frame to process incoming/outgoing data.</summary>
        void Poll();
    }

    /// <summary>
    /// Delivery guarantees for network messages.
    /// </summary>
    public enum DeliveryMethod
    {
        /// <summary>Fire and forget. No guarantee of delivery or order.</summary>
        Unreliable,

        /// <summary>Guaranteed delivery, but may arrive out of order.</summary>
        ReliableUnordered,

        /// <summary>Guaranteed delivery in order. Use for commands.</summary>
        ReliableOrdered
    }
}
