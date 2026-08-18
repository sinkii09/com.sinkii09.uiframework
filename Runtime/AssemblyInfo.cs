using System.Runtime.CompilerServices;

// SaveEnvelopeCodec and its CurrentSchemaVersion const are internal implementation details, but the
// schema-version tests must assert against the real constant rather than a hardcoded literal —
// otherwise they silently stop testing the boundary the day the version is bumped.
[assembly: InternalsVisibleTo("Sinkii09.UIFramework.Tests")]

// RecyclerView's recycling internals (RecycleWindow, CellPool, WindowState) are pure logic and are
// deliberately tested in EditMode — no scene, no ScrollRect, no frame waits — so the EditMode
// assembly needs the same access. They stay internal because they are implementation detail, not
// part of the control's public surface.
[assembly: InternalsVisibleTo("Sinkii09.UIFramework.Tests.Editor")]
