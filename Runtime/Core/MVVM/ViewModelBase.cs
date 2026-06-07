using R3;

namespace Sinkii09.UIFramework
{
    public abstract class ViewModelBase : IViewModel
    {
        // Lifetime bindings — disposed only when the ViewModel is discarded (scope.Dispose via VContainer).
        protected DisposableBag _disposables = new();

        // Per-show bindings — disposed + replaced on every OnHide() call.
        // DisposableBag has no Clear(); always Dispose() then reassign to reset.
        protected DisposableBag _showDisposables = new();

        private bool _disposed;

        public virtual void OnShow() { }

        public virtual void OnHide()
        {
            _showDisposables.Dispose();
            _showDisposables = new DisposableBag();
        }

        // Idempotent — safe if VContainer calls Dispose() after OnHide() already ran.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _showDisposables.Dispose();
            _disposables.Dispose();
        }
    }
}
