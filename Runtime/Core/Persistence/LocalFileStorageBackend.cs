using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The only IStorageBackend implementation in v1: writes JSON payloads to
    // Application.persistentDataPath/Saves/<key>.json. Writes are atomic (temp file + OS-level
    // rename) so a crash mid-write can never leave a truncated save on disk. Also maintains a
    // single rolling backup (<key>.json.bak) and sweeps orphaned .tmp files left by a crash in a
    // prior session.
    public sealed class LocalFileStorageBackend : IStorageBackend
    {
        private readonly string _rootDir;
        private readonly DateTime _constructedAtUtc;

        public LocalFileStorageBackend()
        {
            // Application.persistentDataPath is a Unity API — must be read on the main thread.
            // Captured once here; all file I/O below is pure System.IO run off-thread.
            _rootDir = Path.Combine(Application.persistentDataPath, "Saves");
            _constructedAtUtc = DateTime.UtcNow;
            CleanupOrphanedTempFilesAsync().Forget();
        }

        public async UniTask WriteAsync(string key, string contents, CancellationToken ct = default)
        {
            await UniTask.RunOnThreadPool(() =>
            {
                ct.ThrowIfCancellationRequested();
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
            }, cancellationToken: ct);
        }

        public async UniTask<string> ReadAsync(string key, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                ct.ThrowIfCancellationRequested();
                var path = PathFor(key);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, cancellationToken: ct);
        }

        public async UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                ct.ThrowIfCancellationRequested();
                var path = BackupPathFor(key);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, cancellationToken: ct);
        }

        public async UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                ct.ThrowIfCancellationRequested();
                return File.Exists(PathFor(key));
            }, cancellationToken: ct);
        }

        public async UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
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
            }, cancellationToken: ct);
        }

        // Sweeps .tmp files left behind by a crash between WriteAllText and the replace/move step
        // in a PRIOR session. Runs once, fire-and-forget, from the constructor. Safe by timestamp,
        // not by construction-order alone: a live write from THIS session always has a .tmp
        // timestamp at-or-after _constructedAtUtc (nothing can call WriteAsync before this backend
        // exists), so only strictly older files — provably from an already-ended session — are
        // deleted. The 2s margin absorbs filesystem timestamp rounding (e.g. FAT32).
        private async UniTask CleanupOrphanedTempFilesAsync()
        {
            try
            {
                await UniTask.RunOnThreadPool(() =>
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
                });
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
