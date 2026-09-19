using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Owns "save data <-> on-disk string" and nothing else. Split out of JsonSaveService so that
    // class keeps only orchestration (locking, events, backup policy) and both files stay under the
    // 200-line limit.
    internal static class SaveEnvelopeCodec
    {
        // The version every key starts at, and the version a key with no registered chain keeps
        // forever. NOT the version that gets stamped — that is SaveMigrationRegistry.CurrentVersionFor,
        // which is per key. A single global "current version" stamped unconditionally is precisely the
        // bug this phase removed: one game bumping it would stamp the new number onto every other
        // game's unchanged data.
        internal const int CurrentSchemaVersion = 1;

        internal static string Serialize<T>(T data, string key, SaveMigrationRegistry registry, JsonSerializerSettings settings)
            where T : class
            => JsonConvert.SerializeObject(
                new SaveEnvelope<T> { SchemaVersion = registry.CurrentVersionFor(key), Data = data }, settings);

        // Throws on every corruption mode — including the ones Newtonsoft does not report.
        //
        // Newtonsoft only throws for MALFORMED json. Valid-but-wrong-shape input ("", "  ", "null",
        // "{}") deserializes to a null envelope, or to an envelope with null Data, with no
        // exception at all. Returning null for those would be indistinguishable from "no save yet":
        // the caller would start fresh and the next save would rotate the last good backup out.
        // So they throw and let LoadAsync try the backup instead.
        //
        // Known limitation: a structurally valid envelope whose Data belongs to a DIFFERENT type is
        // not detected — MissingMemberHandling.Ignore (the default) fills an all-default T. Closing
        // that needs a type discriminator in the envelope, a schema change that would also make a
        // namespace move break every existing save. Accepted and documented rather than fixed.
        internal static T Parse<T>(string json, string key, SaveMigrationRegistry registry, JsonSerializerSettings settings)
            where T : class
        {
            // Data binds as a raw JToken first so that the VERSION CHECK NEVER DEPENDS ON T.
            // A newer schema is most likely to have reshaped Data — binding that to today's T would
            // throw (or null out) before the version was ever inspected, and LoadAsync would then
            // "recover" the older backup straight over the newer save. Version first, always.
            var envelope = JsonConvert.DeserializeObject<SaveEnvelope<JToken>>(json, settings);
            if (envelope == null)
                throw new JsonException($"Save '{key}' is present but is empty or holds a bare null.");

            var currentVersion = registry.CurrentVersionFor(key);

            if (envelope.SchemaVersion > currentVersion)
                throw new SaveSchemaVersionException(key, envelope.SchemaVersion, currentVersion);

            if (envelope.Data == null || envelope.Data.Type == JTokenType.Null)
                throw new JsonException($"Save '{key}' is present but is not a valid SaveEnvelope<{typeof(T).Name}>.");

            var payload = RunMigrations(envelope.Data, key, envelope.SchemaVersion, currentVersion, registry, out var migrated);

            T data;
            try
            {
                data = payload.ToObject<T>(JsonSerializer.CreateDefault(settings));
            }
            catch (Exception ex) when (migrated)
            {
                // Without this, a migration that produces a shape T cannot read surfaces as a plain
                // JsonException — which LoadAsync treats as corruption and "recovers" from the OLDER
                // backup, over a save that was perfectly fine before the chain touched it.
                throw new SaveMigrationException(key, envelope.SchemaVersion, currentVersion,
                    $"the migrated payload does not bind to {typeof(T).Name}.", ex);
            }

            if (data == null)
            {
                if (migrated)
                    throw new SaveMigrationException(key, envelope.SchemaVersion, currentVersion,
                        $"the migrated payload bound to a null {typeof(T).Name}.");

                throw new JsonException($"Save '{key}' has a payload that does not bind to {typeof(T).Name}.");
            }

            return data;
        }

        // Walks the chain from the stored version up to the key's current version. Read-only: the
        // migrated token is never written back, so a chain that fails halfway leaves the file exactly
        // as it was, still at its old version, to be migrated again on the next load.
        private static JToken RunMigrations(
            JToken data, string key, int storedVersion, int currentVersion, SaveMigrationRegistry registry, out bool migrated)
        {
            migrated = false;
            var payload = data;
            var version = storedVersion;

            while (version < currentVersion && registry.TryGetStep(key, version, out var step))
            {
                // Private copy before the first step, so a migration that mutates in place cannot
                // reach into the envelope the caller still holds. One line, removes the ambiguity.
                if (!migrated)
                    payload = payload.DeepClone();

                try
                {
                    payload = step.Migrate(payload);
                }
                catch (Exception ex)
                {
                    throw new SaveMigrationException(key, storedVersion, currentVersion,
                        $"the v{version} step threw.", ex);
                }

                if (payload == null)
                    throw new SaveMigrationException(key, storedVersion, currentVersion,
                        $"the v{version} step returned null.");

                version++;
                migrated = true;
            }

            // Reachable only when the chain cannot START from the stored version — in practice a
            // version below the base, including the 0 that a save with no SchemaVersion field
            // deserializes to. Once a chain can start it always reaches currentVersion, because that
            // number is derived from the same contiguous run of steps.
            //
            // Warn and load as-is, which is exactly what this did before any migration engine existed.
            // Throwing here would turn every pre-versioning save into a hard failure the moment its
            // game registered its first migration.
            if (version < currentVersion)
                Debug.LogWarning($"[SaveEnvelopeCodec] Save '{key}' is schema v{storedVersion}, current is v{currentVersion}. " +
                                 $"No migration step is registered for v{version}, so it is loading as-is.");

            return payload;
        }
    }
}
