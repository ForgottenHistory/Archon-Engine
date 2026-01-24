using UnityEngine;
using UnityEngine.UIElements;
using Core;
using Core.UI;
using Archon.Network;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT - Lobby UI for multiplayer mode selection.
    /// Shows connected players and allows host to start the game.
    /// </summary>
    public class LobbyUI : StarterKitPanel
    {
        [Header("Network Settings")]
        [SerializeField] private int defaultPort = 7777;
        [SerializeField] private string defaultAddress = "127.0.0.1";

        [Header("Debug")]
        [SerializeField] private bool logProgress = true;

        // UI Elements
        private VisualElement contentBox;
        private Label titleLabel;
        private Label statusLabel;
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

        // State
        private LobbyState currentState = LobbyState.ModeSelection;
        private bool isHost;

        /// <summary>Current lobby state.</summary>
        public LobbyState CurrentState => currentState;

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

            if (logProgress)
            {
                ArchonLogger.Log("LobbyUI: Initialized", "starter_kit");
            }
        }

        protected override void CreateUI()
        {
            // Container at center of screen
            panelContainer = new VisualElement();
            panelContainer.name = "lobby-container";
            panelContainer.style.position = Position.Absolute;
            panelContainer.style.left = 0;
            panelContainer.style.right = 0;
            panelContainer.style.top = 0;
            panelContainer.style.bottom = 0;
            panelContainer.style.alignItems = Align.Center;
            panelContainer.style.justifyContent = Justify.Center;

            // Content box
            contentBox = CreateStyledPanel("lobby-content");
            UIHelper.SetBorderRadius(contentBox, RadiusLg);
            UIHelper.SetPadding(contentBox, SpacingLg, 40f);
            contentBox.style.alignItems = Align.Center;
            contentBox.style.minWidth = 400f;

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
            modeSelectionPanel.style.alignItems = Align.Center;

            // Single Player button
            singlePlayerButton = CreateStyledButton("Single Player", OnSinglePlayerClicked);
            singlePlayerButton.style.width = 200f;
            singlePlayerButton.style.marginBottom = SpacingMd;
            modeSelectionPanel.Add(singlePlayerButton);

            // Host button
            hostButton = CreateStyledButton("Host Game", OnHostClicked);
            hostButton.style.width = 200f;
            hostButton.style.marginBottom = SpacingMd;
            modeSelectionPanel.Add(hostButton);

            // Join button
            joinButton = CreateStyledButton("Join Game", OnJoinClicked);
            joinButton.style.width = 200f;
            modeSelectionPanel.Add(joinButton);

            contentBox.Add(modeSelectionPanel);
        }

        private void CreateConnectionPanel()
        {
            connectionPanel = new VisualElement();
            connectionPanel.name = "connection-panel";
            connectionPanel.style.display = DisplayStyle.None;
            connectionPanel.style.alignItems = Align.Center;

            // Address field (for joining)
            var addressRow = CreateRow(Justify.SpaceBetween);
            addressRow.style.width = 280f;
            addressRow.style.marginBottom = SpacingSm;

            var addressLabel = CreateText("Address:");
            addressLabel.style.width = 80f;
            addressRow.Add(addressLabel);

            addressField = new TextField();
            addressField.value = defaultAddress;
            addressField.style.flexGrow = 1;
            addressRow.Add(addressField);

            connectionPanel.Add(addressRow);

            // Port field
            var portRow = CreateRow(Justify.SpaceBetween);
            portRow.style.width = 280f;
            portRow.style.marginBottom = SpacingLg;

            var portLabel = CreateText("Port:");
            portLabel.style.width = 80f;
            portRow.Add(portLabel);

            portField = new TextField();
            portField.value = defaultPort.ToString();
            portField.style.flexGrow = 1;
            portRow.Add(portField);

            connectionPanel.Add(portRow);

            // Confirm/Cancel buttons
            var buttonRow = CreateRow(Justify.Center);
            buttonRow.style.width = 280f;

            var confirmButton = CreateStyledButton("Connect", OnConfirmConnection);
            confirmButton.style.width = 100f;
            confirmButton.style.marginRight = SpacingMd;
            buttonRow.Add(confirmButton);

            backButton = CreateStyledButton("Back", OnBackToModeSelection);
            backButton.style.width = 100f;
            buttonRow.Add(backButton);

            connectionPanel.Add(buttonRow);

            contentBox.Add(connectionPanel);
        }

        private void CreateLobbyPanel()
        {
            lobbyPanel = new VisualElement();
            lobbyPanel.name = "lobby-panel";
            lobbyPanel.style.display = DisplayStyle.None;
            lobbyPanel.style.alignItems = Align.Center;
            lobbyPanel.style.width = 350f;

            // Status label
            statusLabel = CreateText("Waiting for players...");
            statusLabel.style.marginBottom = SpacingMd;
            lobbyPanel.Add(statusLabel);

            // Player list header
            var headerLabel = CreateSecondaryText("Connected Players:");
            headerLabel.style.marginBottom = SpacingSm;
            headerLabel.style.alignSelf = Align.FlexStart;
            lobbyPanel.Add(headerLabel);

            // Player list container
            playerListContainer = new VisualElement();
            playerListContainer.name = "player-list";
            playerListContainer.style.width = new Length(100, LengthUnit.Percent);
            playerListContainer.style.minHeight = 100f;
            playerListContainer.style.maxHeight = 200f;
            playerListContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            UIHelper.SetBorderRadius(playerListContainer, RadiusMd);
            UIHelper.SetPadding(playerListContainer, SpacingSm, SpacingSm);
            playerListContainer.style.marginBottom = SpacingLg;
            lobbyPanel.Add(playerListContainer);

            // Button row
            var buttonRow = CreateRow(Justify.Center);
            buttonRow.style.width = new Length(100, LengthUnit.Percent);

            // Start game button (host only)
            startGameButton = CreateStyledButton("Start Game", OnStartGameButtonClicked);
            startGameButton.style.width = 120f;
            startGameButton.style.marginRight = SpacingMd;
            startGameButton.style.backgroundColor = new Color(0.2f, 0.4f, 0.2f, 1f);
            buttonRow.Add(startGameButton);

            // Cancel/Leave button
            cancelButton = CreateStyledButton("Leave", OnCancelClicked);
            cancelButton.style.width = 100f;
            buttonRow.Add(cancelButton);

            lobbyPanel.Add(buttonRow);

            contentBox.Add(lobbyPanel);
        }

        private void SetState(LobbyState state)
        {
            currentState = state;

            // Hide all panels
            modeSelectionPanel.style.display = DisplayStyle.None;
            connectionPanel.style.display = DisplayStyle.None;
            lobbyPanel.style.display = DisplayStyle.None;

            switch (state)
            {
                case LobbyState.ModeSelection:
                    titleLabel.text = "Multiplayer";
                    modeSelectionPanel.style.display = DisplayStyle.Flex;
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
                    titleLabel.text = isHost ? "Hosting Game" : "Game Lobby";
                    startGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
                    lobbyPanel.style.display = DisplayStyle.Flex;
                    break;

                case LobbyState.Connecting:
                    titleLabel.text = "Connecting...";
                    statusLabel.text = "Connecting to host...";
                    lobbyPanel.style.display = DisplayStyle.Flex;
                    startGameButton.style.display = DisplayStyle.None;
                    break;
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
                ArchonLogger.Log("LobbyUI: Connection cancelled", "starter_kit");

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

            foreach (var player in players)
            {
                var playerRow = CreateRowEntry(player.IsHost == 1);

                // Host indicator or player number
                var prefixLabel = CreateText(player.IsHost == 1 ? "[HOST]" : $"P{player.PeerId}");
                prefixLabel.style.width = 60f;
                prefixLabel.style.color = player.IsHost == 1 ? TextGold : TextPrimary;
                playerRow.Add(prefixLabel);

                // Country selection (or "Not Selected")
                var countryLabel = CreateText(player.CountryId > 0 ? $"Country {player.CountryId}" : "Not Selected");
                countryLabel.style.flexGrow = 1;
                countryLabel.style.color = player.CountryId > 0 ? TextPrimary : TextSecondary;
                playerRow.Add(countryLabel);

                // Ready status
                var readyLabel = CreateText(player.IsReady == 1 ? "Ready" : "");
                readyLabel.style.width = 50f;
                readyLabel.style.color = TextIncome;
                playerRow.Add(readyLabel);

                playerListContainer.Add(playerRow);
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
