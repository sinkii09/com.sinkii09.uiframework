using System;
using System.Collections.Concurrent;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Backup-recovery and repair policy, split out of JsonSaveService so that class keeps only
    // orchestration. This is a genuinely separate concern — "what to do when the primary is
    // unreadable" — and it carries most of the rules that were review findings rather than design.
    //
    // It shares JsonSaveService's per-key gate dictionary BY REFERENCE, not a copy. Repair takes the
    // same lock an ordinary save takes; two dictionaries would mean two lock spaces and no mutual
    // exclusion at all, which is the failure mode this whole class exists to avoid.
    internal sealed class SaveBackupRecovery
    {
        private readonly IStorageBackend _backend;
        private readonly SaveMigrationRegistry _migrations;
        private readonly JsonSerializerSettings _settings;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks;
        private readonly SaveEventPublisher _events;

        internal SaveBackupRecovery(
            IStorageBackend backend,
            SaveMigrationRegistry migrations,
            JsonSerializerSettings settings,
            ConcurrentDictionary<string, SemaphoreSlim> keyLocks,
            SaveEventPublisher events)
        {
            _backend = backend;
            _migrations = migrations;
            _settings = settings;
            _keyLocks = keyLocks;
            _events = events;
        }

        // Recovers from the backup and, on success, writes the recovered bytes back over the damaged
        // primary. Without that write-back the primary stays corrupt, and the next ordinary save
        // rotates it into the backup slot (LocalFileStorageBackend's File.Replace), destroying the
        // last good copy — recovery would buy one session and then throw the lifeline away.
        //
        // Returns null when there is nothing usable to recover, which the caller treats as "the
        // original failure stands".
        internal async UniTask<T> RecoverAndRepairAsync<T>(
            string logical, string storage, string corruptJson, CancellationToken ct) where T : class
        {
            var (value, backupJson) = await TryRecoverAsync<T>(logical, storage, ct);
            if (value == null)
                return null;

            Debug.LogWarning($"[JsonSaveService] Primary save for '{logical}' was unreadable, recovered from backup.");

            var repaired = await TryRepairPrimaryAsync(logical, storage, corruptJson, backupJson, ct);

            // Only OnSaveRecovered. A "Saving…" indicator lighting up in the middle of a LOAD would be
            // a lie, so the save-lifecycle events stay out of this path entirely.
            _events.RaiseRecovered(logical, repaired);
            return value;
        }

        // Best-effort, and failure here must NEVER fail the load: by this point a valid value is
        // already in hand, and throwing would reach a consumer that reads it as "corrupt, start
        // fresh", re-enables saving, and overwrites real progress on the next write.
        //
        // POST-REPAIR STATE, stated precisely because it is easy to assume otherwise: the backend
        // writes via File.Replace(tmp, final, backup), so this write rotates the CORRUPT primary into
        // the backup slot. Afterwards primary = good, backup = corrupt, and it stays that way until
        // the next ordinary save rotates the good copy into the backup and heals it.
        //
        // Accepted risk, deliberately taken: between this write and that next save there is exactly
        // one good copy instead of two, whereas doing nothing leaves the good copy in the backup and
        // the corrupt one in the primary. Doing nothing is worse — the very next ordinary save would
        // rotate the corrupt primary into the backup and destroy the last good copy outright.
        private async UniTask<bool> TryRepairPrimaryAsync(
            string logical, string storage, string corruptJson, string backupJson, CancellationToken ct)
        {
            // Takes the gate ITSELF and writes through the backend. Calling the public SaveAsync from
            // here would re-enter a non-reentrant SemaphoreSlim on the same key and deadlock forever —
            // LoadAsync deliberately holds no gate, so this is the first and only acquisition.
            var gate = _keyLocks.GetOrAdd(storage, _ => new SemaphoreSlim(1, 1));
            try
            {
                await gate.WaitAsync(ct);
            }
            catch (Exception ex)
            {
                // Catches everything, not just cancellation. The gate was never acquired, so there is
                // nothing to release — and this runs inside a catch block in LoadAsync, where an
                // escaping exception would replace the original one and fail a load whose value has
                // already been recovered. That directly contradicts this method's contract.
                Debug.LogWarning($"[JsonSaveService] Could not acquire the lock to repair '{logical}', " +
                                 $"leaving the primary damaged: {ex.Message}");
                return false;
            }

            try
            {
                // Lost-update guard. A save that raced this load and finished first would otherwise be
                // overwritten with the older backup payload — a data loss that does not exist today.
                // Ordinal comparison on purpose: the question is "did anyone touch this file", not
                // whether two payloads mean the same thing.
                var current = await _backend.ReadAsync(storage, ct);

                if (current == null)
                {
                    // A racing DeleteAsync removed primary AND backup. Reading "the file is gone" as
                    // "so put it back" would resurrect a save the player deliberately deleted.
                    Debug.LogWarning($"[JsonSaveService] Skipped repairing '{logical}': it was deleted while loading.");
                    return false;
                }

                if (!string.Equals(current, corruptJson, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[JsonSaveService] Skipped repairing '{logical}': another save wrote it first.");
                    return false;
                }

                // Writes the backup's RAW bytes, not a re-serialization of the recovered object. A
                // round-trip through T is lossy by documented design — unknown fields are dropped
                // (MissingMemberHandling.Ignore) and polymorphic payloads do not survive — so
                // re-serializing would quietly repair the file into something smaller than what was
                // recovered.
                await _backend.WriteAsync(storage, backupJson, ct);
                return true;
            }
            catch (Exception ex)
            {
                // Includes cancellation, deliberately. See the method comment: nothing here may fail
                // the load.
                Debug.LogWarning($"[JsonSaveService] Could not repair the primary save for '{logical}', " +
                                 $"leaving it damaged for the next ordinary save to heal: {ex.Message}");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        // Returns the recovered value together with the exact bytes it came from, because repair must
        // write those bytes back rather than a re-serialization. Null on any failure except
        // cancellation, which propagates so a cancelled load is not mistaken for "no usable backup".
        private async UniTask<(T Value, string Json)> TryRecoverAsync<T>(
            string logical, string storage, CancellationToken ct) where T : class
        {
            string backupJson;
            try
            {
                backupJson = await _backend.ReadBackupAsync(storage, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (null, null);
            }

            if (backupJson == null)
                return (null, null);

            try
            {
                // Held to the same validity bar as the primary — a corrupt backup is not a recovery.
                // Note this migrates the backup too, which is exactly what lets a failed primary
                // migration be told apart from a broken chain (see JsonSaveService.LoadAsync).
                return (SaveEnvelopeCodec.Parse<T>(backupJson, logical, _migrations, _settings), backupJson);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SaveMigrationException ex)
            {
                // Logged rather than swallowed silently: a chain that breaks on the backup as well is
                // the strongest signal the chain itself is at fault, and it would otherwise leave no
                // trace anywhere.
                Debug.LogWarning($"[JsonSaveService] Backup for '{logical}' also failed to migrate, so the chain is the " +
                                 $"likely culprit rather than the data: {ex.Message}");
                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
