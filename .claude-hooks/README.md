# Vault Sync Guard

Non-blocking hook that warns when `Runtime/`/`Editor/` C# files changed but the Obsidian vault
(`UIFramework/` in `C:\Users\user\OneDrive\Documents\Obsidian Vault\`) wasn't updated in the same
pass. Backstop for the mandatory vault-sync rule in `unity-development-pipeline.md` Phase 3 — that
rule is prose-only and has already missed a full feature once.

## Install

Only needed in a repo where framework *source* actually changes — this repo itself, or a project
temporarily vendoring a local fork. Consuming projects that only reference the framework via a git
UPM dependency never edit `Runtime/`/`Editor/` directly, so they don't need it.

Add to `.claude/settings.json`:

```json
{
  "hooks": {
    "Stop": [
      {
        "hooks": [
          { "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR\"/.claude-hooks/vault-sync-guard.cjs" }
        ]
      }
    ]
  }
}
```

Always exits 0 — it flags in stderr, it never blocks the turn.
