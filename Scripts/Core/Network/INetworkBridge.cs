using System;

namespace Core.Network
{
    /// <summary>
    /// Bridge interface for network communication.
    /// Core defines this interface, Network (or any custom solution) implements it.
    /// This allows Core to remain network-agnostic while supporting multiplayer.
    ///
    /// Implementations:
    /// - Archon.Network.NetworkBridge (Unity Transport / Steam)
    /// - Custom implementations for other networking solutions
    /// - MockNetworkBridge for testing/replay
    /// </summary>
    public interface INetworkBridge
    {
        /// <summary>
        /// Whether we are the authoritative host.
        /// Host validates and broadcasts commands.
        /// Clients send commands to host for validation.
        /// </summary>
        bool IsHost { get; }

        /// <summary>
        /// Whether we are connected to a multiplayer session.
        /// False for single-player.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Broadcast a command to all connected peers.
        /// Called by CommandProcessor after executing a command (host only).
        /// </summary>
        /// <param name="commandData">Serialized command data</param>
        /// <param name="tick">Game tick the command was executed on</param>
        void BroadcastCommand(byte[] commandData, uint tick);

        /// <summary>
        /// Send a command to the host for validation.
        /// Called by clients when they want to execute a command.
        /// </summary>
        /// <param name="commandData">Serialized command data</param>
        /// <param name="tick">Game tick the command should execute on</param>
        void SendCommandToHost(byte[] commandData, uint tick);

        /// <summary>
        /// Broadcast current state checksum for desync detection.
        /// </summary>
        /// <param name="tick">Game tick this checksum is for</param>
        /// <param name="checksum">State checksum</param>
        void BroadcastChecksum(uint tick, uint checksum);

        /// <summary>
        /// Send full game state to a specific peer (late join / desync recovery).
        /// Host only.
        /// </summary>
        /// <param name="peerId">Target peer</param>
        /// <param name="stateData">Serialized game state</param>
        /// <param name="tick">Current game tick</param>
        void SendStateToPeer(int peerId, byte[] stateData, uint tick);

        /// <summary>
        /// Fired when a command is received from the network.
        /// Host: receives commands from clients for validation.
        /// Client: receives validated commands from host for execution.
        /// </summary>
        event Action<int, byte[], uint> OnCommandReceived;  // peerId, commandData, tick

        /// <summary>
        /// Fired when full state is received (clients only, for late join / desync recovery).
        /// </summary>
        event Action<byte[], uint> OnStateReceived;  // stateData, tick

        /// <summary>
        /// Fired when a checksum is received for comparison.
        /// </summary>
        event Action<int, uint, uint> OnChecksumReceived;  // peerId, tick, checksum

        /// <summary>
        /// Fired when a peer requests full state (host only, for late join).
        /// </summary>
        event Action<int> OnStateSyncRequested;  // peerId
    }
}
