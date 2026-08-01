using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Covers the load path's corruption handling. A 2026-07-30 review found that a present-but-invalid
    // file read back as "no save yet" (Newtonsoft only throws for MALFORMED json), letting the caller
    // start fresh and rotate the last good backup out. These are the regression tests for that.
    // Schema-version behaviour lives in SaveServiceSchemaVersionTests.
    public class SaveServiceLoadTests
    {
        private const string Key = "UIFrameworkTestLoad";

        [UnityTest]
        public IEnumerator LoadAsync_MissingFile_ReturnsNull() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            Assert.IsNull(await service.LoadAsync<TestSaveData>(Key));
        });

        [UnityTest]
        public IEnumerator LoadAsync_RoundTrip_PreservesDictionary() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            await service.SaveAsync(Key, Sample());

            var loaded = await service.LoadAsync<TestSaveData>(Key);
            Assert.AreEqual("player-one", loaded.Name);
            Assert.AreEqual(42, loaded.Score);
            Assert.AreEqual(3, loaded.Inventory["potion"]);
            Assert.AreEqual(1, loaded.Inventory["ether"]);
        });

        [UnityTest]
        public IEnumerator LoadAsync_EmptyFile_Throws() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, string.Empty);
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<JsonException>(error, "An empty file must not read back as 'no save yet'.");
        });

        [UnityTest]
        public IEnumerator LoadAsync_WrongShapeJsonNoBackup_Throws() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, "{}"); // valid json, not a valid envelope — Newtonsoft does NOT throw
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<JsonException>(error, "Wrong-shaped json must be corruption, not 'no save yet'.");
        });

        [UnityTest]
        public IEnumerator LoadAsync_WrongShapeJsonWithGoodBackup_RecoversAndWarns() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, "{}");
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var service = new JsonSaveService(backend);

            LogAssert.Expect(LogType.Warning, new Regex("recovered from backup"));
            var loaded = await service.LoadAsync<TestSaveData>(Key);
            Assert.AreEqual(42, loaded.Score);
        });

        [UnityTest]
        public IEnumerator LoadAsync_MalformedJson_StillRecoversFromBackup() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, "{ this is not json");
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            var service = new JsonSaveService(backend);

            LogAssert.Expect(LogType.Warning, new Regex("recovered from backup"));
            Assert.AreEqual(42, (await service.LoadAsync<TestSaveData>(Key)).Score);
        });

        [UnityTest]
        public IEnumerator LoadAsync_CorruptPrimaryAndCorruptBackup_Throws() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, "{}");
            backend.SeedBackup(Key, "also not an envelope");
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<JsonException>(error);
            // The PRIMARY's error must be the one that surfaces. Asserting only the type would pass
            // either way — the backup's JsonReaderException is also a JsonException.
            StringAssert.Contains("not a valid SaveEnvelope", error.Message);
        });

        [UnityTest]
        public IEnumerator LoadAsync_BackupReadThrows_RethrowsPrimaryError() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend
            {
                ReadBackupError = new IOException("backup file is locked")
            };
            backend.SeedPrimary(Key, "{}");
            var service = new JsonSaveService(backend);

            var error = await CaptureAsync(async () => await service.LoadAsync<TestSaveData>(Key));
            Assert.IsInstanceOf<JsonException>(error, "A failed backup read must surface the primary error, not its own.");
            StringAssert.Contains("not a valid SaveEnvelope", error.Message);
        });
    }
}
