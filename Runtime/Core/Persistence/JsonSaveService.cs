using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // ISaveService implementation: orchestration only — per-key re-entrancy guard, R3 save-lifecycle
    // events, and backup-recovery policy. Serialization lives in SaveEnvelopeCodec, raw storage in
    // IStorageBackend.
    public sealed class JsonSaveService : ISaveService, IDisposable
    {
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        private readonly IStorageBackend _backend;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
        private readonly JsonSerializerSettings _settings = new()
        {
#if UNITY_EDITOR
            Formatting = Formatting.Indented,   // readable saves while developing
#else
            Formatting = Formatting.None,       // roughly half the file size and parse cost in a build
#endif
            NullValueHandling = NullValueHandling.Include
            // ReferenceLoopHandling is deliberately left at its default (Error): a POCO with a
            // parent back-pointer should fail loudly at save time, not silently drop the reference.
        };

        private readonly SaveEventPublisher _events = new();

        public Observable<SaveEventArgs> OnSaveStartedAsObservable => _events.Started;
        public Observable<SaveEventArgs> OnSaveCompletedAsObservable => _events.Completed;
        public Observable<SaveEventArgs> OnSaveFailedAsObservable => _events.Failed;

        public JsonSaveService(IStorageBackend backend)
        {
            _backend = backend;
        }

        public UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class
            => SaveAsync(typeof(T).Name, data, ct);

        public async UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class
        {
            ValidateKey(key);

            // Before the gate: a programmer error must not take the lock or fire OnSaveStarted.
            if (data == null)
                throw new ArgumentNullException(nameof(data),
                    $"Save '{key}': null data is not saveable — use DeleteAsync to remove a key.");

            var gate = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                _events.RaiseStarted(key);
                await _backend.WriteAsync(key, SaveEnvelopeCodec.Serialize(data, _settings), ct);
                _events.RaiseCompleted(key);
            }
            catch (OperationCanceledException ex)
            {
                // Cancellation is not a failure, but a UI bound to Started/Completed would hang
                // forever without a terminal event, so Failed carries the OCE as its Error.
                _events.RaiseFailed(key, ex);
                throw;
            }
            catch (Exception ex)
            {
                _events.RaiseFailed(key, ex);
                throw;
            }
            finally
            {
                gate.Release();
            }
        }

        public UniTask<T> LoadAsync<T>(CancellationToken ct = default) where T : class
            => LoadAsync<T>(typeof(T).Name, ct);

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            ValidateKey(key);
            var json = await _backend.ReadAsync(key, ct);
            if (json == null)
                return null; // genuinely no save yet — the only null-returning path

            try
            {
                return SaveEnvelopeCodec.Parse<T>(json, key, _settings);
            }
            catch (SaveSchemaVersionException)
            {
                // The backup is the player's OWN older progress. Loading it in place of a save
                // written by a newer build would destroy real data, so this is never recovered from.
                throw;
            }
            catch (Exception primaryEx) when (primaryEx is not OperationCanceledException)
            {
                var recovered = await TryRecoverFromBackupAsync<T>(key, ct);
                if (recovered != null)
                {
                    Debug.LogWarning($"[JsonSaveService] Primary save for '{key}' was corrupt, recovered from backup.");
                    return recovered;
                }

                ExceptionDispatchInfo.Capture(primaryEx).Throw();
                throw; // unreachable — satisfies the compiler's return-path analysis
            }
        }

        // Best-effort: null on any failure except cancellation, which propagates immediately so a
        // cancelled load isn't mistaken for "no usable backup."
        private async UniTask<T> TryRecoverFromBackupAsync<T>(string key, CancellationToken ct) where T : class
        {
            string backupJson;
            try
            {
                backupJson = await _backend.ReadBackupAsync(key, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }

            if (backupJson == null)
                return null;

            try
            {
                // Held to the same validity bar as the primary — a corrupt backup is not a recovery.
                return SaveEnvelopeCodec.Parse<T>(backupJson, key, _settings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        public UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class
            => ExistsAsync(typeof(T).Name, ct);

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            ValidateKey(key);
            return _backend.ExistsAsync(key, ct);
        }

        public UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class
            => DeleteAsync(typeof(T).Name, ct);

        public async UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            ValidateKey(key);
            var gate = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                return await _backend.DeleteAsync(key, ct);
            }
            finally
            {
                gate.Release();
            }
        }

        // Called by VContainer on scope teardown — it checks the concrete instance for IDisposable,
        // so ISaveService itself does not declare it. In-flight saves are deliberately NOT aborted:
        // they complete silently (SaveEventPublisher swallows post-dispose publishes), because
        // losing a player's last save at teardown is worse than losing a UI notification.
        //
        // _keyLocks is deliberately neither cleared nor disposed: clearing would hand the next
        // caller a fresh semaphore and lose mutual exclusion against a live write, and SemaphoreSlim
        // only needs disposal once AvailableWaitHandle has been allocated, which this never touches.
        public void Dispose() => _events.Dispose();

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !KeyPattern.IsMatch(key))
                throw new ArgumentException($"Save key '{key}' must match ^[A-Za-z0-9_-]+$.", nameof(key));
        }
    }
}
