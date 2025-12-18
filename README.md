# PurrNet - Unity3D 

![9ed350a5-2701-4163-b19c-89618669479c](https://github.com/user-attachments/assets/25cdde72-47d3-4510-ba82-7348b8dba792)

PurrNet is our attempt at the purrfect networking solution... It's a 100% free Unity Networking solution with no pro or premium version, and no features locked behind a pay-gate.
You can use it to release, and we ask nothing in return! Read the Unique to PurrNet section to see what we offer above other solutions!

Website: https://purrnet.dev/
Docs: https://purrnet.gitbook.io/docs  
Docs (source): https://github.com/BlenMiner/PurrNet-Docs

## Install

You can install PurrNet through Unity's Package Manager by adding a package through this URL:

```bash
https://github.com/PurrNet/PurrNet.git?path=/Assets/PurrNet#release
```

Or through OpenUM [![openupm](https://img.shields.io/npm/v/dev.purrnet.purrnet?label=PurrNet&registry_uri=https://package.openupm.com)](https://openupm.com/packages/dev.purrnet.purrnet/)

`openupm add dev.purrnet.purrnet`

You can also fallback to the asset store but versions will be behind.

[*Asset store link.*](https://assetstore.unity.com/packages/tools/network/purrnet-297320)

## Discord

<a href="https://discord.gg/HnNKdkq9ta" target="_blank">
    <img src="https://discord.com/api/guilds/1288872904272121957/widget.png?style=banner2" alt="Discord Banner">
</a>

## Quick Introduction

### Transports

- UDP (reliable and unreliable)
- WebSockets
- Steam
- Local (no socket)
- PurrTransport (Relay free for development)
- Composite (Allows multiple transports)

### Spawning and Despawning
 
```csharp
[SerializedField] GameObject playerPrefab;

private GameObject _player;

void SpawnPlayer()
{
    _player = Instantiate(playerPrefab);
}

void DespawnPlayer()
{
    Destroy(_player);
}

```

Yes, you are done! PurrNet will handle the rest for you.
The best part is that if you want to allow flexibility over security, you can even have clients spawn and despawn their own objects depending on which NetworkRules you pick.
With no changes to this code.

Bonus, you can also drag and drop prefabs into the scene and have them spawn automatically.
As long as they have a NetworkIdentity component attached to them and are part of your NetworkPrefabs list.

### RPCs

You have `TargetRPC`s, `ServerRPC`s, and `ObserverRPC`s.
Depending on your network rules, these can all be called by clients too.
Or if you want to keep it secure but still allow clients to call some of them, you can use the `requireServer: false` parameter.

```csharp
[ServerRPC]
void DoSomethingOnServer()
{
    Debug.Log("Doing something on the server!");
}
```

Static RPCs are also supported.

```csharp
[ServerRPC]
static void DoSomethingOnServer()
{
    Debug.Log("Doing something on the server!");
}
```

Awaitable RPCs are also supported.

```csharp
[ServerRPC]
static Task<int> GetMyNymber()
{
    return Task.FromResult(42);
}
```

UnitTask integration is also supported.

```csharp
[ServerRPC]
static UniTask<int> GetMyNymber()
{
    return UniTask.FromResult(42);
}
```

Why not Coroutine RPCs? We have that too!

```csharp
[ServerRPC]
static IEnumerator DoSomethingOnServer()
{
    yield return new WaitForSeconds(1);
    Debug.Log("Doing something on the server!");
}
```

Generic RPCs are also supported.

```csharp
[ServerRPC]
static void DoSomethingOnServer<T>(T value)
{
    Debug.Log($"Doing something on the server with {value}!");
}
```

All of these can be combined. For example, you can have a static RPC that returns a value and is awaitable and generic.

### Network Modules

Network Modules are a way to extend PurrNet with your own custom logic.
SyncVars are built using Network Modules, and you can create your own Network Modules to add custom logic to your networked objects.
This opens up a whole new world of possibilities for modularity and extensibility.

You can also nest these modules inside each other.
So for this next example we could have used a `SyncVar<int>` (another `NetworkModule`) but for demonstration purposes we won't.

```csharp
[Serializable]
public class PlayerHealthModule : NetworkModule
{
    [SerializeField] int _health;
    
    [ServerRPC(requireOwnership: true)]
    public void TakeDamage(int damage)
    {
        _health -= damage;
    }
}
```

The example above shows a simple health module that can be attached to any networked object.
Note that any of the mentioned RPCs can be used in Network Modules.

Here is how you would use it:

```csharp
class SomeIdentity : NetworkIdentity
{
    [SerializeField] PlayerHealthModule _healthModule;
    
    void TakeDamage(int damage)
    {
        _healthModule.TakeDamage(damage);
    }
}
```

This is just a simple example, but you can create much more complex modules with multiple RPCs and SyncVars.
All our built-in features are implemented using Network Modules, so you can be sure that they are powerful and flexible.

Don't forget they can also be generic!

### Network Rules

Network Rules are a way to define how your networked objects behave.
You can define who can spawn, despawn, and call RPCs on your objects.
You can also define who can observe your objects and how they are synchronized.
Almost everything is customizable, and every object can have its own set of rules.

![image](https://github.com/user-attachments/assets/aa702bc4-ad6b-4cd4-841b-700d21f28d3e)

### Serialization

PurrNet uses a custom serialization system that is both fast and flexible.
I will keep this short as you shouldn't have to worry about it.

Just want to mention some of the features:

```csharp
// sending an RPC with an object
// PurrNet will automatically serialize it for you and resolve it's type
[ServerRPC]
void DoSomethingOnServer(object someValue)
{
    Debug.Log($"Doing something on the server with {someValue}!");
}
```

You can also use the BitPacker directly if you want to send custom data.
This avoids creating garbage and is much faster than using the object serialization.
It also allows you to send data that might not be able to be represented by a type.

```csharp
void SendSometing()
{
    using var writer = BitPackerPool.Get();
    
    writer.Write(42);
    writer.Write("Hello, World!");
    
    DoSomethingOnServer(writer);
}

[ServerRPC]
void DoSomethingOnServer(BitPacker data)
{
    int value = default;
    string message = default;
    
    Packer<int>.Read(data, ref value);
    Packer<string>.Read(data, ref message);
    
    Debug.Log($"Doing something on the server with {value} and '{message}'!");
    
    data.Dispose();
}
```
 
