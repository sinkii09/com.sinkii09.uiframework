using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // JsonSaveService.TrySaveSync — the path the app-pause flush runs down, and the only save path
    // that is allowed to decline instead of waiting.
    //
    // The distinction that matters here and is easy to get backwards: DECLINING returns false,
    // FAILING throws. Both are asserted, because a caller inside OnApplicationPause treats them very
    // differently — one means "someone else has it", the other means "this save is gone".
    public class SaveServiceSyncWriteTests
    {
        private const string Key = "SyncWriteTestKey";

        private static SyncWritePayload Payload(int value) => new() { Value = value };

        [Test]
        public void TrySaveSync_WritesThroughTheSynchronousBackend()
        {
            var backend = new SyncCapableBackend();
            var service = new JsonSaveService(backend);

            Assert.That(service.TrySaveSync(Key, Payload(7)), Is.True);
            Assert.That(backend.SyncWrites, Is.EqualTo(1));
            Assert.That(backend.Peek(Key), Does.Contain("7"));
        }

        [Test]
        public void TrySaveSync_DeclinesWhenTheBackendCannotWriteSynchronously()
        {
            // A cloud or Steam backend. Nothing is written and nothing throws — the scheduler reads
            // the false and falls back to firing the async save.
            var service = new JsonSaveService(new AsyncOnlyBackend());

            Assert.That(service.TrySaveSync(Key, Payload(1)), Is.False);
        }

        [Test]
        public void TrySaveSync_RejectsNullBeforeTakingTheGate()
        {
            // Same shape as SaveAsync: a programmer error must not acquire the lock, or a null
            // argument during pause would leave the key permanently unwritable for the session.
            var backend = new SyncCapableBackend();
            var service = new JsonSaveService(backend);

            Assert.That(() => service.TrySaveSync<SyncWritePayload>(Key, null),
                Throws.InstanceOf<ArgumentNullException>());

            // The gate is provably free: a real save right after still goes through.
            Assert.That(service.TrySaveSync(Key, Payload(2)), Is.True);
        }

        [Test]
        public void TrySaveSync_PropagatesAWriteFailureRatherThanReportingItAsDeclined()
        {
            var backend = new SyncCapableBackend { SyncWriteError = new InvalidOperationException("disk full") };
            var service = new JsonSaveService(backend);

            // Returning false here would be the dangerous outcome: the caller would read it as
            // "already in flight, fine" and move on while the save was actually lost.
            Assert.That(() => service.TrySaveSync(Key, Payload(3)),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator TrySaveSync_DeclinesWhileAnotherSaveHoldsTheKeyGate() => UniTask.ToCoroutine(async () =>
        {
            // The reason the gate probe uses a ZERO timeout. Waiting here would deadlock: this runs
            // inside OnApplicationPause, and the in-flight save's continuation resumes on a player
            // loop that stops the moment that message returns.
            var release = new UniTaskCompletionSource();
            var backend = new SyncCapableBackend { HoldAsyncWritesUntil = release };
            var service = new JsonSaveService(backend);

            var inFlight = service.SaveAsync(Key, Payload(1));
            await UniTask.Yield();

            Assert.That(service.TrySaveSync(Key, Payload(2)), Is.False,
                "The backend CAN write synchronously — the false must come from the held gate, not the capability.");
            Assert.That(backend.SyncWrites, Is.Zero);

            release.TrySetResult();
            await inFlight;

            // And once the gate is free again it writes, so the decline really was temporary.
            Assert.That(service.TrySaveSync(Key, Payload(3)), Is.True);
        });

        // --- fakes ---------------------------------------------------------------------------

        internal sealed class SyncWritePayload
        {
            public int Value;
        }

        // Mirrors LocalFileStorageBackend's shape: one backend, both write modes.
        private sealed class SyncCapableBackend : IStorageBackend, ISynchronousStorageBackend
        {
            private readonly Dictionary<string, string> _primary = new();

            internal int SyncWrites { get; private set; }
            internal Exception SyncWriteError { get; set; }
            internal UniTaskCompletionSource HoldAsyncWritesUntil { get; set; }

            internal string Peek(string key) => _primary.TryGetValue(key, out var v) ? v : null;

            public void WriteSync(string key, string contents)
            {
                if (SyncWriteError != null) throw SyncWriteError;
                SyncWrites++;
                _primary[key] = contents;
            }

            public async UniTask WriteAsync(string key, string contents, CancellationToken ct = default)
            {
                if (HoldAsyncWritesUntil != null) await HoldAsyncWritesUntil.Task;
                _primary[key] = contents;
            }

            public UniTask<string> ReadAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult(Peek(key));

            public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult(_primary.ContainsKey(key));

            public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult(_primary.Remove(key));

            public UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult<string>(null);
        }

        // Deliberately does NOT implement ISynchronousStorageBackend.
        private sealed class AsyncOnlyBackend : IStorageBackend
        {
            public UniTask WriteAsync(string key, string contents, CancellationToken ct = default)
                => UniTask.CompletedTask;

            public UniTask<string> ReadAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult<string>(null);

            public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult(false);

            public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult(false);

            public UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default)
                => UniTask.FromResult<string>(null);
        }
    }
}
