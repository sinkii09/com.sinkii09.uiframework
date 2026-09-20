using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Sinkii09.UIFramework.Tests
{
    // Test double for AutoSaveScheduler's collaborator. The scheduler is what is under test here —
    // when to write, how many times, and with which snapshot — so a counting service isolates that
    // from serialization and file I/O, both of which have their own suites.
    //
    // Implements ISynchronousSaveService, so the pause path is reachable.
    //
    // A service that CANNOT write synchronously is AsyncOnlySaveService below — it simply does not
    // implement the interface. The first version of this class had a supportsSync flag that made
    // TrySaveSync return false while still implementing the interface, which meant the scheduler's
    // "no synchronous service at all" branch was never once exercised. The test asserting that
    // branch passed nothing and failed for a reason that had nothing to do with the branch.
    internal sealed class CountingSaveService : ISaveService, ISynchronousSaveService
    {
        internal int AsyncWrites { get; private set; }
        internal int SyncWrites { get; private set; }
        internal List<object> Payloads { get; } = new();

        // Set to hold every async write open, so a test can observe the in-flight window.
        internal UniTaskCompletionSource Gate { get; set; }

        // Consumed once, then cleared — a retry after a failure must be able to succeed.
        internal bool FailNextAsyncWrite { get; set; }

        public async UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class
        {
            if (Gate != null) await Gate.Task;

            if (FailNextAsyncWrite)
            {
                FailNextAsyncWrite = false;
                throw new InvalidOperationException("simulated write failure");
            }

            AsyncWrites++;
            Payloads.Add(data);
        }

        public bool TrySaveSync<T>(string key, T data) where T : class
        {
            SyncWrites++;
            Payloads.Add(data);
            return true;
        }

        public UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class
            => SaveAsync(typeof(T).Name, data, ct);

        // Never exercised by the scheduler. Throwing beats returning a plausible default: if a future
        // change starts calling one of these, the test should stop rather than quietly pass.
        public UniTask<T> LoadAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Observable<SaveEventArgs> OnSaveStartedAsObservable => Observable.Empty<SaveEventArgs>();
        public Observable<SaveEventArgs> OnSaveCompletedAsObservable => Observable.Empty<SaveEventArgs>();
        public Observable<SaveEventArgs> OnSaveFailedAsObservable => Observable.Empty<SaveEventArgs>();
    }

    // A save service with no synchronous capability at all — a cloud or Steam backend, where the
    // write genuinely cannot complete without awaiting. Deliberately does NOT implement
    // ISynchronousSaveService: that absence IS the capability check the scheduler performs.
    internal sealed class AsyncOnlySaveService : ISaveService
    {
        internal int AsyncWrites { get; private set; }

        public UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class
        {
            AsyncWrites++;
            return UniTask.CompletedTask;
        }

        public UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class
            => SaveAsync(typeof(T).Name, data, ct);

        public UniTask<T> LoadAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Observable<SaveEventArgs> OnSaveStartedAsObservable => Observable.Empty<SaveEventArgs>();
        public Observable<SaveEventArgs> OnSaveCompletedAsObservable => Observable.Empty<SaveEventArgs>();
        public Observable<SaveEventArgs> OnSaveFailedAsObservable => Observable.Empty<SaveEventArgs>();
    }

    // Minimal saveable payload. A class, because ISaveService constrains T to one.
    internal sealed class CountingSavePayload
    {
        internal int Value;
    }
}
