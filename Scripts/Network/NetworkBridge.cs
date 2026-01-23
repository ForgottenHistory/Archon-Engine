using System;
using Core.Network;

namespace Archon.Network
{
    /// <summary>
    /// Implementation of INetworkBridge using Archon's NetworkManager.
    /// Bridges Core's command system with the network transport layer.
    /// </summary>
    public class NetworkBridge : INetworkBridge
    {
        private readonly NetworkManager networkManager;

        public bool IsHost => networkManager.IsHost;
        public bool IsConnected => networkManager.IsConnected;

        public event Action<int, byte[], uint> OnCommandReceived;
        public event Action<byte[], uint> OnStateReceived;
        public event Action<int, uint, uint> OnChecksumReceived;
        public event Action<int> OnStateSyncRequested;

        public NetworkBridge(NetworkManager networkManager)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));

            // Subscribe to NetworkManager events and forward them
            networkManager.OnCommandBatchReceived += HandleCommandBatchReceived;
            networkManager.OnStateSyncReceived += HandleStateSyncReceived;
        }

        public void BroadcastCommand(byte[] commandData, uint tick)
        {
            if (!IsConnected || !IsHost) return;
            networkManager.SendCommandBatch(commandData, tick);
        }

        public void SendCommandToHost(byte[] commandData, uint tick)
        {
            if (!IsConnected || IsHost) return;
            networkManager.SendCommandBatch(commandData, tick);
        }

        public void BroadcastChecksum(uint tick, uint checksum)
        {
            if (!IsConnected) return;
            networkManager.SendChecksum(tick, checksum);
        }

        public void SendStateToPeer(int peerId, byte[] stateData, uint tick)
        {
            if (!IsConnected || !IsHost) return;
            networkManager.SendStateSync(peerId, stateData, tick);
        }

        private void HandleCommandBatchReceived(int peerId, byte[] data)
        {
            // Extract tick from the data or use current - for now pass 0
            // The actual tick is embedded in the message header, already parsed by NetworkManager
            // We need to enhance this to pass the tick through
            OnCommandReceived?.Invoke(peerId, data, 0);
        }

        private void HandleStateSyncReceived(byte[] stateData, uint tick)
        {
            OnStateReceived?.Invoke(stateData, tick);
        }

        /// <summary>
        /// Unsubscribe from NetworkManager events.
        /// Call when disposing.
        /// </summary>
        public void Detach()
        {
            networkManager.OnCommandBatchReceived -= HandleCommandBatchReceived;
            networkManager.OnStateSyncReceived -= HandleStateSyncReceived;
        }
    }
}
