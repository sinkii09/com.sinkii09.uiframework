using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // A loader whose loads finish only when the test says so, to stage every bank race in order.
    internal sealed class ControlledAssetLoader : IAssetLoader
    {
        private readonly Dictionary<string, UniTaskCompletionSource<SoundBank>> _pending = new();

        public int Loads;
        public readonly List<string> Unloads = new();
        public SoundBank SynchronousResult;
        // Models a loader that cannot be cancelled and completes late anyway.
        public bool IgnoreCancellation;

        public UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default) where T : Object
        {
            Loads++;
            if (SynchronousResult != null) return UniTask.FromResult((T)(Object)SynchronousResult);

            var source = new UniTaskCompletionSource<SoundBank>();
            _pending[key] = source;
            return AwaitAsync<T>(source, IgnoreCancellation ? CancellationToken.None : ct);
        }

        private static async UniTask<T> AwaitAsync<T>(UniTaskCompletionSource<SoundBank> source, CancellationToken ct)
            where T : Object
            => (T)(Object)await source.Task.AttachExternalCancellation(ct);

        public UniTask UnloadAsync(string key, CancellationToken ct = default)
        {
            Unloads.Add(key);
            return UniTask.CompletedTask;
        }

        public void Complete(string key, SoundBank bank) => _pending[key].TrySetResult(bank);

        public void Fail(string key) => _pending[key].TrySetException(new InvalidOperationException("boom"));
    }

    internal sealed class RecordingBankSink : ISoundBankSink
    {
        public readonly List<SoundBank> Registered = new();
        public readonly List<SoundBank> Unregistered = new();

        public void Register(SoundBank bank) => Registered.Add(bank);
        public void Unregister(SoundBank bank) => Unregistered.Add(bank);
    }

    internal static class UniTaskTestExtensions
    {
        // For tasks the test has already driven to completion synchronously.
        public static T Result<T>(this UniTask<T> task)
        {
            if (task.Status == UniTaskStatus.Pending) throw new InvalidOperationException("Task has not completed.");
            return task.GetAwaiter().GetResult();
        }
    }
}
