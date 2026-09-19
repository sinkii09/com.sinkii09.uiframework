namespace Sinkii09.UIFramework
{
    // On-disk wrapper around consumer save data. SchemaVersion is written AND enforced on load
    // (see SaveEnvelopeCodec.Parse): newer than this build supports throws SaveSchemaVersionException;
    // older runs the key's migration chain if one is registered, and otherwise warns and loads as-is.
    //
    // The version stamped here is PER SAVE KEY — SaveMigrationRegistry.CurrentVersionFor(key), derived
    // from that key's chain. It is not a global constant, deliberately: a shared number would let one
    // game's schema bump stamp itself onto every other game's unchanged data.
    public sealed class SaveEnvelope<T>
    {
        public int SchemaVersion { get; set; }
        public T Data { get; set; }
    }
}
