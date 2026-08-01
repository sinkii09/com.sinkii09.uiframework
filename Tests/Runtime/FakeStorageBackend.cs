using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework.Tests
{
    // In-memory IStorageBackend used to drive JsonSaveService's error handling directly: corrupt
    // payloads, missing backups and write failures are all awkward to force through real files.
    // The real disk implementation is covered separately by LocalFileStorageBackendTests.
    internal sealed class FakeStorageBackend : IStorageBackend
    {
        private readonly Dictionary<string, string> _primary = new();
        private readonly Dictionary<string, string> _backup = new();

        // Set to make the next WriteAsync/ReadBackupAsync fail with this exact exception.
        internal Exception WriteError { get; set; }
        internal Exception ReadBackupError { get; set; }

        internal void SeedPrimary(string key, string json) => _primary[key] = json;
        internal void SeedBackup(string key, string json) => _backup[key] = json;
        internal string PeekPrimary(string key) => _primary.TryGetValue(key, out var v) ? v : null;

        // Mirrors LocalFileStorageBackend's contract: a write rotates the previous payload into the
        // backup slot, so "saved twice" produces a recoverable backup here too.
        public UniTask WriteAsync(string key, string contents, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (WriteError != null)
                throw WriteError;

            if (_primary.TryGetValue(key, out var previous))
                _backup[key] = previous;

            _primary[key] = contents;
            return UniTask.CompletedTask;
        }

        public UniTask<string> ReadAsync(string key, CancellationToken ct = default)
            => UniTask.FromResult(_primary.TryGetValue(key, out var v) ? v : null);

        public UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default)
        {
            if (ReadBackupError != null)
                throw ReadBackupError;

            return UniTask.FromResult(_backup.TryGetValue(key, out var v) ? v : null);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
            => UniTask.FromResult(_primary.ContainsKey(key));

        public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            var existed = _primary.Remove(key);
            _backup.Remove(key);
            return UniTask.FromResult(existed);
        }
    }

    // Plain POCO matching the documented usage model. The Dictionary field is deliberate: it is the
    // reason this framework uses Newtonsoft rather than JsonUtility, so a test must prove it survives.
    internal sealed class TestSaveData
    {
        public string Name { get; set; }
        public int Score { get; set; }
        public Dictionary<string, int> Inventory { get; set; } = new();
    }
}
