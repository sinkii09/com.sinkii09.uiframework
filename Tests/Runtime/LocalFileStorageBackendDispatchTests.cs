using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // Covers LocalFileStorageBackend's dispatch seam: thread pool on most platforms, inline on WebGL
    // where UniTask.RunOnThreadPool never resumes. Both modes are forced through the internal ctor so
    // they run in one Editor session — the whole point of using a runtime flag rather than #if.
    //
    // Scope note: this fixture does NOT re-assert what LocalFileStorageBackendTests already proves in
    // the default (thread-pool, in Editor) mode. It asserts inline-mode parity, the routing net, and
    // the default-mode mapping. Behaviour shared by both modes is proven once, over there.
    public class LocalFileStorageBackendDispatchTests
    {
        private const string KeyPrefix = "UIFrameworkTestDispatch";

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
            // Per-path try/catch: these live in the developer's REAL persistentDataPath, so one
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

        // --- Inline-mode parity: the write/replace/read paths must behave identically ------------

        [UnityTest]
        public IEnumerator Inline_RoundTripThroughSaveService_ReturnsEqualObject() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("RoundTrip");
            var service = new JsonSaveService(new LocalFileStorageBackend(useThreadPool: false));
            var data = new TestSaveData
            {
                Name = "inline-player",
                Score = 11,
                Inventory = new Dictionary<string, int> { ["rope"] = 3 }
            };

            await service.SaveAsync(key, data);
            var loaded = await service.LoadAsync<TestSaveData>(key);

            Assert.AreEqual("inline-player", loaded.Name);
            Assert.AreEqual(11, loaded.Score);
            Assert.AreEqual(3, loaded.Inventory["rope"]);
        });

        [UnityTest]
        public IEnumerator Inline_WriteTwice_TakesBothBranchesAndRotatesBackup() => UniTask.ToCoroutine(async () =>
        {
            // First write exercises File.Move, second exercises File.Replace — the branch the spike
            // was run to de-risk in the first place.
            var key = NewKey("Rotate");
            var backend = new LocalFileStorageBackend(useThreadPool: false);

            await backend.WriteAsync(key, "payload-one");
            Assert.IsFalse(File.Exists(BackupPathFor(key)), "A first save has nothing to back up.");

            await backend.WriteAsync(key, "payload-two");
            Assert.AreEqual("payload-two", await backend.ReadAsync(key));
            Assert.AreEqual("payload-one", await backend.ReadBackupAsync(key));
            Assert.IsFalse(File.Exists(PathFor(key) + ".tmp"), "The atomic rename must consume the temp file.");
        });

        [UnityTest]
        public IEnumerator Inline_ExistsAndDelete_TrackWrites() => UniTask.ToCoroutine(async () =>
        {
            var key = NewKey("ExistsDelete");
            var backend = new LocalFileStorageBackend(useThreadPool: false);

            Assert.IsFalse(await backend.ExistsAsync(key));
            await backend.WriteAsync(key, "one");
            await backend.WriteAsync(key, "two"); // creates the .bak
            Assert.IsTrue(await backend.ExistsAsync(key));

            Assert.IsTrue(await backend.DeleteAsync(key));
            Assert.IsFalse(File.Exists(PathFor(key)));
            Assert.IsFalse(File.Exists(BackupPathFor(key)), "A deleted key must not leak its backup.");
        });

        // --- The routing net --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Inline_EveryOperation_CompletesSynchronously() => UniTask.ToCoroutine(async () =>
        {
            // This is the ONLY thing that catches a call site left on UniTask.RunOnThreadPool. Such a
            // site compiles, passes every other test in this file (the Editor has a working thread
            // pool), and then hangs forever on WebGL. Hence: assert on completion STATUS, never on
            // "no exception thrown" — the measured WebGL failure throws nothing at all, it simply
            // never resumes. Covering only WriteAsync would leave four sites unguarded.
            var key = NewKey("Sync");
            var backend = new LocalFileStorageBackend(useThreadPool: false);

            var write = backend.WriteAsync(key, "payload");
            AssertCompletedInline("WriteAsync", write.Status);
            await write;

            // Second write so ReadBackupAsync has something to find.
            await backend.WriteAsync(key, "payload-two");

            var read = backend.ReadAsync(key);
            AssertCompletedInline("ReadAsync", read.Status);
            await read;

            var readBackup = backend.ReadBackupAsync(key);
            AssertCompletedInline("ReadBackupAsync", readBackup.Status);
            await readBackup;

            var exists = backend.ExistsAsync(key);
            AssertCompletedInline("ExistsAsync", exists.Status);
            await exists;

            var delete = backend.DeleteAsync(key);
            AssertCompletedInline("DeleteAsync", delete.Status);
            await delete;
        });

        // Reports the actual status, so an I/O failure (Faulted) is not misdiagnosed as "still
        // dispatched off-thread" — Pending is the only status that means an unrouted call site.
        private static void AssertCompletedInline(string op, UniTaskStatus status)
            => Assert.AreEqual(UniTaskStatus.Succeeded, status,
                $"{op} returned {status} under inline dispatch. " +
                "Pending means the call site is still on UniTask.RunOnThreadPool, which hangs on WebGL.");

        [Test]
        public void ContainerResolution_SelectsTheParameterlessConstructor()
        {
            // Regression net for a defect that shipped green during this very change. VContainer's
            // TypeAnalyzer enumerates constructors with BindingFlags.NonPublic and, absent an
            // [Inject] marker, picks the one with the MOST parameters (TypeAnalyzer.cs:227-244) — so
            // adding the internal test ctor above silently made the container try to resolve a
            // System.Boolean on every ISaveService resolve. Every other test in both storage fixtures
            // builds the backend with `new`, so 650+ green tests said nothing. This one goes through
            // a real container, which is the only way that class of break is visible.
            var builder = new ContainerBuilder();
            builder.Register<IStorageBackend, LocalFileStorageBackend>(Lifetime.Singleton);
            using var container = builder.Build();

            Assert.IsInstanceOf<LocalFileStorageBackend>(container.Resolve<IStorageBackend>());
        }

        [Test]
        public void Inline_Constructor_SweepsOrphanedTempSynchronously()
        {
            // Covers the sixth RunIo call site, which is unobservable through the public API: the
            // constructor's cleanup. Asserting without awaiting anything is what makes it meaningful
            // — a sweep still routed through the thread pool could not possibly have run yet.
            var key = NewKey("Sweep");
            Directory.CreateDirectory(RootDir);
            var orphan = PathFor(key) + ".tmp";
            File.WriteAllText(orphan, "orphan-from-a-prior-session");
            File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow - TimeSpan.FromMinutes(5));

            _ = new LocalFileStorageBackend(useThreadPool: false);

            Assert.IsFalse(File.Exists(orphan), "Inline construction must have swept the orphan before returning.");
        }

        // --- Default-mode mapping ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator PublicConstructor_OffWebGl_DispatchesOffThread() => UniTask.ToCoroutine(async () =>
        {
            // Both modes above are forced through the internal ctor, so an inverted comparison in the
            // platform expression would pass all of them and hang only on WebGL. This is the one test
            // that exercises the real default. It asserts observable behaviour rather than restating
            // the platform expression, which would just copy the bug into the test.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                Assert.Ignore("Default is inline on WebGL; this asserts the non-WebGL default.");

            var key = NewKey("Default");
            var backend = new LocalFileStorageBackend();

            var write = backend.WriteAsync(key, "payload");
            Assert.AreEqual(UniTaskStatus.Pending, write.Status, "Default dispatch must stay off the main thread.");
            await write;
            Assert.IsTrue(File.Exists(PathFor(key)));
        });

        // --- Cancellation parity ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator PreCancelledToken_ThrowsInThreadPoolMode() => UniTask.ToCoroutine(
            () => AssertPreCancelledThrowsAsync(useThreadPool: true));

        [UnityTest]
        public IEnumerator PreCancelledToken_ThrowsInInlineMode() => UniTask.ToCoroutine(
            () => AssertPreCancelledThrowsAsync(useThreadPool: false));

        // Covers the PRE-delegate check only. The post-delegate check that RunIo also performs
        // (mirroring UniTask.Run.cs:87) guards a token cancelled mid-write, which cannot be triggered
        // deterministically without injecting a hook into the backend purely for the test. That was
        // judged not worth it; the post-check is a code-parity requirement carrying a source citation
        // in RunIo's comment, and this is recorded rather than quietly skipped.
        private async UniTask AssertPreCancelledThrowsAsync(bool useThreadPool)
        {
            var key = NewKey(useThreadPool ? "CancelPool" : "CancelInline");
            var backend = new LocalFileStorageBackend(useThreadPool);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await backend.WriteAsync(key, "must-not-be-written", cts.Token);
                Assert.Fail($"A pre-cancelled token must throw (useThreadPool: {useThreadPool}).");
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            Assert.IsFalse(File.Exists(PathFor(key)), "A cancelled write must not leave a file behind.");
        }
    }
}
