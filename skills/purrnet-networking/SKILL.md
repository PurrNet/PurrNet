---
name: purrnet-networking
description: PurrNet networking framework rules — RPCs, SyncVar, identity, serialization, and channel selection. Use when writing `[ServerRpc]`, `[ObserversRpc]`, `[TargetRpc]`, using `SyncVar<T>`, choosing a `Channel`, passing `NetworkIdentity` references in RPCs, working with `NetworkBehaviour`, or debugging RPC delivery, ownership, or synchronization issues. Triggers on `ServerRpc`, `ObserversRpc`, `TargetRpc`, `SyncVar`, `NetworkIdentity`, `NetworkBehaviour`, `NetworkModule`, `Channel.`, `RPCInfo`, `PlayerID`, `requireOwnership`, `requireServer`, `runLocally`, `bufferLast`, `ownerAuth`, "RPC", "sync", "replicate".
---

# PurrNet Networking — RPCs, SyncVar & Identity

PurrNet is NOT Mirror, FishNet, Photon, or NGO. Never use syntax from those frameworks.

## 1. Three RPC types (only these exist)

| Attribute | Direction | Purpose |
|---|---|---|
| `[ServerRpc]` | Client → Server | Request server to do something |
| `[ObserversRpc]` | Server → All Clients | Broadcast state/events to observers |
| `[TargetRpc]` | Server → Specific Client | Send to one player (first param = `PlayerID`) |

**There are no other RPC types.** No `[Command]`, no `[ClientRpc]`, no `[OwnerRpc]`.

### Attribute parameters (verified from source)

```csharp
[ServerRpc(
    Channel channel = Channel.ReliableOrdered,
    bool runLocally = false,          // also run on calling client
    bool requireOwnership = true,     // only owner can call
    CompressionLevel compressionLevel = CompressionLevel.None,
    float asyncTimeoutInSec = 5f,
    bool deltaPacked = false
)]

[ObserversRpc(
    Channel channel = Channel.ReliableOrdered,
    bool runLocally = false,          // also run on server
    bool bufferLast = false,          // buffer for late joiners
    bool requireServer = true,        // only server can call
    bool excludeOwner = false,
    bool excludeSender = false,
    CompressionLevel compressionLevel = CompressionLevel.None,
    float asyncTimeoutInSec = 5f,
    bool deltaPacked = false
)]

[TargetRpc(
    Channel channel = Channel.ReliableOrdered,
    bool runLocally = false,
    bool bufferLast = false,
    bool requireServer = true,
    CompressionLevel compressionLevel = CompressionLevel.None,
    float asyncTimeoutInSec = 5f,
    bool deltaPacked = false
)]
```

### Async RPCs (request/response pattern)

PurrNet supports async RPCs with return values. For any request that needs a server response, use `async` + return type — NOT chained ServerRpc → TargetRpc callbacks:

```csharp
// Client calls, awaits server result
[ServerRpc]
private async Task<bool> TryPurchaseItem(int itemId)
{
    // Server validates and returns result
    if (!HasEnoughGold(itemId)) return false;
    DeductGold(itemId);
    return true;
}

// Usage (client-side):
bool success = await TryPurchaseItem(selectedItem);
```

Timeout configurable via `asyncTimeoutInSec` parameter (default 5s).

## 2. Channel selection

Four channels available (verified from `ITransport.cs`):

| Channel | Guarantees | Use for |
|---|---|---|
| `Channel.ReliableOrdered` | Delivery + order | Critical events, RPCs (default) |
| `Channel.ReliableUnordered` | Delivery, no order | Chat, scoreboard, non-positional data |
| `Channel.UnreliableSequenced` | Order, no delivery | High-frequency state snapshots |
| `Channel.Unreliable` | Neither | Inputs (PurrDiction handles redundancy) |

**Decision rule**: pick by consequence of loss, not by what feels safest.

## 3. SyncVar<T> — a class, not an attribute

`SyncVar<T>` extends `NetworkModule`. It is a **class you instantiate**, not an attribute you decorate fields with.

```csharp
public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private SyncVar<int> _health;

    // SyncVar supports events
    private void OnEnable()
    {
        _health.onChanged += OnHealthChanged;
    }

    // Authority: ownerAuth controls who can write
    // Server-auth (default): only server can set .value
    // Owner-auth: owner client can also set .value
}
```

**Key properties:**
- `.value` — get/set the synced value (write permission depends on `ownerAuth`)
- `.onChanged` — event when value changes
- `.onChangedWithOld` — event with both old and new values
- `.isControllingSyncVar` — whether this peer has write permission
- `.ownerAuth` — if true, owner can write; if false, only server
- `.sendIntervalInSeconds` — throttle sync rate

### Full SyncModule family (verified from source)

| Type | Purpose |
|---|---|
| `SyncVar<T>` | Single value |
| `SyncList<T>` | Synchronized list |
| `SyncDictionary<K,V>` | Synchronized dictionary |
| `SyncHashset<T>` | Synchronized hashset |
| `SyncArray<T>` | Synchronized array |
| `SyncQueue<T>` | Synchronized queue |
| `SyncEvent` | One-shot event broadcast |
| `SyncTimer` | Synchronized timer |
| `ValidatedSyncVar<T>` | SyncVar with server validation |
| `SyncLazyRef<T>` | Lazy network identity reference |

## 4. Identity hierarchy

```
MonoBehaviour
└── NetworkIdentity        ← base networked object (partial class)
    └── NetworkBehaviour   ← abstract, for components needing network context
```

- **`NetworkIdentity`**: attach to GameObjects that need networking. Manages observers, ownership, spawning, modules.
- **`NetworkBehaviour`**: abstract base for your networked scripts. Inherits `NetworkIdentity`.
- **`NetworkModule`**: base for composable modules (`SyncVar`, `SyncList`, etc.) that attach to an identity.

### Passing identities in RPCs

**Prefer direct references** — PurrNet serializes `NetworkIdentity` and its subclasses automatically:

```csharp
[ObserversRpc]
private void NotifyPickup(PickUpItem item, int slot)
{
    // PurrNet resolves the reference on the receiving end
    inventory.AddItem(item, slot);
}
```

Do NOT manually resolve IDs via `HierarchyFactory` in normal gameplay code. Reserve manual lookups for advanced cases requiring explicit control.

## 5. RPCInfo — extracting caller context

Add `RPCInfo info = default` as the last parameter to get caller metadata:

```csharp
[ServerRpc(requireOwnership: false)]
private void RequestAction(int actionId, RPCInfo info = default)
{
    // info.sender — the PlayerID who sent this RPC
    PlayerID caller = info.sender;
}
```

## 6. Common mistakes (framework confusion)

| Wrong (other frameworks) | Correct (PurrNet) |
|---|---|
| `[SyncVar] int health` | `SyncVar<int> _health` (class, not attribute) |
| `[Command]` | `[ServerRpc]` |
| `[ClientRpc]` | `[ObserversRpc]` |
| `[OwnerRpc]` | `[TargetRpc]` with PlayerID |
| `NetworkObject` | `NetworkIdentity` |
| Chained ServerRpc → TargetRpc for request/response | Single async RPC with return value |
| `NetworkServer.Spawn()` | Use `HierarchyFactory` or spawn via `NetworkManager` |

## 7. Authority model

- **Server authority** (default): Server has final say. `requireOwnership: true` on `[ServerRpc]` means only the owner client can call it.
- **Owner authority** (`ownerAuth: true` on SyncVar): Owner client can directly write the value without server approval.
- **Network Rules**: PurrNet's authority is configurable per-action. Prototype with permissive rules, tighten later without rewriting code.

## 8. Serialization

- **BitPacker**: High-performance binary serializer (auto-generated for `IPackedAuto`)
- **DeltaPacker<T>**: Differential compression (only send what changed)
- **`DeltaModule`**: Module-level delta compression — prefer this over raw `DeltaPacker<T>` for gameplay code
- LZ4 compression available via `CompressionLevel` on RPC attributes

### DeltaModule pattern (for custom state sync outside PurrDiction)

```csharp
// Key must implement IStableHashable
public struct MyDeltaKey : IStableHashable
{
    private readonly uint _id;
    public MyDeltaKey(uint id) => _id = id;
    public uint GetStableHash() => _id;
}

// Send (per target):
if (networkManager.deltaModule.Write(packet, player, key, currentState))
    SendStateToTarget(player, packet);

// Receive:
networkManager.deltaModule.Read(packet, key, sender, ref state);
```

Use `Channel.Unreliable` for frequent state; `WriteReliable`/`ReadReliable` for critical data.
