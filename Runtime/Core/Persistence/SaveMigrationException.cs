using System;

namespace Sinkii09.UIFramework
{
    // Thrown when a migration chain fails to produce a usable payload for a save key.
    //
    // Dedicated type for the same reason as SaveSchemaVersionException: LoadAsync must NOT fall back
    // to the backup for this. A buggy migration that fell through to backup recovery would restore
    // the OLDER save, the consumer would read that as "corrupt, start fresh", re-enable saving, and
    // the next write would overwrite real progress — permanent loss caused by a code bug rather than
    // by damaged data.
    //
    // Covers every way a chain can fail, not just a step that throws: a step returning null, and a
    // post-migration payload that will not bind to T. Those last two are the subtle ones — they
    // surface as a plain JsonException from the codec, which IS a backup-recovery trigger, so they
    // are re-wrapped here precisely because they would otherwise look like ordinary corruption.
    public sealed class SaveMigrationException : Exception
    {
        public string Key { get; }
        public int FromVersion { get; }
        public int TargetVersion { get; }

        public SaveMigrationException(string key, int fromVersion, int targetVersion, string reason, Exception inner = null)
            : base($"Migrating save '{key}' from schema v{fromVersion} to v{targetVersion} failed: {reason} " +
                   "The save on disk was NOT modified and the backup was NOT used — this is a code defect in the " +
                   "migration, not damaged data.", inner)
        {
            Key = key;
            FromVersion = fromVersion;
            TargetVersion = targetVersion;
        }
    }
}
