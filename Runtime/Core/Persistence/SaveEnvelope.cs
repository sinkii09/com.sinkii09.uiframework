namespace Sinkii09.UIFramework
{
    // On-disk wrapper around consumer save data. SchemaVersion is written AND enforced on load
    // (see SaveEnvelopeCodec.Parse): a newer version throws SaveSchemaVersionException, an older one
    // logs a warning and loads as-is. There is still no migration engine — only the check, so a
    // format change fails loudly instead of silently reading as "no save yet".
    public sealed class SaveEnvelope<T>
    {
        public int SchemaVersion { get; set; }
        public T Data { get; set; }
    }
}
