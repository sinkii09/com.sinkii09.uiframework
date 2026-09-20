using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The doing half of AutoSaveScheduler. See AutoSaveScheduler.cs for the deciding half.
    //
    // Every loop here copies the key list out of _sources before touching a source, because
    // Snapshot() is game code: a snapshot that lazily registers another key would otherwise mutate
    // the dictionary mid-enumeration and take the whole flush down with it.
    public sealed partial class AutoSaveScheduler
    {
        public async UniTask FlushAsync(CancellationToken ct = default)
        {
            var keys = new List<string>(_sources.Keys);
            List<UniTask> pending = null;

            for (var i = 0; i < keys.Count; i++)
            {
                if (!_sources.TryGetValue(keys[i], out var source)) continue;

                // An already-running write counts toward this flush: "everything has landed" is the
                // reason a caller reaches for this method before a scene load.
                if (source.Saving)
                    (pending ??= new List<UniTask>()).Add(source.InFlight);
                else if (source.Dirty)
                    (pending ??= new List<UniTask>()).Add(BeginSave(keys[i], source, ct));
            }

            if (pending != null)
                await UniTask.WhenAll(pending);
        }

        // The app-pause path. Synchronous top to bottom on purpose: OnApplicationPause cannot be
        // awaited, and the player loop stops when it returns, so anything left as a continuation may
        // never run.
        internal void FlushSynchronously()
        {
            if (_syncSaves == null)
            {
                // A substituted save service that cannot write synchronously. Firing the async save
                // is strictly better than doing nothing — on Android the process usually survives
                // long enough for a thread-pool write to land — but it is a race, so say so.
                Debug.LogWarning("[AutoSave] ISaveService cannot save synchronously; the pause flush " +
                                 "is a race against the OS. Implement ISynchronousSaveService to close it.");
                FlushAsync().Forget();
                return;
            }

            var keys = new List<string>(_sources.Keys);

            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!_sources.TryGetValue(key, out var source) || !source.Dirty) continue;

                // The WHOLE per-key body sits inside the try, snapshot included. A snapshot that
                // touches a destroyed Unity object throws, and letting that escape here abandons
                // every key after this one — while the app is being backgrounded, with no second
                // chance to notice.
                try
                {
                    var data = source.Snapshot();
                    if (data == null)
                    {
                        Debug.LogError($"[AutoSave] Snapshot for '{key}' returned null; not written.");
                        continue;
                    }

                    // A source that is already Saving is NOT skipped. Its in-flight write may carry
                    // older data, and TrySaveSync's zero-timeout gate probe settles it safely: it
                    // returns false while that write still holds the gate, and lands the newer state
                    // if it has already finished.
                    if (source.SaveSync(_syncSaves, data))
                        source.ClearDirty();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutoSave] Pause flush of '{key}' failed: {ex}");
                }
            }
        }

        private UniTask BeginSave(string key, AutoSaveSource source, CancellationToken ct)
        {
            object data;
            try
            {
                data = source.Snapshot();
            }
            catch (Exception ex)
            {
                source.Defer();
                Debug.LogError($"[AutoSave] Snapshot for '{key}' threw; not written, will retry: {ex}");
                return UniTask.CompletedTask;
            }

            if (data == null)
            {
                Debug.LogError($"[AutoSave] Snapshot for '{key}' returned null; not written, will retry.");
                source.Defer();
                return UniTask.CompletedTask;
            }

            source.ClearDirty();
            source.Saving = true;
            source.InFlight = RunSaveAsync(source, data, ct).Preserve();
            return source.InFlight;
        }

        private async UniTask RunSaveAsync(AutoSaveSource source, object data, CancellationToken ct)
        {
            try
            {
                await source.SaveAsync(_saves, data, ct);
            }
            catch
            {
                // Re-dirty rather than swallow. Without this a single transient write failure drops
                // the change permanently, and the next save writes state that never included it.
                source.Mark();
                throw;
            }
            finally
            {
                source.Saving = false;
            }
        }

        private static async UniTaskVoid LogIfFailed(string key, UniTask save)
        {
            try
            {
                await save;
            }
            catch (Exception ex)
            {
                // Tick has no caller to throw to. Logging beats UniTask's unobserved-exception
                // channel, which reports the failure without naming the key that lost data.
                Debug.LogError($"[AutoSave] Save of '{key}' failed: {ex}");
            }
        }
    }
}
