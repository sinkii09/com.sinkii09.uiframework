using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using R3;
using VContainer;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // ISaveService implementation: orchestration only — per-key re-entrancy guard, R3 save-lifecycle
    // events, slot resolution and backup-recovery policy. Serialization lives in SaveEnvelopeCodec,
    // raw storage in IStorageBackend, key rules in SaveKeyResolver.
    //
    // TWO KEYS FLOW THROUGH THIS CLASS AND THEY ARE NOT INTERCHANGEABLE:
    //
    //   logical key  — what the game asked for. Goes to SaveEnvelopeCodec (and therefore to
    //                  SaveMigrationRegistry), to the R3 events, and into exception messages.
    //   storage key  — the logical key decorated with the active slot. Goes to IStorageBackend and
    //                  to the per-key gate, and nowhere else.
    //
    // Handing the storage key to the codec would be a silent disaster: migration chains are registered
    // against the key the GAME knows, so "PlayerData.s1" would find no chain, and every slot above 0
    // would sit at the base schema version forever — never migrating, and never able to raise
    // SaveSchemaVersionException when a newer build wrote it.
    public sealed class JsonSaveService : ISaveService, IDisposable
    {
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
        private readonly SaveMigrationRegistry _migrations;
        private readonly ISaveSlotContext _slots;

        // Shares _keyLocks by reference, so repair takes the same gate an ordinary save takes.
        private readonly SaveBackupRecovery _recovery;

        public Observable<SaveEventArgs> OnSaveStartedAsObservable => _events.Started;
        public Observable<SaveEventArgs> OnSaveCompletedAsObservable => _events.Completed;
        public Observable<SaveEventArgs> OnSaveFailedAsObservable => _events.Failed;

        // Deliberately NOT on ISaveService. Adding a member to a public interface breaks every
        // implementer — there is one in this repo already — which would force a major version for a
        // feature nobody has asked for yet. The scope registers this class AsSelf so a game can opt in
        // by resolving the concrete type. Promoting it to the interface is a v4.0.0 decision.
        public Observable<SaveRecoveredEventArgs> OnSaveRecoveredAsObservable => _events.Recovered;

        // Kept so existing callers still compile — this is public API of a published package, and
        // dropping it would be a breaking change for a release that is otherwise purely additive.
        // Pins slot 0 and registers no migrations, i.e. exactly the behaviour that shipped before
        // either feature existed.
        public JsonSaveService(IStorageBackend backend)
            : this(backend, SaveMigrationRegistry.Empty, new SaveSlotContext())
        {
        }

        // [Inject] is MANDATORY, not decoration. VContainer would pick this one anyway as the
        // greediest constructor (TypeAnalyzer.cs:227-244), but leaving that implicit is exactly what
        // silently broke DI earlier in this sprint while every test stayed green. Marking it also
        // means adding a further constructor cannot quietly steal the injection point.
        [Inject]
        public JsonSaveService(IStorageBackend backend, SaveMigrationRegistry migrations, ISaveSlotContext slots)
        {
            _backend = backend;
            _migrations = migrations ?? SaveMigrationRegistry.Empty;
            _slots = slots ?? new SaveSlotContext();
            _recovery = new SaveBackupRecovery(_backend, _migrations, _settings, _keyLocks, _events);
        }

        // Reads the active slot EXACTLY ONCE per operation. Reading it twice could decorate the gate
        // key and the storage key differently if a game switched slots mid-call, which is precisely
        // how mutual exclusion gets lost. The resolved pair is threaded through everything that
        // follows, including the repair path that runs after an await.
        private (string Logical, string Storage) ResolveKey(string key)
        {
            SaveKeyResolver.ValidateKey(key);
            return (key, SaveKeyResolver.StorageKeyFor(key, _slots.ActiveSlot));
        }

        public UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class
            => SaveAsync(typeof(T).Name, data, ct);

        public async UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class
        {
            var (logical, storage) = ResolveKey(key);

            // Before the gate: a programmer error must not take the lock or fire OnSaveStarted.
            if (data == null)
                throw new ArgumentNullException(nameof(data),
                    $"Save '{logical}': null data is not saveable — use DeleteAsync to remove a key.");

            var gate = _keyLocks.GetOrAdd(storage, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                _events.RaiseStarted(logical);
                await _backend.WriteAsync(storage, SaveEnvelopeCodec.Serialize(data, logical, _migrations, _settings), ct);
                _events.RaiseCompleted(logical);
            }
            catch (OperationCanceledException ex)
            {
                // Cancellation is not a failure, but a UI bound to Started/Completed would hang
                // forever without a terminal event, so Failed carries the OCE as its Error.
                _events.RaiseFailed(logical, ex);
                throw;
            }
            catch (Exception ex)
            {
                _events.RaiseFailed(logical, ex);
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
            var (logical, storage) = ResolveKey(key);
            var json = await _backend.ReadAsync(storage, ct);
            if (json == null)
                return null; // genuinely no save yet — the only null-returning path

            try
            {
                return SaveEnvelopeCodec.Parse<T>(json, logical, _migrations, _settings);
            }
            catch (SaveSchemaVersionException)
            {
                // The backup is the player's OWN older progress. Loading it in place of a save
                // written by a newer build would destroy real data, so this is never recovered from.
                throw;
            }
            catch (SaveMigrationException)
            {
                // A failed chain usually means the CODE is wrong rather than the data, and recovering
                // then would restore an older save over an intact one. But "the chain broke it" and
                // "it was already broken and the chain merely ran over the damage" produce the same
                // symptom, so refusing recovery outright would strand a genuinely corrupt save that
                // has a perfectly good backup sitting right next to it.
                //
                // The backup settles it. It is at most one save older, therefore at the same schema
                // version, therefore it goes through the SAME chain: if the chain is the problem the
                // backup fails identically and recovery yields null. A successful recovery here is
                // positive evidence that only the primary was damaged.
                //
                // Either way this never falls through to the generic handler below — on failure the
                // migration exception is rethrown so the consumer can fail closed.
                var afterMigrationFailure = await _recovery.RecoverAndRepairAsync<T>(logical, storage, json, ct);
                if (afterMigrationFailure != null)
                    return afterMigrationFailure;

                throw;
            }
            catch (Exception primaryEx) when (primaryEx is not OperationCanceledException)
            {
                var recovered = await _recovery.RecoverAndRepairAsync<T>(logical, storage, json, ct);
                if (recovered != null)
                    return recovered;

                ExceptionDispatchInfo.Capture(primaryEx).Throw();
                throw; // unreachable — satisfies the compiler's return-path analysis
            }
        }

        public UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class
            => ExistsAsync(typeof(T).Name, ct);

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
            => _backend.ExistsAsync(ResolveKey(key).Storage, ct);

        public UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class
            => DeleteAsync(typeof(T).Name, ct);

        public async UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            var (_, storage) = ResolveKey(key);
            var gate = _keyLocks.GetOrAdd(storage, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                return await _backend.DeleteAsync(storage, ct);
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
        // Slot decoration grows that dictionary to {keys} x {slots}, which is why SaveKeyResolver
        // bounds the slot index.
        public void Dispose() => _events.Dispose();
    }
}
