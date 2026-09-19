using Newtonsoft.Json.Linq;

namespace Sinkii09.UIFramework
{
    // One step of a save migration chain: turns a payload at FromVersion into a payload at
    // FromVersion + 1. Steps are registered per save key on SaveMigrationRegistry.
    //
    // Works on JToken, not on T, for two reasons. The shape being migrated is by definition NOT
    // today's T — that is the whole point — so binding it first would throw before the migration
    // could run. And staying on JToken keeps migrations from calling back into SaveEnvelopeCodec,
    // which would otherwise be a cycle.
    //
    // Migrations run READ-ONLY at load time. Nothing is written back mid-chain, so a chain that
    // fails halfway cannot corrupt the file on disk; it stays at its old version until the next
    // ordinary save. Implementations should treat the incoming token as immutable and return a new
    // one — the codec hands each step a private copy, so mutating it corrupts nothing, but returning
    // a fresh token keeps the intent obvious.
    public interface ISaveMigration
    {
        // The version this step reads. Must be >= 1. It produces FromVersion + 1.
        int FromVersion { get; }

        // Never return null — that is reported as a failed migration, not as "no data".
        JToken Migrate(JToken data);
    }
}
