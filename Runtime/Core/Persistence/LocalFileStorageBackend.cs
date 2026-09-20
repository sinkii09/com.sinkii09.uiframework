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
    // says belongs on the implementation. When WriteAsync completes, the bytes are in IDBFS; the
    // flush from IDBFS to IndexedDB is owned by the engine, asynchronous, and NOT awaitable from C#.
    // Measured: saves survived a forced browser kill on Chromium and a session boundary on iOS
    // Safari. Not proven: writing and closing the tab within about a second. A host page narrows the
    // window with `config.autoSyncPersistentDataPath = true` in createUnityInstance(), which syncs on
    // every file modification — not free, since one save here is 3-4 modifications (temp write, then
    // replace). Closing it outright needs a .jslib wrapper over Emscripten's FS.syncfs; that is a
    // deliberate, named extension point left unbuilt because nothing needs it yet. Note Unity has
    // deprecated the older JS_FileSystem_Sync(), so do not build on that one.
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
    public sealed class LocalFileStorageBackend : IStorageBackend, ISynchronousStorageBackend
    {
        private readonly string _rootDir;
        private readonly DateTime _constructedAtUtc;

        // Deliberately a runtime check and NOT #if UNITY_WEBGL. A compile-time branch is not compiled
        // during an Editor test run, so "both paths are tested" would be a silent lie — the exact
        // trap this sprint's review caught in the autosave policy design. Application.platform keeps
        // both paths compiled on every target and lets a test force either one.
        private readonly bool _useThreadPool;

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

        // Test seam: forces the dispatch mode so both paths run in a single Editor session.
        internal LocalFileStorageBackend(bool useThreadPool)
        {
            // Assigned FIRST: CleanupOrphanedTempFilesAsync below is itself a RunIo caller and would
            // otherwise dispatch on an unassigned flag.
            _useThreadPool = useThreadPool;

            // Application.persistentDataPath is a Unity API — must be read on the main thread.
            // Captured once here; all file I/O below is pure System.IO.
            _rootDir = Path.Combine(Application.persistentDataPath, "Saves");
            _constructedAtUtc = DateTime.UtcNow;
            CleanupOrphanedTempFilesAsync().Forget();
        }

        // Dispatch seam for every I/O path in this class. Thread pool where one exists; inline on
        // WebGL, where UniTask.RunOnThreadPool never resumes — it does not throw, it simply hangs
        // forever, which is why the tests assert on completion status rather than on "no exception".
        //
        // The token is checked BEFORE and AFTER the delegate, matching RunOnThreadPool's ordering
        // (UniTask.Run.cs:64-88 checks pre-switch, pre-delegate and post-delegate). Dropping the
        // post-check would make a token cancelled mid-write surface as success inline but as
        // OperationCanceledException on the thread pool — OnSaveCompleted vs OnSaveFailed for the
        // very same sequence of events.
        //
        // One DELIBERATE divergence, on the throwing path only. UniTask is inconsistent between its
        // own two overloads: the Action one puts the post-check after the try/finally (:87), so a
        // throwing delegate keeps its own exception, while the generic one puts it INSIDE the finally
        // (:192), so a cancelled token there replaces the real exception with an OCE. Both inline
        // overloads below follow the Action shape — the original I/O exception always wins — because
        // reproducing that asymmetry would trade a genuine error for a cancellation notice and make
        // the two overloads behave differently for no reason. Net effect: WriteAsync is exact parity;
        // the four value-returning ops differ only when a delegate throws AND the token is already
        // cancelled, where inline reports the real failure and the thread pool reports cancellation.
        //
        // Inline dispatch never yields to the player loop (the thread-pool path does, via
        // UniTask.Run.cs's `finally { await UniTask.Yield(); }`), so an await-retry loop built on top
        // of this would spin the WebGL main thread across zero frames.
        private UniTask RunIo(Action action, CancellationToken ct)
        {
            if (_useThreadPool)
                return UniTask.RunOnThreadPool(action, cancellationToken: ct);

            ct.ThrowIfCancellationRequested();
            action();
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        private UniTask<T> RunIo<T>(Func<T> func, CancellationToken ct)
        {
            if (_useThreadPool)
                return UniTask.RunOnThreadPool(func, cancellationToken: ct);

            ct.ThrowIfCancellationRequested();
            var result = func();
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(result);
        }

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
                var path = PathFor(key);
                var existed = File.Exists(path);
                if (existed)
                    File.Delete(path);

                var backupPath = BackupPathFor(key);
                if (File.Exists(backupPath))
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

                return existed;
            }, ct);
        }

        // Sweeps .tmp files left behind by a crash between WriteAllText and the replace/move step
        // in a PRIOR session. Runs once from the constructor. Safe by timestamp, not by
        // construction-order alone: a live write from THIS session always has a .tmp timestamp
        // at-or-after _constructedAtUtc (nothing can call WriteAsync before this backend exists), so
        // only strictly older files — provably from an already-ended session — are deleted. The 2s
        // margin absorbs filesystem timestamp rounding (e.g. FAT32).
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
