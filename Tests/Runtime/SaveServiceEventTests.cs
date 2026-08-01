using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Covers the R3 event contract, the generic key-less overloads, and disposal semantics.
    public class SaveServiceEventTests
    {
        private const string Key = "UIFrameworkTestEvents";

        [UnityTest]
        public IEnumerator SaveAsync_Success_FiresStartedThenCompleted() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            var order = new List<string>();
            using var s1 = service.OnSaveStartedAsObservable.Subscribe(_ => order.Add("started"));
            using var s2 = service.OnSaveCompletedAsObservable.Subscribe(_ => order.Add("completed"));

            await service.SaveAsync(Key, Sample());
            CollectionAssert.AreEqual(new[] { "started", "completed" }, order);
        });

        [UnityTest]
        public IEnumerator SaveAsync_BackendThrows_FiresOnSaveFailedAndRethrows() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend { WriteError = new InvalidOperationException("disk on fire") };
            var service = new JsonSaveService(backend);
            SaveEventArgs? failed = null;
            using var sub = service.OnSaveFailedAsObservable.Subscribe(e => failed = e);

            var error = await CaptureAsync(async () => await service.SaveAsync(Key, Sample()));
            Assert.IsInstanceOf<InvalidOperationException>(error);
            Assert.IsTrue(failed.HasValue);
            Assert.AreEqual(Key, failed.Value.Key);
            Assert.IsInstanceOf<InvalidOperationException>(failed.Value.Error);
        });

        // Without a terminal event on cancellation a "Saving..." indicator bound to
        // Started/Completed would hang forever. Failed carries the OperationCanceledException.
        [UnityTest]
        public IEnumerator SaveAsync_Cancelled_FiresOnSaveFailed() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend { WriteError = new OperationCanceledException() };
            var service = new JsonSaveService(backend);
            var startedCount = 0;
            SaveEventArgs? failed = null;
            using var s1 = service.OnSaveStartedAsObservable.Subscribe(_ => startedCount++);
            using var s2 = service.OnSaveFailedAsObservable.Subscribe(e => failed = e);

            var error = await CaptureAsync(async () => await service.SaveAsync(Key, Sample()));
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreEqual(1, startedCount);
            Assert.IsTrue(failed.HasValue, "Every OnSaveStarted must have a terminal partner.");
            Assert.IsInstanceOf<OperationCanceledException>(failed.Value.Error);
        });

        [UnityTest]
        public IEnumerator SaveAsync_NullData_ThrowsAndEmitsNoStarted() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            var startedCount = 0;
            using var sub = service.OnSaveStartedAsObservable.Subscribe(_ => startedCount++);

            var error = await CaptureAsync(async () => await service.SaveAsync<TestSaveData>(Key, null));
            Assert.IsInstanceOf<ArgumentNullException>(error);
            Assert.AreEqual(0, startedCount, "The null guard must run before the gate and before any event.");
        });

        [UnityTest]
        public IEnumerator SaveAsync_InvalidKey_ThrowsArgumentException() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            var error = await CaptureAsync(async () => await service.SaveAsync("bad key/with slash", Sample()));
            Assert.IsInstanceOf<ArgumentException>(error);
        });

        [UnityTest]
        public IEnumerator ExistsAsync_Generic_MatchesStringKeyed() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            Assert.IsFalse(await service.ExistsAsync<TestSaveData>());

            await service.SaveAsync(Sample()); // key defaults to typeof(T).Name
            Assert.IsTrue(await service.ExistsAsync<TestSaveData>());
            Assert.IsTrue(await service.ExistsAsync(nameof(TestSaveData)));
        });

        [UnityTest]
        public IEnumerator DeleteAsync_Generic_MatchesStringKeyed() => UniTask.ToCoroutine(async () =>
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            await service.SaveAsync(Sample());

            Assert.IsTrue(await service.DeleteAsync<TestSaveData>());
            Assert.IsFalse(await service.ExistsAsync(nameof(TestSaveData)));
            Assert.IsFalse(await service.DeleteAsync<TestSaveData>(), "Deleting a missing key returns false.");
        });

        [Test]
        public void Dispose_IsIdempotent()
        {
            var service = new JsonSaveService(new FakeStorageBackend());
            service.Dispose();
            Assert.DoesNotThrow(() => service.Dispose());
        }

        // Regression test: without the post-dispose publish guard, OnSaveStarted throws
        // ObjectDisposedException, the catch block then throws a SECOND time while reporting the
        // failure, and the original error is masked.
        [UnityTest]
        public IEnumerator Dispose_ThenSave_DoesNotThrowObjectDisposed() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            var service = new JsonSaveService(backend);
            service.Dispose();

            var error = await CaptureAsync(async () => await service.SaveAsync(Key, Sample()));
            Assert.IsNull(error, "An in-flight save at teardown must complete silently, not throw.");
            Assert.IsNotNull(backend.PeekPrimary(Key), "The save must still reach storage.");
        });
    }
}
