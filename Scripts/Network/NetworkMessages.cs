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
        DesyncRecovery = 0x32,
        TickSync = 0x33,            // Host broadcasts current tick to clients
        TickAck = 0x34,             // Client acknowledges tick (for speed adaptation)

        // Lobby (0x40-0x4F)
        LobbyUpdate = 0x40,
        PlayerReady = 0x41,
        GameStart = 0x42,
        PlayerCountrySelected = 0x43
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

    /// <summary>
    /// Tick sync message. Host sends this to clients periodically.
    /// Clients use this to synchronize their game time.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TickSyncMessage
    {
        public ulong CurrentTick;         // 8 bytes - host's current tick
        public byte GameSpeed;            // 1 byte - current speed level
        public byte IsPaused;             // 1 byte - pause state

        public const int Size = 10;
    }

    /// <summary>
    /// Tick acknowledgement message. Client sends this to confirm tick processing.
    /// Host uses these to detect slow clients and adapt speed.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TickAckMessage
    {
        public ulong AcknowledgedTick;    // 8 bytes - last tick client has processed
        public ushort TicksBehind;        // 2 bytes - how many ticks client is behind

        public const int Size = 10;
    }

    /// <summary>
    /// Player slot info for lobby updates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LobbyPlayerSlot
    {
        public int PeerId;                // 4 bytes
        public ushort CountryId;          // 2 bytes - 0 = not selected
        public byte IsReady;              // 1 byte - 0 = not ready, 1 = ready
        public byte IsHost;               // 1 byte - 0 = client, 1 = host

        public const int Size = 8;
    }

    /// <summary>
    /// Lobby state update message. Sent by host to all clients.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LobbyUpdateHeader
    {
        public byte PlayerCount;          // Number of players in lobby
        public byte LobbyState;           // 0 = waiting, 1 = starting, 2 = in game

        public const int Size = 2;
    }

    /// <summary>
    /// Lobby state enum.
    /// </summary>
    public enum LobbyGameState : byte
    {
        Waiting = 0,
        Starting = 1,
        InGame = 2
    }

    /// <summary>
    /// Player ready message.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerReadyMessage
    {
        public byte IsReady;              // 0 = not ready, 1 = ready
    }

    /// <summary>
    /// Player country selection message.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerCountryMessage
    {
        public ushort CountryId;          // Selected country ID (0 = none)
    }
}
