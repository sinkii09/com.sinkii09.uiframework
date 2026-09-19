using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;
using StubMigration = Sinkii09.UIFramework.Tests.SaveMigrationRegistryTests.StubMigration;

namespace Sinkii09.UIFramework.Tests
{
    // The load path through a migration chain. Registry semantics are in SaveMigrationRegistryTests.
    public class SaveMigrationTests
    {
        private const string Key = "MigrationTestKey";
        private const string OtherKey = "MigrationOtherKey";

        // Bumps Score, so "did the chain run, and how many times" is readable off the result.
        private static ISaveMigration BumpScore(int fromVersion) => new StubMigration(fromVersion, token =>
        {
            token["Score"] = token["Score"].Value<int>() + 100;
            return token;
        });

        private static JsonSaveService ServiceWith(FakeStorageBackend backend, SaveMigrationRegistry registry)
            => new(backend, registry, new SaveSlotContext());

        // --- The NEW-5 proof: version granularity matches chain granularity --------------------

        [UnityTest]
        public IEnumerator Save_StampsPerKeyVersion_OtherKeysUnaffected() => UniTask.ToCoroutine(async () =>
        {
            // Asserted on the RAW stamped JSON, not on a Load round-trip. A round-trip would pass even
            // with a single global version, because whatever was stamped is also what gets compared —
            // the bug this whole phase exists to prevent would sail straight through it.
            var backend = new FakeStorageBackend();
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));
            var service = ServiceWith(backend, registry);

            await service.SaveAsync(Key, Sample());
            await service.SaveAsync(OtherKey, Sample());

            Assert.AreEqual(2, JObject.Parse(backend.PeekPrimary(Key))["SchemaVersion"].Value<int>(),
                "A key with a one-step chain must be stamped v2.");
            Assert.AreEqual(1, JObject.Parse(backend.PeekPrimary(OtherKey))["SchemaVersion"].Value<int>(),
                "A key with NO chain must stay at v1 even while another key is on v2.");
        });

        // --- Chain execution ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Load_OneStepBehind_RunsTheChain() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));

            var loaded = await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(142, loaded.Score, "Sample() is 42; one step adds 100.");
        });

        [UnityTest]
        public IEnumerator Load_TwoStepsBehind_RunsBothInOrder() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));
            registry.Register(Key, BumpScore(2));

            var loaded = await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(242, loaded.Score, "Both steps must run, exactly once each.");
        });

        [UnityTest]
        public IEnumerator Load_MigrationDoesNotRewriteTheFile() => UniTask.ToCoroutine(async () =>
        {
            // Read-only by design: a chain that fails halfway must not be able to corrupt the source.
            var backend = new FakeStorageBackend();
            var original = Envelope(Sample(), 1);
            backend.SeedPrimary(Key, original);
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));

            await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(original, backend.PeekPrimary(Key), "Loading must never write back.");
        });

        [UnityTest]
        public IEnumerator Load_GapInChain_ThrowsSchemaVersion() => UniTask.ToCoroutine(async () =>
        {
            // Steps from v1 and v3 with nothing from v2 cap the current version at 2, so a v3 file is
            // "newer than supported". Loud beats running half a chain.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 3));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));
            registry.Register(Key, BumpScore(3));

            // The registry also reports the orphaned v3 step on first read — see
            // SaveMigrationRegistryTests. Expected here so it is acknowledged rather than suppressed.
            LogAssert.Expect(LogType.Error, new Regex("will never run"));

            var error = await CaptureAsync(async () =>
                await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key));

            Assert.IsInstanceOf<SaveSchemaVersionException>(error);
        });

        [UnityTest]
        public IEnumerator Load_VersionBelowChainStart_WarnsAndLoadsAsIs() => UniTask.ToCoroutine(async () =>
        {
            // A save with no SchemaVersion field deserializes to 0, and no chain starts at 0. Throwing
            // would turn every pre-versioning save into a hard failure the moment its game registered
            // a first migration, so this keeps the pre-engine behaviour: warn, load anyway.
            var backend = new FakeStorageBackend();
            // Genuinely omits the field rather than writing an explicit 0, because the claim being
            // tested is about what a PRE-VERSIONING save looks like. Newtonsoft leaves the int at its
            // default, which is how those files present themselves.
            backend.SeedPrimary(Key, new JObject { ["Data"] = JToken.FromObject(Sample()) }.ToString());
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));

            LogAssert.Expect(LogType.Warning, new Regex("loading as-is"));
            var loaded = await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(42, loaded.Score, "Unmigrated, but loaded.");
        });

        // --- Failure modes: none of these may consume the backup ----------------------------------

        [UnityTest]
        public IEnumerator Load_MigrationThrows_ThrowsMigrationAndKeepsBackup() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));   // must NOT be used
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1, _ => throw new InvalidOperationException("boom")));

            var error = await CaptureAsync(async () =>
                await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key));

            Assert.IsInstanceOf<SaveMigrationException>(error,
                "A failed migration is a code defect; recovering the older backup would destroy intact data.");
        });

        [UnityTest]
        public IEnumerator Load_MigrationReturnsNull_ThrowsMigration() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1, _ => null));

            var error = await CaptureAsync(async () =>
                await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key));

            Assert.IsInstanceOf<SaveMigrationException>(error);
        });

        [UnityTest]
        public IEnumerator Load_MigratedPayloadDoesNotBind_ThrowsMigrationNotJson() => UniTask.ToCoroutine(async () =>
        {
            // The subtle one. A chain that produces a shape T cannot read surfaces as a plain
            // JsonException from the codec — which IS a backup-recovery trigger. Without the
            // migration-aware wrap it would restore the OLDER backup over a save that was fine until
            // the chain touched it, and a consumer reading that as "corrupt, start fresh" would then
            // overwrite real progress.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1, _ => new JArray(1, 2, 3)));

            var error = await CaptureAsync(async () =>
                await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key));

            Assert.IsInstanceOf<SaveMigrationException>(error,
                "A post-migration bind failure must not be mistaken for corruption.");
        });

        // --- Telling a broken chain apart from a broken file ---------------------------------------

        [UnityTest]
        public IEnumerator Load_CorruptPrimaryButHealthyBackup_RecoversInsteadOfBlamingTheChain() => UniTask.ToCoroutine(async () =>
        {
            // "The chain broke it" and "it was already broken and the chain merely ran over the
            // damage" look identical from inside Parse. Refusing recovery for every migration failure
            // would strand a genuinely corrupt save that has a perfectly good backup beside it.
            //
            // The backup is the discriminator: same schema version, therefore the same chain. Here the
            // chain is fine and only the primary payload is junk, so recovery must succeed.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, new JObject { ["SchemaVersion"] = 1, ["Data"] = 12345 }.ToString());
            backend.SeedBackup(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1));   // identity: the chain itself is healthy

            LogAssert.ignoreFailingMessages = true;
            var loaded = await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key);

            Assert.IsNotNull(loaded, "A healthy backup must still be recoverable when a migration is registered.");
            Assert.AreEqual(42, loaded.Score);
        });

        [UnityTest]
        public IEnumerator Load_ChainBrokenOnBothCopies_StillRefusesRecovery() => UniTask.ToCoroutine(async () =>
        {
            // The other side of the same discriminator. A chain that fails on the backup as well is
            // the chain's fault, so no recovery may happen — restoring an older save here and letting
            // the consumer re-enable writing is how a code defect turns into permanent data loss.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            backend.SeedBackup(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1, _ => throw new InvalidOperationException("boom")));

            LogAssert.ignoreFailingMessages = true;
            var error = await CaptureAsync(async () =>
                await ServiceWith(backend, registry).LoadAsync<TestSaveData>(Key));

            Assert.IsInstanceOf<SaveMigrationException>(error);
        });

        // --- The registration guard the scope relies on ---------------------------------------------

        [Test]
        public void ContainerBuilder_Exists_SeesAnAlreadyRegisteredRegistry()
        {
            // UIFrameworkLifetimeScope registers its empty registry only when one is not already
            // present, so a game may register its own BEFORE calling base.Configure. That guard rests
            // entirely on Exists behaving this way, and the claim had no coverage.
            var builder = new ContainerBuilder();
            Assert.IsFalse(builder.Exists(typeof(SaveMigrationRegistry)), "Nothing registered yet.");

            builder.RegisterInstance(new SaveMigrationRegistry());

            Assert.IsTrue(builder.Exists(typeof(SaveMigrationRegistry)),
                "The framework must be able to detect a game-supplied registry and stand down.");
        }

        // --- End to end through a real container --------------------------------------------------

        [UnityTest]
        public IEnumerator ContainerResolved_Service_RunsRegisteredMigration() => UniTask.ToCoroutine(async () =>
        {
            // Asserts the migration RUNS, not merely that resolution succeeds. Resolution alone would
            // pass even if the registry were never reaching the codec.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), 1));
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, BumpScore(1));

            var builder = new ContainerBuilder();
            builder.RegisterInstance<IStorageBackend>(backend);
            builder.RegisterInstance(registry);
            // Mirrors UIFrameworkLifetimeScope. Omitting it throws on resolve, because a constructor
            // parameter is a hard dependency in VContainer — which is exactly how this test caught the
            // missing registration when slots were added.
            builder.Register<ISaveSlotContext, SaveSlotContext>(Lifetime.Singleton);
            builder.Register<ISaveService, JsonSaveService>(Lifetime.Singleton);
            using var container = builder.Build();

            var loaded = await container.Resolve<ISaveService>().LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(142, loaded.Score, "The container must inject the registry, not an empty one.");
        });
    }
}
