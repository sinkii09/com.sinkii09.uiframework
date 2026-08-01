using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Raw string payload storage keyed by string. The only swap seam in the persistence system —
    // serialization stays in ISaveService, storage location stays here. LocalFileStorageBackend is
    // the only implementation in v1; a cloud/Steam backend plugs in later without touching save logic.
    public interface IStorageBackend
    {
        // Must be atomic: a concurrent read sees either the old complete payload or the new
        // complete payload, never a partial write. Implementations that maintain a backup (e.g.
        // LocalFileStorageBackend, via File.Replace's backup parameter) do so as a side effect of
        // WriteAsync — ReadBackupAsync only reads it, it doesn't independently create one. A future
        // second backend must honor this write-creates/read-only-reads contract itself; it isn't
        // enforced by these signatures alone.
        UniTask WriteAsync(string key, string contents, CancellationToken ct = default);
        UniTask<string> ReadAsync(string key, CancellationToken ct = default);
        UniTask<bool> ExistsAsync(string key, CancellationToken ct = default);
        UniTask<bool> DeleteAsync(string key, CancellationToken ct = default);

        // Best-effort recovery source — returns the previous payload for key, or null if no backup
        // exists yet (a key must have been saved at least twice before one is available).
        UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default);
    }
}
