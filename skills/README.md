# Claude Code Skills for PurrNet

This folder contains [Claude Code](https://docs.anthropic.com/en/docs/claude-code) skills — specialized AI development guidance for writing correct PurrNet code.

## Usage

Copy the skills into your project's `.claude/skills/` directory:

```bash
cp -r skills/purrnet-networking /path/to/your-project/.claude/skills/
```

Once in place, the skill auto-activates when Claude Code detects relevant keywords (e.g., `ServerRpc`, `SyncVar`, `NetworkIdentity`, `Channel`).

## Available Skills

| Skill | Covers |
|---|---|
| `purrnet-networking` | RPCs (ServerRpc, ObserversRpc, TargetRpc), SyncVar, Channel selection, identity passing, async RPCs, serialization, DeltaModule |

## PurrDiction (Client-Side Prediction)

For prediction/rollback/reconciliation skills, see the [PurrDiction repository](https://github.com/PurrNet/PurrDiction).
