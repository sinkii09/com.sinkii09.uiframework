using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // The dispatch half of LocalFileStorageBackend: where each I/O delegate runs. See
    // LocalFileStorageBackend.cs for what the delegates do.
    public sealed partial class LocalFileStorageBackend
    {
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
    }
}
