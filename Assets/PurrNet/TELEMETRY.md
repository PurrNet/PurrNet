# PurrNet Telemetry

PurrNet collects anonymous usage data to help us understand how the library is used and prioritize development. No personal data is collected.

## What we collect

- **Installation ID** - a random UUID generated per machine, not tied to any identity
- **Project ID** - a SHA256 hash of Unity's internal project GUID (editor only)
- **Event type** - one of: `project_start`, `connection`, `steam_session`
- **PurrNet version** - e.g. `v1.20.0`
- **Unity version** - e.g. `2022.3.20f1`
- **Operating system** - e.g. `Windows 11`
- **Player count** - number of connected players when the client joins a session
- **Transport** - which transport is in use (e.g. `UDPTransport`, `SteamTransport`)
- **Steam App ID** - only if you're using the Steam transport (this is a public app ID)

## What we do NOT collect

- IP addresses
- Player names, emails, or any personal information
- Project names or file paths
- Source code or assets
- Anything that identifies an individual

## How to opt out

In the Unity Editor: **Tools > PurrNet > Misc > Disable Telemetry**

You can also add the scripting define `PURRNET_NO_TELEMETRY` to strip telemetry at compile time, including in builds.

## Auto-disable

Telemetry is automatically skipped when:

- Running in batch mode (`-batchmode`)
- A CI environment is detected (`CI` environment variable set to `true`)

## How the data is used

We use telemetry internally to understand:

- Which PurrNet versions are in active use
- OS and Unity version distribution
- Which transports are popular
- General connection patterns
- Improve and understand the PurrNet usage

This helps us decide what to support, what to deprecate, and where to focus development effort.
