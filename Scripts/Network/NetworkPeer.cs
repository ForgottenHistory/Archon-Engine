namespace Archon.Network
{
    /// <summary>
    /// State of a network peer connection.
    /// </summary>
    public enum PeerState
    {
        /// <summary>Connection initiated, waiting for handshake.</summary>
        Connecting,

        /// <summary>Handshake complete, receiving initial state sync.</summary>
        Synchronizing,

        /// <summary>Fully connected and synchronized.</summary>
        Connected,

        /// <summary>Desync detected, receiving state resync.</summary>
        Resyncing,

        /// <summary>Disconnecting gracefully.</summary>
        Disconnecting,

        /// <summary>Connection lost or closed.</summary>
        Disconnected
    }

    /// <summary>
    /// Represents a connected peer (client from host's perspective, or host from client's perspective).
    /// </summary>
    public class NetworkPeer
    {
        /// <summary>Unique identifier for this peer within the session.</summary>
        public int PeerId { get; }

        /// <summary>Current connection state.</summary>
        public PeerState State { get; set; }

        /// <summary>Country ID this peer controls (-1 if observer or unassigned).</summary>
        public int ControlledCountryId { get; set; } = -1;

        /// <summary>Display name for this peer.</summary>
        public string DisplayName { get; set; }

        /// <summary>Round-trip time in milliseconds.</summary>
        public int PingMs { get; set; }

        /// <summary>Last tick we received a heartbeat from this peer.</summary>
        public uint LastHeartbeatTick { get; set; }

        /// <summary>Last checksum received from this peer (for desync detection).</summary>
        public uint LastChecksum { get; set; }

        /// <summary>Tick of the last checksum.</summary>
        public uint LastChecksumTick { get; set; }

        public NetworkPeer(int peerId)
        {
            PeerId = peerId;
            State = PeerState.Connecting;
        }

        /// <summary>
        /// Whether this peer is in a state where they can send/receive game commands.
        /// </summary>
        public bool CanSendCommands => State == PeerState.Connected;

        /// <summary>
        /// Whether this peer is in a state where they should receive game state updates.
        /// </summary>
        public bool ShouldReceiveUpdates => State == PeerState.Connected || State == PeerState.Resyncing;
    }
}
