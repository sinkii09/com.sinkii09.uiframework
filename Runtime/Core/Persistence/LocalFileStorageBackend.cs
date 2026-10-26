using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // The only IStorageBackend implementation: writes JSON payloads to
    // Application.persistentDataPath/Saves/<key>.json. Writes are atomic (temp file + OS-level
    // rename) so a crash mid-write can never leave a truncated save on disk. Also maintains a
    // single rolling backup (<key>.json.bak) and sweeps orphaned .tmp files left by a crash in a
    // prior session.
    //
    // WebGL: this class IS the WebGL backend — there is no second implementation. File.Replace,
    // File.Move and Directory.GetFiles were all measured working on IDBFS (Chromium and iOS Safari,
    // including private mode). The one thing that does not work there is the thread pool, which is
    // what RunIo exists to handle.
    //
    // WebGL durability, the honest version — this is the residual window IStorageBackend.WriteAsync
    // says belongs on the implementation. When WriteAsync completes, the bytes are in IDBFS (memory).
    // Every successful write or delete then REQUESTS an FS.syncfs push to IndexedDB through
    // WebGlBrowserBridge (v3.11.0), coalesced in the jslib and deliberately not awaited — see
    // RequestFlushIfInline. So a completed save has been handed to IndexedDB, and the remaining
    // window is that IndexedDB transaction itself, which no page can close. Page hide also flushes,
    // after AutoSaveScheduler's synchronous save. Hosts should leave
    // `config.autoSyncPersistentDataPath` off now: it syncs on every one of a save's 3-4 file
    // modifications and races this flush. Unity has deprecated JS_FileSystem_Sync(); do not use it.
    //
    // Substituting a backend needs no virtual hook: VContainer resolves duplicate registrations
    // last-wins (Registry.cs:78-81), so a scope that calls base.Configure(builder) and then registers
    // its own IStorageBackend wins, and this class is never constructed. Two caveats:
    //   - Override in the SAME scope that owns ISaveService. JsonSaveService is a root Singleton
    //     (UIFrameworkLifetimeScope.cs:206-207); a child-scope registration does not change what it
    //     resolves, so the override silently no-ops AND this class gets constructed anyway.
    //   - Resolving IEnumerable<IStorageBackend> constructs every registration, not just the winner.
    //
    // WebGL knock-on worth knowing before writing a save-event subscriber: because inline dispatch
    // makes every operation here complete synchronously, JsonSaveService.SaveAsync runs end to end on
    // the caller's stack, so OnSaveStarted and OnSaveCompleted are raised back to back inside the
    // per-key gate with no frame between them. Two consequences, both WebGL-only: a "Saving…"
    // indicator bound to those events never gets a frame to render, and an OnSaveCompleted subscriber
    // that saves a DIFFERENT key recurses synchronously — a two-key save cycle that merely spreads
    // across frames elsewhere is a stack overflow here. Same-key re-entry is safe: it suspends on the
    // semaphore and the outer call releases it.
    public sealed partial class LocalFileStorageBackend : IStorageBackend, ISynchronousStorageBackend
    {
        private readonly string _rootDir;
        private readonly DateTime _constructedAtUtc;

        // Deliberately a runtime check and NOT #if UNITY_WEBGL. A compile-time branch is not compiled
        // during an Editor test run, so "both paths are tested" would be a silent lie — the exact
        // trap this sprint's review caught in the autosave policy design. Application.platform keeps
        // both paths compiled on every target and lets a test force either one.
        private readonly bool _useThreadPool;
        private readonly Action _requestStorageFlush;

        // [Inject] is MANDATORY here, not decoration. VContainer's TypeAnalyzer enumerates
        // constructors with BindingFlags.NonPublic and, absent an [Inject] marker, picks the one with
        // the MOST parameters (TypeAnalyzer.cs:227-244). The internal test ctor below therefore wins
        // that election, and the container tries to resolve a System.Boolean that nothing registers —
        // a VContainerException on every resolve of ISaveService, while every unit test stays green
        // because they all construct with `new`. Do not remove this attribute, and do not add another
        // parameterised constructor of ANY accessibility without checking the same trap.
        [Inject]
        public LocalFileStorageBackend()
            : this(Application.platform != RuntimePlatform.WebGLPlayer)
        {
        }

        // Test seam: forces the dispatch mode so both paths run in a single Editor session, and lets a
        // test observe flush requests, which are a no-op outside a WebGL player.
        internal LocalFileStorageBackend(bool useThreadPool, Action requestStorageFlush = null)
        {
            // Assigned FIRST: CleanupOrphanedTempFilesAsync below is itself a RunIo caller and would
            // otherwise dispatch on an unassigned flag.
            _useThreadPool = useThreadPool;
            _requestStorageFlush = requestStorageFlush ?? WebGlBrowserBridge.RequestStorageFlush;

            // Application.persistentDataPath is a Unity API — must be read on the main thread.
            // Captured once here; all file I/O below is pure System.IO.
            _rootDir = Path.Combine(Application.persistentDataPath, "Saves");
            _constructedAtUtc = DateTime.UtcNow;
            CleanupOrphanedTempFilesAsync().Forget();
        }

        // RunIo, the dispatch seam every I/O path below goes through, lives in
        // LocalFileStorageBackend.Dispatch.cs.

        public async UniTask WriteAsync(string key, string contents, CancellationToken ct = default)
        {
            await RunIo(() =>
            {
                ct.ThrowIfCancellationRequested();
                WriteCore(key, contents);
            }, ct);
        }

        // ISynchronousStorageBackend. The one moment awaiting is impossible: OnApplicationPause.
        //
        // Runs the SAME bytes-to-disk path as WriteAsync — deliberately the same method, not a copy —
        // minus the dispatch and the token. That the write is atomic is what makes this safe to be
        // interrupted: an OS kill part-way through leaves the previous save whole rather than torn.
        public void WriteSync(string key, string contents) => WriteCore(key, contents);

        private void WriteCore(string key, string contents)
        {
            Directory.CreateDirectory(_rootDir);
            var finalPath = PathFor(key);
            var tempPath = finalPath + ".tmp";
            File.WriteAllText(tempPath, contents);

            // File.Replace requires the destination to exist; File.Move (2-arg) requires it not
            // to. Branch covers first-save and every subsequent save atomically either way.
            // ignoreMetadataErrors tolerates attribute/ACL-merge failures on the backup file
            // without failing the primary replace — it does NOT swallow a genuine lock/IOException
            // on the backup (e.g. AV/indexer holding it open); that residual risk is accepted,
            // same treatment as the read/replace sharing-violation risk below.
            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, BackupPathFor(key), ignoreMetadataErrors: true);
            else
                File.Move(tempPath, finalPath);

            RequestFlushIfInline();
        }

        public async UniTask<string> ReadAsync(string key, CancellationToken ct = default)
        {
            return await RunIo(() =>
            {
                ct.ThrowIfCancellationRequested();
                var path = PathFor(key);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, ct);
        }

        public async UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default)
        {
            return await RunIo(() =>
            {
                ct.ThrowIfCancellationRequested();
                var path = BackupPathFor(key);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, ct);
        }

        public async UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return await RunIo(() =>
            {
                ct.ThrowIfCancellationRequested();
                return File.Exists(PathFor(key));
            }, ct);
        }

        public async UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return await RunIo(() =>
            {
                ct.ThrowIfCancellationRequested();
                return DeleteCore(key);
            }, ct);
        }

        private bool DeleteCore(string key)
        {
            var path = PathFor(key);
            var existed = File.Exists(path);
            if (existed)
                File.Delete(path);

            var backupPath = BackupPathFor(key);
            var backupExisted = File.Exists(backupPath);
            if (backupExisted)
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LocalFileStorageBackend] Failed to delete backup for '{key}': {ex.Message}");
                }
            }

            // A delete must reach IndexedDB too: otherwise "start fresh" can be undone by the browser
            // restoring the old store on the next load.
            if (existed || backupExisted)
                RequestFlushIfInline();

            return existed;
        }

        // Sweeps .tmp files left behind by a crash between WriteAllText and the replace/move step
        // in a PRIOR session. Runs once from the constructor. Safe by timestamp, not by
        // construction-order alone: a live write from THIS session always has a .tmp timestamp
        // at-or-after _constructedAtUtc (nothing can call WriteAsync before this backend exists), so
        // only strictly older files — provably from an already-ended session — are deleted. The 2s
        // margin absorbs filesystem timestamp rounding (e.g. FAT32).
        //
        // Requests no storage flush: an orphan .tmp left in IndexedDB is swept again next session, and
        // the next real write's flush pushes this deletion along with it.
        //
        // Fire-and-forget on the thread-pool path only. Under inline dispatch it runs to completion
        // synchronously inside the constructor — i.e. during VContainer's container build — so the
        // .Forget() below is a no-op there.
        private async UniTask CleanupOrphanedTempFilesAsync()
        {
            try
            {
                await RunIo(() =>
                {
                    if (!Directory.Exists(_rootDir))
                        return;

                    var cutoff = _constructedAtUtc - TimeSpan.FromSeconds(2);
                    foreach (var tempFile in Directory.GetFiles(_rootDir, "*.tmp"))
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(tempFile) < cutoff)
                                File.Delete(tempFile);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[LocalFileStorageBackend] Failed to clean up orphaned temp file '{tempFile}': {ex.Message}");
                        }
                    }
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFileStorageBackend] Orphaned temp file cleanup failed: {ex.Message}");
            }
        }

        private string PathFor(string key) => Path.Combine(_rootDir, key + ".json");
        private string BackupPathFor(string key) => Path.Combine(_rootDir, key + ".json.bak");
    }
}
