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

        protected DisposableBag _showDisposables;

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

        // Override to set up R3 bindings; add subscriptions to _showDisposables.
        // Called once per view lifetime — BindViewModel is not called again on re-show.
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
            _viewModel.OnHide();
            _showDisposables.Dispose();
            _showDisposables = new DisposableBag();
        }

        internal override UniTask InitializeNonGenericAsync(IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            => InitializeAsync((TViewModel)viewModel, scope, ct);

        // Called by UIViewFactory on pool return (Phase 04 wires IPoolable.OnReturnedToPool → Cleanup).
        // _viewScope.Dispose() triggers VContainer's IDisposable tracking, which disposes the ViewModel.
        // Do NOT call _viewModel.Dispose() directly — VContainer owns the ViewModel's lifetime.
        protected virtual void Cleanup()
        {
            _showDisposables.Dispose();
            _viewScope?.Dispose();
            _viewScope = null;
            _showDisposables = new DisposableBag();
            _initialized = false;
        }
    }
}
