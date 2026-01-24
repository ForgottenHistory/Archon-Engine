using UnityEngine;
using Core;
using Core.Commands;
using Core.Network;
using Archon.Network;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Initializes multiplayer networking.
    /// Wires up transport, manager, and bridge to CommandProcessor.
    /// Handles lobby flow: mode selection → lobby → game start.
    /// </summary>
    public class NetworkInitializer : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool logProgress = true;

        // Network components
        private DirectTransport transport;
        private NetworkManager networkManager;
        private NetworkBridge networkBridge;
        private LateJoinHandler lateJoinHandler;
        private DesyncDetector desyncDetector;
        private DesyncRecovery desyncRecovery;
        private NetworkTimeSync networkTimeSync;

        // References
        private GameState gameState;
        private CommandProcessor commandProcessor;
        private LobbyUI lobbyUI;
        private CountrySelectionUI countrySelectionUI;

        private bool isInitialized;
        private bool isNetworkActive;

        /// <summary>Whether network is initialized and ready.</summary>
        public bool IsInitialized => isInitialized;

        /// <summary>Whether we are in a multiplayer session.</summary>
        public bool IsMultiplayer => isNetworkActive && networkManager?.IsConnected == true;

        /// <summary>Whether we are the host.</summary>
        public bool IsHost => networkManager?.IsHost ?? false;

        /// <summary>The NetworkManager instance.</summary>
        public NetworkManager NetworkManager => networkManager;

        /// <summary>The NetworkTimeSync for multiplayer time control.</summary>
        public NetworkTimeSync TimeSync => networkTimeSync;

        /// <summary>
        /// Check if a country is controlled by a human player in multiplayer.
        /// </summary>
        public bool IsCountryHumanControlled(ushort countryId)
        {
            if (!IsMultiplayer || networkManager == null)
                return false;

            var players = networkManager.GetLobbyPlayers();
            foreach (var player in players)
            {
                if (player.CountryId == countryId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Initialize networking components.
        /// Called by Initializer after ENGINE is ready.
        /// </summary>
        public void Initialize(GameState gameStateRef, LobbyUI lobbyUIRef, CountrySelectionUI countrySelectionUIRef)
        {
            gameState = gameStateRef;
            lobbyUI = lobbyUIRef;
            countrySelectionUI = countrySelectionUIRef;

            // Get CommandProcessor from GameState
            commandProcessor = gameState?.CommandProcessor;

            if (commandProcessor == null)
            {
                ArchonLogger.LogWarning("NetworkInitializer: CommandProcessor not available - multiplayer disabled", "starter_kit");
                return;
            }

            // Subscribe to lobby UI events
            if (lobbyUI != null)
            {
                lobbyUI.OnSinglePlayerSelected += HandleSinglePlayerSelected;
                lobbyUI.OnHostSelected += HandleHostSelected;
                lobbyUI.OnJoinSelected += HandleJoinSelected;
                lobbyUI.OnCancelled += HandleCancelled;
                lobbyUI.OnStartGameClicked += HandleStartGameClicked;
            }

            isInitialized = true;

            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Initialized", "starter_kit");
        }

        private void OnDestroy()
        {
            Shutdown();

            if (lobbyUI != null)
            {
                lobbyUI.OnSinglePlayerSelected -= HandleSinglePlayerSelected;
                lobbyUI.OnHostSelected -= HandleHostSelected;
                lobbyUI.OnJoinSelected -= HandleJoinSelected;
                lobbyUI.OnCancelled -= HandleCancelled;
                lobbyUI.OnStartGameClicked -= HandleStartGameClicked;
            }
        }

        private void Update()
        {
            // Poll network each frame
            networkManager?.Poll();
            lateJoinHandler?.Update();
        }

        #region Lobby Event Handlers

        private void HandleSinglePlayerSelected()
        {
            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Single player mode", "starter_kit");

            // No network setup needed - proceed directly to country selection
            countrySelectionUI?.Show();
        }

        private void HandleHostSelected(int port)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Hosting on port {port}", "starter_kit");

            SetupNetworkComponents();
            networkManager.Host(port);
            isNetworkActive = true;

            // Update lobby with initial player list (just host)
            UpdateLobbyDisplay();

            lobbyUI?.SetStatus($"Waiting for players on port {port}...");
        }

        private void HandleJoinSelected(string address, int port)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Joining {address}:{port}", "starter_kit");

            SetupNetworkComponents();
            networkManager.Connect(address, port);
            isNetworkActive = true;

            lobbyUI?.SetStatus("Connecting...");
        }

        private void HandleCancelled()
        {
            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Cancelled", "starter_kit");

            Shutdown();
        }

        private void HandleStartGameClicked()
        {
            if (!IsHost)
            {
                ArchonLogger.LogWarning("NetworkInitializer: Only host can start the game", "starter_kit");
                return;
            }

            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Host starting game", "starter_kit");

            lobbyUI?.OnGameStarting();
            networkManager?.StartGame();
        }

        #endregion

        #region Network Setup

        private void SetupNetworkComponents()
        {
            // Create transport
            transport = new DirectTransport();

            // Create manager
            networkManager = new NetworkManager();
            networkManager.Initialize(transport);

            // Create bridge and attach to CommandProcessor
            networkBridge = new NetworkBridge(networkManager);
            commandProcessor.SetNetworkBridge(networkBridge);

            // Create sync components
            lateJoinHandler = new LateJoinHandler(networkManager, networkBridge);
            desyncDetector = new DesyncDetector(networkManager, networkBridge);
            desyncRecovery = new DesyncRecovery(networkManager, desyncDetector, lateJoinHandler);

            // Create time sync (requires TimeManager from GameState)
            var timeManager = gameState?.GetComponent<Core.Systems.TimeManager>();
            if (timeManager != null)
            {
                networkTimeSync = new NetworkTimeSync(networkManager, timeManager, logProgress);
            }

            // Subscribe to network events
            networkManager.OnPeerConnected += HandlePeerConnected;
            networkManager.OnPeerDisconnected += HandlePeerDisconnected;
            networkManager.OnLobbyUpdated += HandleLobbyUpdated;
            networkManager.OnGameStarted += HandleGameStarted;
            networkBridge.OnStateReceived += HandleStateReceived;
            desyncRecovery.OnDesyncDetected += HandleDesyncDetected;
            desyncRecovery.OnResyncComplete += HandleResyncComplete;

            // Give LobbyUI reference to NetworkManager for country selection
            lobbyUI?.SetNetworkManager(networkManager);

            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Network components created", "starter_kit");
        }

        private void Shutdown()
        {
            if (!isNetworkActive) return;

            // Detach from CommandProcessor
            commandProcessor?.SetNetworkBridge(null);

            // Unsubscribe from events
            if (networkManager != null)
            {
                networkManager.OnPeerConnected -= HandlePeerConnected;
                networkManager.OnPeerDisconnected -= HandlePeerDisconnected;
                networkManager.OnLobbyUpdated -= HandleLobbyUpdated;
                networkManager.OnGameStarted -= HandleGameStarted;
            }

            if (networkBridge != null)
            {
                networkBridge.OnStateReceived -= HandleStateReceived;
            }

            if (desyncRecovery != null)
            {
                desyncRecovery.OnDesyncDetected -= HandleDesyncDetected;
                desyncRecovery.OnResyncComplete -= HandleResyncComplete;
            }

            // Cleanup sync components
            networkTimeSync?.Dispose();
            desyncRecovery?.Detach();
            desyncDetector?.Detach();
            lateJoinHandler?.Detach();
            networkBridge?.Detach();

            // Shutdown network
            networkManager?.Dispose();
            transport?.Dispose();

            transport = null;
            networkManager = null;
            networkBridge = null;
            lateJoinHandler = null;
            desyncDetector = null;
            desyncRecovery = null;
            networkTimeSync = null;

            isNetworkActive = false;

            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Shutdown complete", "starter_kit");
        }

        #endregion

        #region Network Event Handlers

        private void HandlePeerConnected(NetworkPeer peer)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Peer {peer.PeerId} connected", "starter_kit");

            if (IsHost)
            {
                lobbyUI?.SetStatus($"Player {peer.PeerId} joined");
                UpdateLobbyDisplay();
            }
            else
            {
                // We connected to host - wait for lobby update
                lobbyUI?.OnConnectionEstablished();
            }
        }

        private void HandlePeerDisconnected(NetworkPeer peer)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Peer {peer.PeerId} disconnected", "starter_kit");

            if (!IsHost && peer.PeerId == 0)
            {
                // Host disconnected
                lobbyUI?.OnConnectionFailed("Host disconnected");
                Shutdown();
            }
            else if (IsHost)
            {
                lobbyUI?.SetStatus($"Player {peer.PeerId} left");
                UpdateLobbyDisplay();
            }
        }

        private void HandleLobbyUpdated(LobbyUpdateHeader header, LobbyPlayerSlot[] players)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Lobby updated - {players.Length} players", "starter_kit");

            lobbyUI?.UpdatePlayerList(players);
            lobbyUI?.SetStatus($"{players.Length} player(s) in lobby");
        }

        private void HandleGameStarted()
        {
            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Game started", "starter_kit");

            // Start multiplayer time sync
            networkTimeSync?.StartMultiplayerSync();

            // Get the country selected in the lobby
            ushort selectedCountry = lobbyUI?.SelectedCountryId ?? 0;

            // Hide lobby (don't show country selection - it was done in lobby)
            lobbyUI?.Hide();

            if (selectedCountry > 0)
            {
                // Set the player's country from lobby selection
                var playerState = Initializer.Instance?.PlayerState;
                playerState?.SetPlayerCountry(selectedCountry);

                // Emit event to start the game
                gameState?.EventBus.Emit(new PlayerCountrySelectedEvent
                {
                    CountryId = selectedCountry,
                    TimeStamp = UnityEngine.Time.time
                });

                if (logProgress)
                {
                    var countryTag = gameState?.CountryQueries?.GetTag(selectedCountry) ?? selectedCountry.ToString();
                    ArchonLogger.Log($"NetworkInitializer: Starting game as {countryTag}", "starter_kit");
                }
            }
            else
            {
                ArchonLogger.LogWarning("NetworkInitializer: No country selected, showing country selection UI", "starter_kit");
                countrySelectionUI?.Show();
            }
        }

        private void HandleStateReceived(byte[] stateData, uint tick)
        {
            if (logProgress)
                ArchonLogger.Log($"NetworkInitializer: Received state sync ({stateData.Length} bytes)", "starter_kit");

            // TODO: Apply state to simulation
        }

        private void HandleDesyncDetected()
        {
            if (logProgress)
                ArchonLogger.LogWarning("NetworkInitializer: Desync detected - resyncing...", "starter_kit");

            // Could show UI notification here
        }

        private void HandleResyncComplete()
        {
            if (logProgress)
                ArchonLogger.Log("NetworkInitializer: Resync complete", "starter_kit");
        }

        #endregion

        #region Helpers

        private void UpdateLobbyDisplay()
        {
            if (networkManager == null) return;

            var players = networkManager.GetLobbyPlayers();
            lobbyUI?.UpdatePlayerList(players);
        }

        #endregion
    }
}
