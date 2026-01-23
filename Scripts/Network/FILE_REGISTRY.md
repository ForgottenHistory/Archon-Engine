# Network Layer File Registry

## Overview
Transport-agnostic multiplayer networking. Sits alongside Core, imports Core for commands/state.

**Namespace:** `Archon.Network`
**Import Rules:** Network→Core (uses commands, state), Core knows nothing of Network

## Files

### Interfaces
| File | Purpose | Tags |
|------|---------|------|
| `INetworkTransport.cs` | Transport abstraction (Unity Transport, Steam, etc.) | `[INTERFACE]` |

### Core Types
| File | Purpose | Tags |
|------|---------|------|
| `NetworkManager.cs` | High-level coordinator, peer management, message routing | `[COORDINATOR]` |
| `NetworkMessages.cs` | Message types, headers, protocol structs | `[PROTOCOL]` |
| `NetworkPeer.cs` | Per-connection state tracking | `[DATA]` |
| `NetworkBridge.cs` | Implements Core's `INetworkBridge`, bridges commands to transport | `[BRIDGE]` |

### Transport Implementations
| File | Purpose | Tags |
|------|---------|------|
| `DirectTransport.cs` | Unity Transport Package impl (dev/LAN) | `[TRANSPORT]` |
| `SteamTransport.cs` | Steamworks.NET impl (release) | `[TRANSPORT]` `[TODO]` `[PHASE3]` |

### Synchronization
| File | Purpose | Tags |
|------|---------|------|
| `LateJoinHandler.cs` | State sync for joining players | `[SYNC]` |
| `DesyncDetector.cs` | Periodic checksum verification | `[SYNC]` |
| `DesyncRecovery.cs` | Automatic state resync (key differentiator) | `[SYNC]` |
| `DesyncDebugger.cs` | Debug tools for finding causes | `[TODO]` `[DEBUG]` |

## Quick Reference

**Need to add transport backend?** → Implement `INetworkTransport`
**Need to add message type?** → Add to `NetworkMessageType` enum in `NetworkMessages.cs`
**Need peer state?** → Check `NetworkPeer.cs`
**Need to send/receive?** → Use `NetworkManager` methods

## Implementation Phases

1. **Phase 1 (Current):** Core types, DirectTransport
2. **Phase 2:** Game integration, late join, speed sync
3. **Phase 2.5:** Desync detection & recovery
4. **Phase 3:** Steam integration
5. **Phase 4:** Polish (reconnect, host migration)
