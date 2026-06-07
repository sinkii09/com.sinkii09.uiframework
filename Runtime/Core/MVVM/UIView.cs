using Cysharp.Threading.Tasks;
using System.Threading;
using R3;
using VContainer;

namespace Sinkii09.UIFramework
{
    public abstract class UIView<TViewModel> : UIViewBase
        where TViewModel : class, IViewModel
    {
        private TViewModel _viewModel;
        private IObjectResolver _viewScope;
        private bool _initialized;

        protected DisposableBag _showDisposables = new();

        protected TViewModel ViewModel => _viewModel;

        // Not an IUIView override — UIViewFactory (Phase 04) casts to UIView<TViewModel> to call this.
        // _initialized guard prevents double-init; reset in Cleanup() on pool return.
        public UniTask InitializeAsync(TViewModel vm, IObjectResolver scope, CancellationToken ct = default)
        {
            if (_initialized) return UniTask.CompletedTask;
            _viewModel = vm;
            _viewScope = scope;
            BindViewModel(vm);
            _initialized = true;
            return UniTask.CompletedTask;
        }

        // Override to set up R3 bindings; add show-time subscriptions to _showDisposables.
        // Called once per initialization cycle — re-called when UIViewFactory reuses a cached instance.
        protected abstract void BindViewModel(TViewModel vm);

        public override async UniTask ShowAsync(CancellationToken externalCt = default)
        {
            _viewModel?.OnShow();
            await base.ShowAsync(externalCt);
        }

        public override async UniTask HideAsync(CancellationToken externalCt = default)
        {
            if (_viewModel == null) return;
            await base.HideAsync(externalCt);
            // null-guard: pool-return race can null _viewModel while animator was awaited
            if (_viewModel == null) return;
            // Teardown AFTER animation so active bindings remain valid during the hide transition
            _viewModel.Show();
            _showDisposables.Dispose();
            _showDisposables = new DisposableBag();
        }

        internal override UniTask InitializeNonGenericAsync(IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            => InitializeAsync((TViewModel)viewModel, scope, ct);

        // Called by FactoryReset() when UIViewFactory is about to reuse this cached instance.
        // _viewScope.Dispose() triggers VContainer's IDisposable tracking, which disposes the ViewModel.
        // Do NOT call _viewModel.Dispose() directly — VContainer owns the ViewModel's lifetime.
        protected virtual void Cleanup()
        {
            _showDisposables.Dispose();
            _viewScope?.Dispose();
            _viewScope = null;
            _viewModel = null;
            _showDisposables = new DisposableBag();
            _initialized = false;
        }

        // UIViewFactory calls this before re-using a cached instance (rather than destroying + re-instantiating).
        // Performs a full reset including _showDisposables in case HideAsync was cancelled before its teardown ran.
        internal override void FactoryReset() => Cleanup();
    }
}
