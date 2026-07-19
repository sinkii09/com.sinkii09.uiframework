#!/usr/bin/env node
// Non-blocking flag: warns when Sinkii09 UI Framework source changed in the working
// tree but no note in the Obsidian vault was touched since. Backstop for the mandatory
// "update the vault in the same pass" rule in unity-development-pipeline.md Phase 3 —
// that rule is prose-only and already missed one full feature once, hence this hook.
// Intended as a Stop hook. Always exits 0 — it flags, it never blocks.

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const VAULT_DIR = 'C:\\Users\\user\\OneDrive\\Documents\\Obsidian Vault\\UIFramework';
const SOURCE_PREFIXES = ['Runtime/', 'Editor/'];

function changedCsFiles(repoRoot) {
  try {
    const out = execSync('git diff --name-only HEAD', { cwd: repoRoot, encoding: 'utf-8' });
    return out
      .split('\n')
      .map((l) => l.trim())
      .filter((f) => f.endsWith('.cs') && SOURCE_PREFIXES.some((p) => f.startsWith(p)));
  } catch {
    return [];
  }
}

function main() {
  const repoRoot = process.cwd();
  const changed = changedCsFiles(repoRoot);
  if (changed.length === 0) process.exit(0);
  if (!fs.existsSync(VAULT_DIR)) process.exit(0); // vault not reachable from this machine

  const oldestChangeMs = Math.min(
    ...changed.map((f) => {
      try {
        return fs.statSync(path.join(repoRoot, f)).mtimeMs;
      } catch {
        return Date.now();
      }
    })
  );

  const vaultTouchedSince = fs
    .readdirSync(VAULT_DIR)
    .filter((f) => f.endsWith('.md'))
    .some((f) => {
      try {
        return fs.statSync(path.join(VAULT_DIR, f)).mtimeMs >= oldestChangeMs;
      } catch {
        return false;
      }
    });

  if (!vaultTouchedSince) {
    const preview = changed.slice(0, 5).join(', ') + (changed.length > 5 ? ', ...' : '');
    console.error(
      `[vault-sync-guard] ${changed.length} framework source file(s) changed (${preview}) but no ` +
        `note under "${VAULT_DIR}" was touched since. Update the matching vault note(s) before this ` +
        `task counts as done.`
    );
  }
  process.exit(0);
}

main();
