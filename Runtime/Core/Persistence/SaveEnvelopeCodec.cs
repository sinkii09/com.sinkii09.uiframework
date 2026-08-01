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
        internal const int CurrentSchemaVersion = 1;

        internal static string Serialize<T>(T data, JsonSerializerSettings settings) where T : class
            => JsonConvert.SerializeObject(
                new SaveEnvelope<T> { SchemaVersion = CurrentSchemaVersion, Data = data }, settings);

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
        internal static T Parse<T>(string json, string key, JsonSerializerSettings settings) where T : class
        {
            // Data binds as a raw JToken first so that the VERSION CHECK NEVER DEPENDS ON T.
            // A newer schema is most likely to have reshaped Data — binding that to today's T would
            // throw (or null out) before the version was ever inspected, and LoadAsync would then
            // "recover" the older backup straight over the newer save. Version first, always.
            var envelope = JsonConvert.DeserializeObject<SaveEnvelope<JToken>>(json, settings);
            if (envelope == null)
                throw new JsonException($"Save '{key}' is present but is empty or holds a bare null.");

            if (envelope.SchemaVersion > CurrentSchemaVersion)
                throw new SaveSchemaVersionException(key, envelope.SchemaVersion, CurrentSchemaVersion);

            if (envelope.Data == null || envelope.Data.Type == JTokenType.Null)
                throw new JsonException($"Save '{key}' is present but is not a valid SaveEnvelope<{typeof(T).Name}>.");

            if (envelope.SchemaVersion < CurrentSchemaVersion)
                Debug.LogWarning($"[SaveEnvelopeCodec] Save '{key}' is schema v{envelope.SchemaVersion}, current is v{CurrentSchemaVersion}. Loading as-is — there is no migration engine.");

            var data = envelope.Data.ToObject<T>(JsonSerializer.CreateDefault(settings));
            if (data == null)
                throw new JsonException($"Save '{key}' has a payload that does not bind to {typeof(T).Name}.");

            return data;
        }
    }
}
