using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // The WebGL storage flush (FS.syncfs) is requested by the backend after every successful write
    // or delete — inline dispatch only, since that is WebGL; the thread-pool path has a real disk.
    // The bridge itself is a no-op outside a WebGL player, so the request is observed through the
    // internal ctor's flush seam. What the jslib does with it is proven only by the device test.
    public class LocalFileStorageBackendFlushTests
    {
        private const string KeyPrefix = "UIFrameworkTestFlush";

        private static string RootDir => Path.Combine(Application.persistentDataPath, "Saves");
        private static string PathFor(string key) => Path.Combine(RootDir, key + ".json");

        private readonly List<string> _touchedKeys = new();
        private int _flushRequests;

        private string NewKey(string suffix)
        {
            var key = KeyPrefix + suffix;
            _touchedKeys.Add(key);
            return key;
        }

        private LocalFileStorageBackend NewBackend(bool useThreadPool)
            => new(useThreadPool, () => _flushRequests++);

        [SetUp]
        public void SetUp() => _flushRequests = 0;

        [TearDown]
        public void Cleanup()
        {
            foreach (var key in _touchedKeys)
            {
                foreach (var path in new[] { PathFor(key), PathFor(key) + ".bak", PathFor(key) + ".tmp" })
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
        public IEnumerator Inline_EachWrite_RequestsOneFlush() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("Write");
            var backend = NewBackend(useThreadPool: false);

            await backend.WriteAsync(key, "one");   // File.Move branch
            await backend.WriteAsync(key, "two");   // File.Replace branch

            Assert.AreEqual(2, _flushRequests);
        });

        [Test]
        public void Inline_WriteSync_RequestsFlush()
        {
            // The pause/page-hide path: a synchronous save that is not pushed to IndexedDB is the
            // exact case this feature exists for.
            var key = NewKey("WriteSync");
            var backend = NewBackend(useThreadPool: false);

            backend.WriteSync(key, "payload");

            Assert.AreEqual(1, _flushRequests);
        }

        [UnityTest]
        public IEnumerator Inline_DeleteOfExistingKey_RequestsFlush() => UniTask.ToCoroutine(async () =>
        {
            // Without this a "start fresh" delete could be undone by the browser restoring the old
            // IndexedDB store on the next load.
            var key = NewKey("Delete");
            var backend = NewBackend(useThreadPool: false);
            await backend.WriteAsync(key, "payload");
            _flushRequests = 0;

            Assert.IsTrue(await backend.DeleteAsync(key));

            Assert.AreEqual(1, _flushRequests);
        });

        [UnityTest]
        public IEnumerator Inline_DeleteOfMissingKey_DoesNotFlush() => UniTask.ToCoroutine(async () =>
        {
            var backend = NewBackend(useThreadPool: false);

            Assert.IsFalse(await backend.DeleteAsync(NewKey("Missing")));

            Assert.AreEqual(0, _flushRequests);
        });

        [UnityTest]
        public IEnumerator Inline_ReadsAndExists_NeverFlush() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("Read");
            var backend = NewBackend(useThreadPool: false);
            await backend.WriteAsync(key, "payload");
            _flushRequests = 0;

            await backend.ReadAsync(key);
            await backend.ReadBackupAsync(key);
            await backend.ExistsAsync(key);

            Assert.AreEqual(0, _flushRequests);
        });

        [UnityTest]
        public IEnumerator ThreadPool_WritesAndDeletes_NeverFlush() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("ThreadPool");
            var backend = NewBackend(useThreadPool: true);

            await backend.WriteAsync(key, "one");
            backend.WriteSync(key, "two");
            await backend.DeleteAsync(key);

            Assert.AreEqual(0, _flushRequests);
        });
    }
}
