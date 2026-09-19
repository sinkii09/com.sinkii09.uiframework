using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Registry semantics: how the current version is derived, and which registration mistakes are
    // rejected. The load-path behaviour lives in SaveMigrationTests.
    public class SaveMigrationRegistryTests
    {
        private const string KeyA = "MigrationKeyA";
        private const string KeyB = "MigrationKeyB";

        // Lambda-backed step so each test states only the part it cares about.
        internal sealed class StubMigration : ISaveMigration
        {
            private readonly Func<JToken, JToken> _migrate;
            public int FromVersion { get; }

            internal StubMigration(int fromVersion, Func<JToken, JToken> migrate = null)
            {
                FromVersion = fromVersion;
                _migrate = migrate ?? (token => token);
            }

            public JToken Migrate(JToken data) => _migrate(data);
        }

        [Test]
        public void CurrentVersionFor_NoChain_IsTheBaseVersion()
        {
            Assert.AreEqual(SaveEnvelopeCodec.CurrentSchemaVersion,
                new SaveMigrationRegistry().CurrentVersionFor(KeyA));
        }

        [Test]
        public void CurrentVersionFor_ChainOfTwo_WalksToTheEnd()
        {
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));
            registry.Register(KeyA, new StubMigration(2));

            Assert.AreEqual(3, registry.CurrentVersionFor(KeyA), "v1 -> v2 -> v3 means current is 3.");
        }

        [Test]
        public void CurrentVersionFor_IsPerKey_NotGlobal()
        {
            // The whole reason this class exists. A global version would return 2 for BOTH keys, and
            // key B's unchanged v1 data would start being stamped v2 — silent and permanent.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));

            Assert.AreEqual(2, registry.CurrentVersionFor(KeyA));
            Assert.AreEqual(1, registry.CurrentVersionFor(KeyB), "A key with no chain must never move.");
        }

        [Test]
        public void CurrentVersionFor_GapInChain_StopsAtTheGapAndReportsTheOrphan()
        {
            // Steps from v1 and v3, nothing from v2. The walk must stop at 2 rather than claiming 4 —
            // that is what makes a v3 file fail loudly instead of running half a chain. The orphaned
            // v3 step is reported, because otherwise the only symptom is v3 saves being rejected as
            // "newer than this build supports", which points at the wrong cause entirely.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));
            registry.Register(KeyA, new StubMigration(3));

            LogAssert.Expect(LogType.Error, new Regex("will never run"));
            Assert.AreEqual(2, registry.CurrentVersionFor(KeyA));
        }

        [Test]
        public void Register_DuplicateFromVersion_Throws()
        {
            // Last-wins would pick an arbitrary winner AND leave no trace: the derived version is
            // identical either way, so the only evidence would be wrong data much later.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));

            Assert.Throws<ArgumentException>(() => registry.Register(KeyA, new StubMigration(1)));
        }

        [Test]
        public void Register_SameFromVersionOnADifferentKey_IsFine()
        {
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));

            Assert.DoesNotThrow(() => registry.Register(KeyB, new StubMigration(1)));
        }

        [Test]
        public void Register_FromVersionBelowOne_Throws()
        {
            var registry = new SaveMigrationRegistry();
            Assert.Throws<ArgumentException>(() => registry.Register(KeyA, new StubMigration(0)));
        }

        [Test]
        public void Register_NullOrEmptyKey_Throws()
        {
            var registry = new SaveMigrationRegistry();
            Assert.Throws<ArgumentException>(() => registry.Register(null, new StubMigration(1)));
            Assert.Throws<ArgumentException>(() => registry.Register("", new StubMigration(1)));
        }

        [Test]
        public void Register_AfterTheRegistryHasBeenRead_Throws()
        {
            // Loads take no lock and can resume off the main thread, so a registry that is still
            // mutable once reads have begun is a data race. Freezing on first read also enforces the
            // "configure it at container-build time" rule structurally instead of by convention.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));
            registry.CurrentVersionFor(KeyA);

            Assert.Throws<InvalidOperationException>(() => registry.Register(KeyA, new StubMigration(2)));
        }

        [Test]
        public void ChainNotStartingAtTheBaseVersion_ReportsTheUnreachableStep()
        {
            // The quiet trap: registering only a v2 step — the natural move once every player is
            // thought to be past v1 — leaves the derived current version at 1. The step never runs,
            // and every v2 save instead reads as "newer than this build supports", which gets blamed
            // on a newer release rather than on the missing v1 step.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(2));

            LogAssert.Expect(LogType.Error, new Regex("will never run"));
            Assert.AreEqual(1, registry.CurrentVersionFor(KeyA), "An unreachable step must not move the version.");
        }

        [Test]
        public void ContiguousChain_ReportsNothing()
        {
            // Guards the guard: a correct chain must stay silent, or the error above becomes noise
            // everyone learns to ignore.
            var registry = new SaveMigrationRegistry();
            registry.Register(KeyA, new StubMigration(1));
            registry.Register(KeyA, new StubMigration(2));

            Assert.AreEqual(3, registry.CurrentVersionFor(KeyA));
        }

        [Test]
        public void Empty_IsFrozenAndRejectsRegistration()
        {
            // Shared static instance. It is only safe to share because nothing can add to it — the
            // usual "a static registry leaks chains across scopes and test runs" objection cannot
            // apply to something immutable.
            Assert.Throws<InvalidOperationException>(
                () => SaveMigrationRegistry.Empty.Register(KeyA, new StubMigration(1)));
        }
    }
}
