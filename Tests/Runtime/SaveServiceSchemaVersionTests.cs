using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // SchemaVersion was written but never read until 2026-07-30 — the envelope's "forward insurance"
    // bought nothing, and the first schema change would have read as "no save yet" and been
    // overwritten. These cover the enforcement that closed it.
    public class SaveServiceSchemaVersionTests
    {
        private const string Key = "UIFrameworkTestSchema";

        [UnityTest]
        public IEnumerator LoadAsync_OlderSchemaVersion_LoadsAndWarns() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), SaveEnvelopeCodec.CurrentSchemaVersion - 1));
            var service = new JsonSaveService(backend);

            LogAssert.Expect(LogType.Warning, new Regex("no migration engine"));
            Assert.AreEqual(42, (await service.LoadAsync<TestSaveData>(Key)).Score);
        });

        [UnityTest]
        public IEnumerator LoadAsync_NewerSchemaVersion_ThrowsAndIgnoresBackup() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Envelope(Sample(), NewerVersion));
            // A perfectly good backup exists — it must NOT be used. It is the player's own older
            // progress, and silently loading it in place of a newer-build save would destroy data.
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<SaveSchemaVersionException>(error);
        });

        // Regression tests for a post-implementation review finding: the version check originally ran
        // AFTER the payload-null check, so a v2 file whose Data was absent or reshaped threw a plain
        // JsonException, fell through to backup recovery, and quietly restored the older save over the
        // newer one — the exact data loss this whole change set out to remove. Verified non-vacuous:
        // both fail when the check order is reverted.
        [UnityTest]
        public IEnumerator LoadAsync_NewerSchemaWithNoDataField_ThrowsSchemaError() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, $"{{\"SchemaVersion\":{NewerVersion}}}");
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<SaveSchemaVersionException>(error, "Version must be checked before the payload binds.");
        });

        [UnityTest]
        public IEnumerator LoadAsync_NewerSchemaWithReshapedData_ThrowsSchemaError() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            // A v2 that turned Data into an array — it cannot bind to TestSaveData at all.
            backend.SeedPrimary(Key, $"{{\"SchemaVersion\":{NewerVersion},\"Data\":[1,2,3]}}");
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<SaveSchemaVersionException>(error, "A reshaped payload must not mask the version.");
        });

        [UnityTest]
        public IEnumerator LoadAsync_CorruptPrimaryAndNewerSchemaBackup_ThrowsPrimaryError() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, "{}");
            backend.SeedBackup(Key, Envelope(Sample(), NewerVersion)); // unusable for recovery
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<JsonException>(error);
            StringAssert.Contains("not a valid SaveEnvelope", error.Message);
        });
    }
}
