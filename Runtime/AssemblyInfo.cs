using System.Runtime.CompilerServices;

// SaveEnvelopeCodec and its CurrentSchemaVersion const are internal implementation details, but the
// schema-version tests must assert against the real constant rather than a hardcoded literal —
// otherwise they silently stop testing the boundary the day the version is bumped.
[assembly: InternalsVisibleTo("Sinkii09.UIFramework.Tests")]
