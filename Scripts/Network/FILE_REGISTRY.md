# Network Layer File Registry
**Namespace:** `Archon.Network`
**Purpose:** Transport-agnostic multiplayer networking with lockstep synchronization
**Rules:** Network→Core (uses commands, state), Core knows nothing of Network

---

## Interfaces/
- **Archon.Network.INetworkTransport** - Transport abstraction (Unity Transport, Steam, etc.); Connect, Disconnect, Send, Receive

---

## Core/
- **Archon.Network.NetworkManager** - High-level coordinator: peer management, message routing, lobby state, SetReady, SetCountrySelection, StartGame
- **Archon.Network.NetworkBridge** - Implements Core's INetworkBridge; bridges commands to transport, handles command routing (client→host→broadcast)
- **Archon.Network.NetworkMessages** - Message types, headers, protocol structs; LobbyPlayerSlot, LobbyUpdateHeader, PlayerReadyMessage, PlayerCountryMessage
- **Archon.Network.NetworkPeer** - Per-connection state tracking: peer ID, connection status, player info

---

## Transport/
- **Archon.Network.DirectTransport** - Unity Transport Package implementation (dev/LAN)

---

## Synchronization/
- **Archon.Network.NetworkTimeSync** - Game time synchronization across clients; ensures tick-aligned simulation
- **Archon.Network.LateJoinHandler** - State sync for players joining mid-game
- **Archon.Network.DesyncDetector** - Periodic checksum verification to detect state divergence
- **Archon.Network.DesyncRecovery** - Automatic state resync when desync detected

---

## Quick Reference

**Need to add transport backend?** → Implement `INetworkTransport`
**Need to add message type?** → Add to `NetworkMessageType` enum in `NetworkMessages.cs`
**Need peer state?** → Check `NetworkPeer`
**Need to send/receive?** → Use `NetworkManager` methods
**Need lobby functionality?** → Use `NetworkManager.SetReady()`, `SetCountrySelection()`, `StartGame()`
**Need command sync?** → Commands go through `NetworkBridge` (client→host→broadcast pattern)
**Need time sync?** → `NetworkTimeSync` handles tick alignment

---

*Updated: 2026-01-24*
