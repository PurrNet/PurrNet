# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PurrNet is a free, open-source Unity networking framework (multiplayer solution). It's a Unity package (`dev.purrnet.purrnet`) located under `Assets/PurrNet/`. The project targets Unity 6000.3.2+ with C# 9.0, .NET Standard 2.1, and unsafe code enabled.

## Build & Test

This is a Unity project — there is no standalone CLI build. Open `PurrNet.sln` in an IDE or open the project in Unity Editor.

**Running tests:** Tests use NUnit via Unity Test Runner. Test files are in `Assets/Tests/BitPacker.Tests/`. The test assembly (`BitPacker.Tests.asmdef`) requires `UNITY_INCLUDE_TESTS` define constraint. Run tests through Unity's Test Runner window (Window > General > Test Runner) or via Unity CLI:
```
Unity.exe -runTests -testPlatform EditMode -projectPath . -testResults results.xml
```

**CI/CD:** GitHub Actions with semantic-release on `dev` (beta) and `release` (stable) branches. Commits to these branches trigger automatic versioning, changelog generation, and `.unitypackage` creation.

## Commit Conventions

Commits follow [Conventional Commits](https://www.conventionalcommits.org/) for semantic-release. Use prefixes like `fix:`, `feat:`, `perf:`, `refactor:`, etc. Automated release commits use `ci(release):`. Recent development commits are more informal on `dev` — but prefixed commits are what trigger version bumps.

## Architecture

### Assembly Structure

| Assembly | Path | Purpose |
|---|---|---|
| `PurrNet.Runtime` | `Assets/PurrNet/Runtime/` | Core networking runtime |
| `PurrNet.Editor` | `Assets/PurrNet/Editor/` | Custom inspectors, editor tools |
| `PurrNet.Codegen` | `Assets/PurrNet/Codegen/` | IL post-processing (Mono.Cecil) |
| `BitPacker.Tests` | `Assets/Tests/BitPacker.Tests/` | NUnit tests |
| Addon assemblies | `Assets/PurrNet/Addons/{Steam,UTP,Edgegap}/` | Optional integrations |

### Core Systems

**NetworkManager** (`Runtime/Managers/NetworkManager.cs`): Singleton entry point (`NetworkManager.main`). Runs at `DefaultExecutionOrder(-999)`. Manages transport, prefabs, rules, visibility, authentication, tick rate, and connection lifecycle.

**IL Post-Processing** (`Codegen/PostProcessor.cs`): Compiles serialization methods, validates RPC signatures, and generates delta packers at build time using Mono.Cecil. Skips assemblies starting with `Unity.`, `UnityEngine.`, or containing `Editor`/`NuGetForUnity`.

**BitPacker** (`Runtime/BitPacker/`): Bit-level serialization system. Key types:
- `BitPacker` / `BitData` — read/write bit streams
- `Packer<T>` — generic type serialization
- `DeltaPacker<T>` / `NativeDeltaPacker` — delta compression for state sync
- `BitPackerPool` — pooled allocation to avoid GC
- `PurrEquality` / `PurrCopy` — generated equality and copy operations

**RPC System** (`Runtime/CoreModules/RPCs/`): Three RPC types via attributes:
- `[ServerRPC]` — client-to-server calls
- `[ObserverRPC]` — broadcast to observers
- `[TargetRPC]` — send to specific client

RPCs support: static methods, async (Task/UniTask), coroutines, generics, ownership requirements (`requireOwnership`), and client-callable (`requireServer: false`). IL post-processing rewrites RPC methods at compile time.

**NetworkModule** (`Runtime/Components/NetworkModule/`): Composable networking unit. SyncVars, RPCs, and custom logic can live in modules attached to NetworkIdentity. Modules can nest inside each other.

**Transport Layer** (`Runtime/Transports/`): Abstracted via `ITransport` / `GenericTransport`. Implementations: UDP (via Ruffles/LiteNetLib), WebSockets (SimpleWebTransport), Steam, Local, PurrTransport (relay), Composite (multiplexing), UTP.

**Hierarchy & Spawning** (`Runtime/CoreModules/HierarchyV2/`): Object lifecycle management with pool support. Spawn/despawn uses standard `Instantiate`/`Destroy` — PurrNet intercepts these calls.

**Network Rules**: Per-object configurable rules for spawn/despawn permissions, RPC access, and visibility/observation.

### Conditional Compilation Defines

Defined automatically via `versionDefines` in the runtime `.asmdef`:
- `UNITASK_PURRNET_SUPPORT` — when UniTask package is present
- `UNITY_PHYSICS_3D` / `UNITY_PHYSICS_2D` — physics module support
- `UNITY_ANIMATION` — animator sync support
- `UNITY_WEB` — web request support
- `EDGEGAP_PURRNET_SUPPORT` — Edgegap hosting integration
- `UNITY_MONO_CECIL` — guards IL post-processing code

### Dependencies

**Required** (via `package.json`):
- `com.unity.nuget.mono-cecil` 1.11.4 — IL manipulation for codegen
- `com.unity.nuget.newtonsoft-json` 3.2.1 — JSON serialization
- `com.unity.collections` 2.6.2 — native collections

**Precompiled DLLs** (in `Externals/`):
- K4os.Compression.LZ4, Ruffles, System.Collections.Immutable, System.Runtime.CompilerServices.Unsafe

### Key Namespaces

- `PurrNet` — root (NetworkManager, NetworkIdentity, NetworkBehaviour)
- `PurrNet.Packing` — BitPacker serialization
- `PurrNet.Modules` — NetworkModule system
- `PurrNet.Transports` — transport abstractions
- `PurrNet.Codegen` — IL post-processing
- `PurrNet.Authentication` — auth layer
- `PurrNet.Pooling` — object/buffer pooling

### Code Style

- PascalCase for public members, `_camelCase` for private fields
- XML doc comments on public APIs
- Custom attributes: `[PurrDocs("link")]` for doc links, `[PurrLock]` for inspector locking
- Uses TriInspector for custom inspector attributes
- `[UsedImplicitly]` from JetBrains annotations marks reflection/IL-accessed members
