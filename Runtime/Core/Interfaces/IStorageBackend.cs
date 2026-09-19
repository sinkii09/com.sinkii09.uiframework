using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Raw string payload storage keyed by string. The only swap seam in the persistence system —
    // serialization stays in ISaveService, storage location stays here. LocalFileStorageBackend is
    // the only implementation; a cloud/Steam backend plugs in later without touching save logic.
    //
    // Swapping one in needs no hook on UIFrameworkLifetimeScope: VContainer resolves duplicate
    // registrations last-wins, so registering your own after base.Configure(builder) is enough. It
    // must be the same scope that owns ISaveService, though — JsonSaveService is a root Singleton,
    // so a child-scope registration silently no-ops.
    public interface IStorageBackend
    {
        /// <summary>
        /// Writes <paramref name="contents"/> for <paramref name="key"/>, atomically.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Atomicity.</b> A concurrent read sees either the old complete payload or the new
        /// complete payload, never a partial write.
        /// </para>
        /// <para>
        /// <b>Durability.</b> Completing means the payload has been pushed as far as the platform
        /// lets this implementation push it — never merely buffered somewhere the implementation
        /// itself controls. Do not return early intending to flush later: every layer above treats a
        /// completed write as landed, and there is no FlushAsync to redeem the promise.
        /// </para>
        /// <para>
        /// Where a platform owns a further asynchronous step that no C# call can await, that residual
        /// window is the implementation's to document on its own type, not something to hide behind a
        /// completed task. <see cref="LocalFileStorageBackend"/> does exactly that for WebGL.
        /// </para>
        /// <para>
        /// <b>Backups.</b> Implementations that maintain one (e.g. LocalFileStorageBackend, via
        /// File.Replace's backup parameter) do so as a side effect of writing — ReadBackupAsync only
        /// reads it, it never independently creates one. A second backend must honour this
        /// write-creates / read-only-reads contract itself; the signatures alone do not enforce it.
        /// </para>
        /// </remarks>
        UniTask WriteAsync(string key, string contents, CancellationToken ct = default);
        UniTask<string> ReadAsync(string key, CancellationToken ct = default);
        UniTask<bool> ExistsAsync(string key, CancellationToken ct = default);
        UniTask<bool> DeleteAsync(string key, CancellationToken ct = default);

        // Best-effort recovery source — returns the previous payload for key, or null if no backup
        // exists yet (a key must have been saved at least twice before one is available).
        UniTask<string> ReadBackupAsync(string key, CancellationToken ct = default);
    }
}
