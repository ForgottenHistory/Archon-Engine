using System.Runtime.InteropServices;

namespace Archon.Network
{
    /// <summary>
    /// Network message types. First byte of every message.
    /// </summary>
    public enum NetworkMessageType : byte
    {
        // Connection (0x01-0x0F)
        Handshake = 0x01,
        HandshakeResponse = 0x02,
        Disconnect = 0x03,
        Heartbeat = 0x04,

        // Game state (0x10-0x1F)
        CommandBatch = 0x10,
        StateSync = 0x11,
        StateDelta = 0x12,

        // Session management (0x20-0x2F)
        PlayerJoined = 0x20,
        PlayerLeft = 0x21,
        GameSpeedChange = 0x22,
        PauseRequest = 0x23,

        // Synchronization (0x30-0x3F)
        ChecksumRequest = 0x30,
        ChecksumResponse = 0x31,
        DesyncRecovery = 0x32
    }

    /// <summary>
    /// Header for all network messages. 7 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NetworkMessageHeader
    {
        public NetworkMessageType Type;   // 1 byte
        public uint Tick;                 // 4 bytes - game tick this message relates to
        public ushort PayloadSize;        // 2 bytes

        public const int Size = 7;

        public NetworkMessageHeader(NetworkMessageType type, uint tick, ushort payloadSize)
        {
            Type = type;
            Tick = tick;
            PayloadSize = payloadSize;
        }
    }

    /// <summary>
    /// Handshake sent by client when connecting.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HandshakeMessage
    {
        public uint ProtocolVersion;      // 4 bytes
        public uint GameVersion;          // 4 bytes

        public const uint CurrentProtocolVersion = 1;
    }

    /// <summary>
    /// Handshake response from host.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HandshakeResponseMessage
    {
        public byte Accepted;             // 1 = accepted, 0 = rejected
        public byte RejectReason;         // 0 = none, 1 = version mismatch, 2 = game full, 3 = game in progress
        public int AssignedPeerId;        // Peer ID assigned by host
        public uint CurrentTick;          // Current game tick for sync
    }

    /// <summary>
    /// Reject reasons for handshake.
    /// </summary>
    public enum HandshakeRejectReason : byte
    {
        None = 0,
        VersionMismatch = 1,
        GameFull = 2,
        GameInProgress = 3,
        Banned = 4
    }

    /// <summary>
    /// Checksum message for desync detection.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChecksumMessage
    {
        public uint Tick;                 // 4 bytes - tick this checksum is for
        public uint Checksum;             // 4 bytes - FNV-1a hash of game state
    }

    /// <summary>
    /// Game speed change message.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GameSpeedMessage
    {
        public byte SpeedLevel;           // 0 = paused, 1-5 = speed levels
    }
}
