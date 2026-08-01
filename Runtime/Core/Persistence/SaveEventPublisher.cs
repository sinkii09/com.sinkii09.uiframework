using System;
using R3;

namespace Sinkii09.UIFramework
{
    // Owns the three R3 save-lifecycle subjects and their disposal semantics. Split out of
    // JsonSaveService to keep that class under the 200-line limit, and to keep the
    // "publishing must stay safe after disposal" rule in one place instead of at three call sites.
    internal sealed class SaveEventPublisher : IDisposable
    {
        private readonly Subject<SaveEventArgs> _started = new();
        private readonly Subject<SaveEventArgs> _completed = new();
        private readonly Subject<SaveEventArgs> _failed = new();
        private volatile bool _disposed;

        internal Observable<SaveEventArgs> Started => _started;
        internal Observable<SaveEventArgs> Completed => _completed;
        internal Observable<SaveEventArgs> Failed => _failed;

        internal void RaiseStarted(string key) => Publish(_started, key, null);
        internal void RaiseCompleted(string key) => Publish(_completed, key, null);
        internal void RaiseFailed(string key, Exception error) => Publish(_failed, key, error);

        // After Dispose, R3's Subject.OnNext throws ObjectDisposedException — and an in-flight save
        // would then throw a SECOND time from inside its own catch block, masking the real error.
        // The _disposed check covers the common case; the catch covers the genuine race, because a
        // cancelled WriteAsync propagates on a thread-pool thread (UniTask.RunOnThreadPool throws
        // before yielding back to the player loop), so this can run off the main thread, where
        // check-then-act is not atomic no matter how the flag is declared.
        private void Publish(Subject<SaveEventArgs> subject, string key, Exception error)
        {
            if (_disposed)
                return;

            try
            {
                subject.OnNext(new SaveEventArgs(key, error));
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                // Disposed between the check and the publish. There is nothing left to report to.
                // The `when (_disposed)` filter matters: OnNext invokes subscribers synchronously,
                // and a subscriber throwing its own ObjectDisposedException (destroyed Unity object,
                // disposed CTS) must NOT be swallowed here and misattributed to a teardown race.
                // Dispose() sets _disposed before disposing the subjects, so the real race always
                // sees it true.
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _started.Dispose();
            _completed.Dispose();
            _failed.Dispose();
        }
    }
}
