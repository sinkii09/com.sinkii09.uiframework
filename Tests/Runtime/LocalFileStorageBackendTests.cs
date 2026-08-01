using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Exercises the real disk backend against Application.persistentDataPath. Keys are prefixed so
    // TearDown can clean up without touching a developer's actual saves; a crashed run can leave
    // UIFrameworkTestFile* files behind, which is accepted.
    public class LocalFileStorageBackendTests
    {
        private const string KeyPrefix = "UIFrameworkTestFile";

        private static string RootDir => Path.Combine(Application.persistentDataPath, "Saves");
        private static string PathFor(string key) => Path.Combine(RootDir, key + ".json");
        private static string BackupPathFor(string key) => Path.Combine(RootDir, key + ".json.bak");

        private readonly List<string> _touchedKeys = new();

        private string NewKey(string suffix)
        {
            var key = KeyPrefix + suffix;
            _touchedKeys.Add(key);
            return key;
        }

        [TearDown]
        public void Cleanup()
        {
            // Per-path try/catch: these are files in the developer's REAL persistentDataPath, so one
            // locked file must not abort the loop and leak every remaining key.
            foreach (var key in _touchedKeys)
            {
                foreach (var path in new[] { PathFor(key), BackupPathFor(key), PathFor(key) + ".tmp" })
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (IOException ex)
                    {
                        Debug.LogWarning($"[Tests] Could not clean up '{path}': {ex.Message}");
                    }
                }
            }

            _touchedKeys.Clear();
        }

        [UnityTest]
        public IEnumerator RoundTrip_PocoWithDictionary_ReturnsEqualObject() => UniTask.ToCoroutine(async () =>
        {
            // The Dictionary is the whole reason this framework uses Newtonsoft over JsonUtility.
            var key = NewKey("RoundTrip");
            var service = new JsonSaveService(new LocalFileStorageBackend());
            var data = new TestSaveData
            {
                Name = "disk-player",
                Score = 7,
                Inventory = new Dictionary<string, int> { ["bomb"] = 2 }
            };

            await service.SaveAsync(key, data);
            var loaded = await service.LoadAsync<TestSaveData>(key);

            Assert.AreEqual("disk-player", loaded.Name);
            Assert.AreEqual(7, loaded.Score);
            Assert.AreEqual(2, loaded.Inventory["bomb"]);
        });

        [UnityTest]
        public IEnumerator Write_FirstTime_CreatesFileWithoutBackup() => UniTask.ToCoroutine(async () =>
        {
            // Exercises the File.Move branch — File.Replace requires an existing destination.
            var key = NewKey("FirstWrite");
            var backend = new LocalFileStorageBackend();

            await backend.WriteAsync(key, "payload-one");

            Assert.IsTrue(File.Exists(PathFor(key)));
            Assert.IsFalse(File.Exists(BackupPathFor(key)), "A first save has nothing to back up.");
        });

        [UnityTest]
        public IEnumerator Write_Twice_SecondWriteBacksUpTheFirst() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("Backup");
            var backend = new LocalFileStorageBackend();

            await backend.WriteAsync(key, "payload-one");
            await backend.WriteAsync(key, "payload-two");

            Assert.AreEqual("payload-two", await backend.ReadAsync(key));
            Assert.AreEqual("payload-one", await backend.ReadBackupAsync(key));
        });

        [UnityTest]
        public IEnumerator Write_LeavesNoTempFileBehind() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("NoTemp");
            var backend = new LocalFileStorageBackend();

            await backend.WriteAsync(key, "payload");

            Assert.IsFalse(File.Exists(PathFor(key) + ".tmp"), "The atomic rename must consume the temp file.");
        });

        [UnityTest]
        public IEnumerator ExistsAsync_ReflectsWriteAndDelete() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("Exists");
            var backend = new LocalFileStorageBackend();

            Assert.IsFalse(await backend.ExistsAsync(key));
            await backend.WriteAsync(key, "payload");
            Assert.IsTrue(await backend.ExistsAsync(key));
            await backend.DeleteAsync(key);
            Assert.IsFalse(await backend.ExistsAsync(key));
        });

        [UnityTest]
        public IEnumerator Delete_RemovesPrimaryAndBackup() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("Delete");
            var backend = new LocalFileStorageBackend();
            await backend.WriteAsync(key, "payload-one");
            await backend.WriteAsync(key, "payload-two"); // creates the .bak

            Assert.IsTrue(await backend.DeleteAsync(key));
            Assert.IsFalse(File.Exists(PathFor(key)));
            Assert.IsFalse(File.Exists(BackupPathFor(key)), "A deleted key must not leak its backup.");
        });

        [UnityTest]
        public IEnumerator Delete_MissingKey_ReturnsFalse() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("DeleteMissing");
            Assert.IsFalse(await new LocalFileStorageBackend().DeleteAsync(key));
        });

        [UnityTest]
        public IEnumerator TwoRapidSaves_BothComplete_LastWriteWins() => UniTask.ToCoroutine(async () =>
        {
            // The per-key SemaphoreSlim must serialise these; neither may throw and the file must
            // end up holding one complete payload, never a mix.
            var key = NewKey("Concurrent");
            var service = new JsonSaveService(new LocalFileStorageBackend());

            var first = service.SaveAsync(key, new TestSaveData { Name = "first", Score = 1 });
            var second = service.SaveAsync(key, new TestSaveData { Name = "second", Score = 2 });
            await UniTask.WhenAll(first, second);

            var loaded = await service.LoadAsync<TestSaveData>(key);
            Assert.IsNotNull(loaded);
            Assert.Contains(loaded.Name, new[] { "first", "second" });
            Assert.AreEqual(loaded.Name == "first" ? 1 : 2, loaded.Score, "The file must hold one complete payload.");
        });
    }
}
