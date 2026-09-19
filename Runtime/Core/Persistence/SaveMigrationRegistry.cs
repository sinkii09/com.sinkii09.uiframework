using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Per-save-key migration chains, plus the current schema version each key derives from them.
    //
    // WHY PER-KEY, AND WHY THE VERSION IS DERIVED RATHER THAN DECLARED
    //
    // The version used to be one global const that Serialize stamped unconditionally. With per-key
    // chains that is a data-loss bug waiting on a second game: game A bumps to v2, game B's next save
    // is stamped v2 over v1-shaped data, and if B ever registers a chain it sees v2 and skips
    // migrating data that is genuinely v1. Silent, permanent, and it only detonates long after
    // everything shipped green. Version granularity must match chain granularity.
    //
    // So CurrentVersionFor walks the chain instead of reading a number someone maintains by hand. An
    // explicit "current version" argument could drift from the steps that are supposed to produce it
    // — two sources of truth for one fact, which is the same class of bug as the global const. Derived
    // makes that drift unrepresentable.
    //
    // A key with no chain stays at SaveEnvelopeCodec.CurrentSchemaVersion forever. Bumping is a
    // deliberate act by the one game that registers steps, never a side effect of another game's bump.
    //
    // OVERRIDING THE FRAMEWORK'S EMPTY REGISTRY: register your own in the scope that calls
    // base.Configure(builder) — VContainer resolves duplicate registrations last-wins. Do it in
    // Configure, never from an IInitializable: an IInitializable runs after the boot load, which would
    // see version 1 against a v2 file, throw SaveSchemaVersionException, and leave a consumer like
    // LevelProgressService with saving disabled for the entire session.
    public sealed class SaveMigrationRegistry
    {
        // Shared no-migrations instance. Frozen, therefore immutable, therefore safe to share — the
        // usual objection to a static registry (chains leaking across LifetimeScopes and across
        // Editor test runs in one domain) cannot apply to something nothing can add to.
        public static readonly SaveMigrationRegistry Empty = CreateFrozenEmpty();

        private readonly Dictionary<string, Dictionary<int, ISaveMigration>> _chains = new();
        private bool _frozen;

        private static SaveMigrationRegistry CreateFrozenEmpty()
        {
            var registry = new SaveMigrationRegistry();
            registry._frozen = true;
            return registry;
        }

        /// <summary>
        /// Adds one step to <paramref name="key"/>'s chain. Call during container configuration only.
        /// </summary>
        public void Register(string key, ISaveMigration migration)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Save key must not be null or empty.", nameof(key));
            if (migration == null)
                throw new ArgumentNullException(nameof(migration));

            // Not SaveMigrationException: that type is a load-time marker meaning "do not touch the
            // backup". These are programmer errors at configuration time, and conflating them would
            // let a registration mistake masquerade as a data problem.
            if (migration.FromVersion < 1)
                throw new ArgumentException(
                    $"Migration for '{key}' has FromVersion {migration.FromVersion}; versions start at 1.",
                    nameof(migration));

            if (_frozen)
                throw new InvalidOperationException(
                    $"Cannot register a migration for '{key}': this registry has already been read. " +
                    "Register every migration during container configuration, before the first load.");

            if (!_chains.TryGetValue(key, out var chain))
                _chains[key] = chain = new Dictionary<int, ISaveMigration>();

            // Throwing rather than last-wins on purpose. Two steps for the same version means the
            // winner is arbitrary, and the mistake would be invisible afterwards: the derived current
            // version is identical either way, so the only evidence would be wrong data much later.
            if (chain.ContainsKey(migration.FromVersion))
                throw new ArgumentException(
                    $"'{key}' already has a migration from v{migration.FromVersion}. " +
                    "Each version may have exactly one step.", nameof(migration));

            chain[migration.FromVersion] = migration;
        }

        // The version a save for this key is stamped with, and the version a chain migrates up to:
        // one past the end of the CONTIGUOUS run of steps starting at the base version.
        //
        // A gap (steps from v1 and v3, none from v2) stops the walk at v2, so a v3 file on disk reads
        // as newer-than-supported and throws SaveSchemaVersionException. That is the intended
        // outcome: loud, and it refuses to run a chain it cannot complete.
        internal int CurrentVersionFor(string key)
        {
            Freeze();

            var version = SaveEnvelopeCodec.CurrentSchemaVersion;
            if (!_chains.TryGetValue(key, out var chain))
                return version;

            while (chain.ContainsKey(version))
                version++;

            return version;
        }

        internal bool TryGetStep(string key, int fromVersion, out ISaveMigration migration)
        {
            Freeze();

            migration = null;
            return _chains.TryGetValue(key, out var chain) && chain.TryGetValue(fromVersion, out migration);
        }

        // Closes the registry to further registration on first read, which is also the earliest point
        // at which the chains are complete enough to validate.
        //
        // This enforces the "configure it at container-build time" rule without needing a separate
        // "build finished" signal. It is NOT a memory barrier and does not by itself make concurrent
        // access safe: _frozen is a plain bool. What makes reads safe is that configuration, container
        // build and the first load are strictly ordered in practice, and a Dictionary nobody writes to
        // any more is safe to read from several threads — which matters because LoadAsync holds no
        // lock and may resume off the main thread.
        private void Freeze()
        {
            if (_frozen)
                return;

            _frozen = true;
            ReportUnreachableSteps();
        }

        // A step is unreachable when the contiguous walk stops before it — most easily by registering
        // a chain that does not START at the base version. Register only a v2 step, the natural move
        // once every player is thought to be past v1, and the derived current version stays at 1: the
        // step never runs, and every v2 file on disk instead reads as "newer than this build supports"
        // and gets blamed on a newer release. Silent, and the symptom points at the wrong thing.
        //
        // Reported rather than thrown: this is a configuration mistake, and turning it into an
        // exception here would convert it into a crash on the load path at boot.
        private void ReportUnreachableSteps()
        {
            foreach (var entry in _chains)
            {
                var reachable = SaveEnvelopeCodec.CurrentSchemaVersion;
                while (entry.Value.ContainsKey(reachable))
                    reachable++;

                foreach (var fromVersion in entry.Value.Keys)
                {
                    if (fromVersion < reachable)
                        continue;

                    Debug.LogError(
                        $"[SaveMigrationRegistry] The migration from v{fromVersion} for save key '{entry.Key}' will " +
                        $"never run: the chain is only contiguous up to v{reachable}, so that is the current version " +
                        $"for this key. Saves at v{fromVersion} or later will be rejected as newer than this build " +
                        $"supports. Chains must start at v{SaveEnvelopeCodec.CurrentSchemaVersion} and leave no gaps.");
                }
            }
        }
    }
}
