using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    internal interface ISoundBankSink
    {
        void Register(SoundBank bank);
        void Unregister(SoundBank bank);
    }

    // Reference-counted bank loading. Every rule here exists because of a race:
    // - Two loads of one key share one in-flight load, so the loader is asked once.
    // - The shared load is tied to the service's lifetime only. Tying it to the first caller's token
    //   would let that caller's cancel fail every other waiter.
    // - An entry still loading is kept even when its count drops to 0, so a reload in that window
    //   joins the same load instead of starting a second one. Both loaders keep one handle per key,
    //   and a stale load releasing "its" handle would release the new entry's.
    // - A load that completes with nobody left wanting it releases its handle at once.
    internal sealed class BankManager : IDisposable
    {
        private sealed class Entry
        {
            public int References;
            public SoundBank Bank;
            public readonly UniTaskCompletionSource<bool> Loaded = new();
        }

        private readonly IAssetLoader _loader;
        private readonly ISoundBankSink _sink;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Dictionary<string, Entry> _entries = new();
        private bool _disposed;

        public BankManager(IAssetLoader loader, ISoundBankSink sink)
        {
            _loader = loader;
            _sink = sink;
        }

        public bool IsLoaded(string key) => key != null && _entries.TryGetValue(key, out var entry) && entry.Bank != null;

        public int ReferenceCount(string key) => key != null && _entries.TryGetValue(key, out var entry) ? entry.References : 0;

        // Never throws. False when the bank could not be loaded, or this caller cancelled; in both
        // cases the caller holds no reference and must not call Unload for it.
        public async UniTask<bool> LoadAsync(string key, CancellationToken ct = default)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return false;

            // The reference is taken BEFORE the load starts: a loader that completes synchronously
            // would otherwise find a count of 0 and release the bank it just loaded.
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.References++;
            }
            else
            {
                entry = new Entry { References = 1 };
                _entries.Add(key, entry);
                LoadEntryAsync(key, entry).Forget();
            }

            if (entry.Bank != null) return true;

            try
            {
                return await entry.Loaded.Task.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                Release(key, entry);
                return false;
            }
        }

        public void Unload(string key)
        {
            if (key == null || !_entries.TryGetValue(key, out var entry))
            {
                Debug.LogWarning($"[Audio] UnloadBank('{key}') with no matching successful load; ignored.");
                return;
            }
            Release(key, entry);
        }

        private void Release(string key, Entry entry)
        {
            if (entry.References <= 0) return;
            if (--entry.References > 0) return;
            // Still loading: LoadEntryAsync sees the 0 when the load lands and releases the handle.
            if (entry.Bank == null) return;

            Remove(key, entry);
            _sink.Unregister(entry.Bank);
            ReleaseHandle(key);
        }

        private async UniTaskVoid LoadEntryAsync(string key, Entry entry)
        {
            SoundBank bank;
            try
            {
                bank = await _loader.LoadAssetAsync<SoundBank>(key, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Only the service's own disposal cancels this; nothing to report.
                Fail(key, entry);
                return;
            }
            catch (Exception ex)
            {
                // The loader's failures vary by backend (missing key, bad bundle); all mean "no bank".
                Debug.LogError($"[Audio] Bank '{key}' failed to load: {ex.Message}");
                Fail(key, entry);
                return;
            }

            if (bank == null)
            {
                Debug.LogError($"[Audio] Bank '{key}' loaded as null.");
                ReleaseHandle(key);
                Fail(key, entry);
                return;
            }

            if (_disposed || entry.References <= 0)
            {
                Remove(key, entry);
                ReleaseHandle(key);
                entry.Loaded.TrySetResult(false);
                return;
            }

            entry.Bank = bank;
            _sink.Register(bank);
            entry.Loaded.TrySetResult(true);
        }

        private void Fail(string key, Entry entry)
        {
            Remove(key, entry);
            entry.Loaded.TrySetResult(false);
        }

        private void Remove(string key, Entry entry)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(key);
        }

        private void ReleaseHandle(string key)
            => _loader.UnloadAsync(key).Forget(ex => Debug.LogError($"[Audio] Releasing bank '{key}' failed: {ex.Message}"));

        // Releases every loaded bank's handle. The engine's voices are stopped by the service first.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();

            foreach (var pair in _entries)
                if (pair.Value.Bank != null) ReleaseHandle(pair.Key);
            _entries.Clear();
        }
    }
}
