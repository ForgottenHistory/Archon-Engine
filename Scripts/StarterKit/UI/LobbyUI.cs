using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.UI;
using Map.Interaction;
using Archon.Network;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Lobby UI for multiplayer mode selection.
    /// Shows connected players and allows host to start the game.
    /// Players can click the map to select their country.
    /// </summary>
    public class LobbyUI : StarterKitPanel
    {
        [Header("Network Settings")]
        [SerializeField] private int defaultPort = 7777;
        [SerializeField] private string defaultAddress = "127.0.0.1";

        [Header("Country Selection")]
        [SerializeField] private Color countryHighlightColor = new Color(1f, 0.84f, 0f, 0.5f);

        [Header("Debug")]
        [SerializeField] private bool logProgress = true;

        // UI Elements
        private VisualElement contentBox;
        private Label titleLabel;
        private Label statusLabel;
        private Label selectionHintLabel;
        private TextField addressField;
        private TextField portField;
        private Button singlePlayerButton;
        private Button hostButton;
        private Button joinButton;
        private Button cancelButton;
        private Button startGameButton;
        private Button backButton;
        private VisualElement modeSelectionPanel;
        private VisualElement connectionPanel;
        private VisualElement lobbyPanel;
        private VisualElement playerListContainer;

        // Country selection references
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private NetworkManager networkManager;
        private ushort selectedCountryId;

        // State
        private LobbyState currentState = LobbyState.ModeSelection;
        private bool isHost;

        /// <summary>Current lobby state.</summary>
        public LobbyState CurrentState => currentState;

        /// <summary>The country ID selected by this player in the lobby.</summary>
        public ushort SelectedCountryId => selectedCountryId;

        /// <summary>Fired when single player is selected.</summary>
        public event System.Action OnSinglePlayerSelected;

        /// <summary>Fired when host is selected. Parameter is port.</summary>
        public event System.Action<int> OnHostSelected;

        /// <summary>Fired when join is selected. Parameters are address and port.</summary>
        public event System.Action<string, int> OnJoinSelected;

        /// <summary>Fired when cancel is clicked during connection.</summary>
        public event System.Action OnCancelled;

        /// <summary>Fired when host clicks start game.</summary>
        public event System.Action OnStartGameClicked;

        public void Initialize(GameState gameStateRef)
        {
            if (!base.Initialize(gameStateRef))
            {
                return;
            }

            // Find province selector for country selection
            provinceSelector = FindFirstObjectByType<ProvinceSelector>();
            provinceHighlighter = FindFirstObjectByType<ProvinceHighlighter>();

            // Disable province selection until we enter the lobby
            if (provinceSelector != null)
            {
                provinceSelector.SelectionEnabled = false;
            }

            if (logProgress)
            {
                ArchonLogger.Log("LobbyUI: Initialized", "starter_kit");
            }
        }

        /// <summary>
        /// Set the network manager reference for country selection sync.
        /// </summary>
        public void SetNetworkManager(NetworkManager manager)
        {
            networkManager = manager;
        }

        protected override void CreateUI()
        {
            // Container on the left side of screen
            panelContainer = new VisualElement();
            panelContainer.name = "lobby-container";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 20f;
            panelContainer.style.top = 100f;
            panelContainer.style.bottom = 20f;
            panelContainer.style.width = 320f;
            panelContainer.style.alignItems = Align.Stretch;
            panelContainer.style.justifyContent = Justify.FlexStart;

            // Content box
            contentBox = CreateStyledPanel("lobby-content");
            UIHelper.SetBorderRadius(contentBox, RadiusLg);
            UIHelper.SetPadding(contentBox, SpacingMd, SpacingLg);
            contentBox.style.alignItems = Align.Stretch;
            contentBox.style.flexGrow = 0;

            // Title
            titleLabel = CreateHeader("Multiplayer");
            titleLabel.style.marginBottom = SpacingLg;
            contentBox.Add(titleLabel);

            // Mode selection panel
            CreateModeSelectionPanel();

            // Connection panel (hidden by default)
            CreateConnectionPanel();

            // Lobby panel (hidden by default)
            CreateLobbyPanel();

            panelContainer.Add(contentBox);

            // Start in mode selection
            SetState(LobbyState.ModeSelection);
        }

        private void CreateModeSelectionPanel()
        {
            modeSelectionPanel = new VisualElement();
            modeSelectionPanel.name = "mode-selection-panel";
            modeSelectionPanel.style.alignItems = Align.Stretch;

            // Single Player button
            singlePlayerButton = CreateStyledButton("Single Player", OnSinglePlayerClicked);
            singlePlayerButton.style.marginBottom = SpacingMd;
            modeSelectionPanel.Add(singlePlayerButton);

            // Host button
            hostButton = CreateStyledButton("Host Game", OnHostClicked);
            hostButton.style.marginBottom = SpacingMd;
            modeSelectionPanel.Add(hostButton);

            // Join button
            joinButton = CreateStyledButton("Join Game", OnJoinClicked);
            modeSelectionPanel.Add(joinButton);

            contentBox.Add(modeSelectionPanel);
        }

        private void CreateConnectionPanel()
        {
            connectionPanel = new VisualElement();
            connectionPanel.name = "connection-panel";
            connectionPanel.style.display = DisplayStyle.None;
            connectionPanel.style.alignItems = Align.Stretch;

            // Address field (for joining)
            var addressRow = CreateRow(Justify.SpaceBetween);
            addressRow.style.marginBottom = SpacingSm;

            var addressLabel = CreateText("Address:");
            addressLabel.style.width = 70f;
            addressRow.Add(addressLabel);

            addressField = new TextField();
            addressField.value = defaultAddress;
            addressField.style.flexGrow = 1;
            addressRow.Add(addressField);

            connectionPanel.Add(addressRow);

            // Port field
            var portRow = CreateRow(Justify.SpaceBetween);
            portRow.style.marginBottom = SpacingLg;

            var portLabel = CreateText("Port:");
            portLabel.style.width = 70f;
            portRow.Add(portLabel);

            portField = new TextField();
            portField.value = defaultPort.ToString();
            portField.style.flexGrow = 1;
            portRow.Add(portField);

            connectionPanel.Add(portRow);

            // Confirm/Cancel buttons
            var buttonRow = CreateRow(Justify.Center);

            var confirmButton = CreateStyledButton("Connect", OnConfirmConnection);
            confirmButton.style.flexGrow = 1;
            confirmButton.style.marginRight = SpacingSm;
            buttonRow.Add(confirmButton);

            backButton = CreateStyledButton("Back", OnBackToModeSelection);
            backButton.style.flexGrow = 1;
            buttonRow.Add(backButton);

            connectionPanel.Add(buttonRow);

            contentBox.Add(connectionPanel);
        }

        private void CreateLobbyPanel()
        {
            lobbyPanel = new VisualElement();
            lobbyPanel.name = "lobby-panel";
            lobbyPanel.style.display = DisplayStyle.None;
            lobbyPanel.style.alignItems = Align.Stretch;

            // Status label
            statusLabel = CreateText("Waiting for players...");
            statusLabel.style.marginBottom = SpacingMd;
            statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            lobbyPanel.Add(statusLabel);

            // Player list header
            var headerLabel = CreateSecondaryText("Players:");
            headerLabel.style.marginBottom = SpacingSm;
            lobbyPanel.Add(headerLabel);

            // Player list container
            playerListContainer = new VisualElement();
            playerListContainer.name = "player-list";
            playerListContainer.style.width = new Length(100, LengthUnit.Percent);
            playerListContainer.style.minHeight = 80f;
            playerListContainer.style.maxHeight = 200f;
            playerListContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            UIHelper.SetBorderRadius(playerListContainer, RadiusMd);
            UIHelper.SetPadding(playerListContainer, SpacingSm, SpacingSm);
            playerListContainer.style.marginBottom = SpacingMd;
            lobbyPanel.Add(playerListContainer);

            // Selection hint
            selectionHintLabel = CreateSecondaryText("Click the map to select your country");
            selectionHintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            selectionHintLabel.style.marginBottom = SpacingMd;
            selectionHintLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            lobbyPanel.Add(selectionHintLabel);

            // Button row
            var buttonRow = CreateRow(Justify.Center);
            buttonRow.style.width = new Length(100, LengthUnit.Percent);

            // Start game button (host only)
            startGameButton = CreateStyledButton("Start Game", OnStartGameButtonClicked);
            startGameButton.style.flexGrow = 1;
            startGameButton.style.marginRight = SpacingSm;
            startGameButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f, 1f);
            buttonRow.Add(startGameButton);

            // Cancel/Leave button
            cancelButton = CreateStyledButton("Leave", OnCancelClicked);
            cancelButton.style.flexGrow = 1;
            buttonRow.Add(cancelButton);

            lobbyPanel.Add(buttonRow);

            contentBox.Add(lobbyPanel);
        }

        private void SetState(LobbyState state)
        {
            var previousState = currentState;
            currentState = state;

            // Unsubscribe from province clicks when leaving lobby
            if (previousState == LobbyState.InLobby && state != LobbyState.InLobby)
            {
                if (provinceSelector != null)
                {
                    provinceSelector.OnProvinceClicked -= HandleProvinceClicked;
                    provinceSelector.SelectionEnabled = false;
                }
                provinceHighlighter?.ClearHighlight();
            }

            // Hide all panels
            modeSelectionPanel.style.display = DisplayStyle.None;
            connectionPanel.style.display = DisplayStyle.None;
            lobbyPanel.style.display = DisplayStyle.None;

            switch (state)
            {
                case LobbyState.ModeSelection:
                    titleLabel.text = "Main Menu";
                    modeSelectionPanel.style.display = DisplayStyle.Flex;
                    // Disable map interaction in main menu
                    if (provinceSelector != null)
                        provinceSelector.SelectionEnabled = false;
                    break;

                case LobbyState.EnteringHostInfo:
                    titleLabel.text = "Host Game";
                    addressField.parent.style.display = DisplayStyle.None;
                    connectionPanel.style.display = DisplayStyle.Flex;
                    break;

                case LobbyState.EnteringJoinInfo:
                    titleLabel.text = "Join Game";
                    addressField.parent.style.display = DisplayStyle.Flex;
                    connectionPanel.style.display = DisplayStyle.Flex;
                    break;

                case LobbyState.InLobby:
                    titleLabel.text = isHost ? "Hosting" : "Lobby";
                    startGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
                    lobbyPanel.style.display = DisplayStyle.Flex;
                    // Enable province clicks for country selection
                    if (provinceSelector != null)
                    {
                        provinceSelector.SelectionEnabled = true;
                        provinceSelector.OnProvinceClicked += HandleProvinceClicked;
                    }
                    break;

                case LobbyState.Connecting:
                    titleLabel.text = "Connecting...";
                    statusLabel.text = "Connecting to host...";
                    lobbyPanel.style.display = DisplayStyle.Flex;
                    startGameButton.style.display = DisplayStyle.None;
                    break;
            }
        }

        private void HandleProvinceClicked(ushort provinceId)
        {
            if (currentState != LobbyState.InLobby) return;
            if (provinceId == 0) return;

            var provinceQueries = gameState?.ProvinceQueries;
            if (provinceQueries == null) return;

            ushort ownerId = provinceQueries.GetOwner(provinceId);
            if (ownerId == 0) return;

            // Highlight the selected country
            provinceHighlighter?.HighlightCountry(ownerId, countryHighlightColor);

            // Update local state
            selectedCountryId = ownerId;

            // Update hint label
            var countryQueries = gameState?.CountryQueries;
            string countryTag = countryQueries?.GetTag(ownerId) ?? $"Country {ownerId}";
            if (selectionHintLabel != null)
            {
                selectionHintLabel.text = $"Selected: {countryTag}";
                selectionHintLabel.style.color = TextGold;
            }

            // Send country selection to network
            networkManager?.SetCountrySelection(ownerId);

            if (logProgress)
            {
                ArchonLogger.Log($"LobbyUI: Selected country {countryTag} (ID: {ownerId})", "starter_kit");
            }
        }

        private void OnSinglePlayerClicked()
        {
            if (logProgress)
                ArchonLogger.Log("LobbyUI: Single Player selected", "starter_kit");

            OnSinglePlayerSelected?.Invoke();
            Hide();
        }

        private void OnHostClicked()
        {
            SetState(LobbyState.EnteringHostInfo);
        }

        private void OnJoinClicked()
        {
            SetState(LobbyState.EnteringJoinInfo);
        }

        private void OnConfirmConnection()
        {
            if (!int.TryParse(portField.value, out int port))
            {
                port = defaultPort;
            }

            if (currentState == LobbyState.EnteringHostInfo)
            {
                if (logProgress)
                    ArchonLogger.Log($"LobbyUI: Hosting on port {port}", "starter_kit");

                isHost = true;
                SetState(LobbyState.InLobby);
                OnHostSelected?.Invoke(port);
            }
            else if (currentState == LobbyState.EnteringJoinInfo)
            {
                string address = addressField.value;
                if (string.IsNullOrWhiteSpace(address))
                    address = defaultAddress;

                if (logProgress)
                    ArchonLogger.Log($"LobbyUI: Joining {address}:{port}", "starter_kit");

                isHost = false;
                SetState(LobbyState.Connecting);
                OnJoinSelected?.Invoke(address, port);
            }
        }

        private void OnBackToModeSelection()
        {
            SetState(LobbyState.ModeSelection);
        }

        private void OnCancelClicked()
        {
            if (logProgress)
            {
                ArchonLogger.Log("LobbyUI: Leave clicked", "starter_kit");
            }

            OnCancelled?.Invoke();
            SetState(LobbyState.ModeSelection);
        }

        private void OnStartGameButtonClicked()
        {
            if (logProgress)
                ArchonLogger.Log("LobbyUI: Start game clicked", "starter_kit");

            OnStartGameClicked?.Invoke();
        }

        /// <summary>
        /// Update the player list display.
        /// </summary>
        public void UpdatePlayerList(LobbyPlayerSlot[] players)
        {
            if (playerListContainer == null) return;

            playerListContainer.Clear();

            var countryQueries = gameState?.CountryQueries;

            foreach (var player in players)
            {
                var playerRow = CreateRowEntry(player.IsHost == 1);
                playerRow.style.paddingTop = 2f;
                playerRow.style.paddingBottom = 2f;

                // Host indicator or player number
                var prefixLabel = CreateText(player.IsHost == 1 ? "[Host]" : $"[P{player.PeerId}]");
                prefixLabel.style.width = 50f;
                prefixLabel.style.fontSize = FontSizeSmall;
                prefixLabel.style.color = player.IsHost == 1 ? TextGold : TextSecondary;
                playerRow.Add(prefixLabel);

                // Country selection (or "---")
                string countryName = "---";
                if (player.CountryId > 0)
                {
                    countryName = countryQueries?.GetTag(player.CountryId) ?? $"Country {player.CountryId}";
                }
                var countryLabel = CreateText(countryName);
                countryLabel.style.flexGrow = 1;
                countryLabel.style.color = player.CountryId > 0 ? TextPrimary : TextSecondary;
                playerRow.Add(countryLabel);

                playerListContainer.Add(playerRow);
            }

            // Update status
            if (statusLabel != null)
            {
                statusLabel.text = $"{players.Length} player(s) in lobby";
            }
        }

        /// <summary>
        /// Update status text.
        /// </summary>
        public void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        /// <summary>
        /// Called when connection is established (client only).
        /// </summary>
        public void OnConnectionEstablished()
        {
            SetState(LobbyState.InLobby);
        }

        /// <summary>
        /// Called when connection fails.
        /// </summary>
        public void OnConnectionFailed(string error)
        {
            if (logProgress)
                ArchonLogger.LogWarning($"LobbyUI: Connection failed - {error}", "starter_kit");

            SetStatus($"Failed: {error}");

            // Return to mode selection after a delay
            StartCoroutine(ReturnToModeSelectionAfterDelay(2f));
        }

        /// <summary>
        /// Called when game is starting.
        /// </summary>
        public void OnGameStarting()
        {
            SetStatus("Game starting...");
            if (startGameButton != null)
                startGameButton.SetEnabled(false);
        }

        private System.Collections.IEnumerator ReturnToModeSelectionAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetState(LobbyState.ModeSelection);
        }
    }

    /// <summary>
    /// Lobby UI states.
    /// </summary>
    public enum LobbyState
    {
        ModeSelection,
        EnteringHostInfo,
        EnteringJoinInfo,
        Connecting,
        InLobby
    }
}
