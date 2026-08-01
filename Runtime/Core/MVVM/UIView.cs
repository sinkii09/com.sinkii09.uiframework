using Cysharp.Threading.Tasks;
using System;
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

        // Not an IUIView override — InitializeNonGenericAsync's guarded cast below calls this once
        // it has confirmed the incoming IViewModel is actually a TViewModel.
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
            try
            {
                await base.ShowAsync(externalCt);
            }
            catch (OperationCanceledException)
            {
                // OnShow() already ran; without this the per-show bindings from OnShow leak until
                // the next FactoryReset. NotifyHide is guarded and idempotent (ViewModelBase).
                _viewModel?.NotifyHide();
                throw;
            }
        }

        public override async UniTask HideAsync(CancellationToken externalCt = default)
        {
            // Log programmer error but do NOT early-return — base.HideAsync must always run
            // so that IsVisible and SetActive(false) are set correctly regardless of VM state.
            if (_viewModel == null)
                UnityEngine.Debug.LogError($"[UIView] HideAsync called on uninitialized view {GetType().Name}. Ensure InitializeAsync ran before HideAsync.");
            await base.HideAsync(externalCt);
            // null-guard: FactoryReset can null _viewModel while animator was awaited (pool-return race)
            if (_viewModel == null) return;
            // Teardown AFTER animation so active bindings remain valid during the hide transition
            _viewModel.NotifyHide();
            _showDisposables.Dispose();
            _showDisposables = new DisposableBag();
        }

        internal override UniTask InitializeNonGenericAsync(IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
        {
            if (viewModel is not TViewModel typed)
                throw new InvalidCastException(
                    $"[UIView] {GetType().Name} expects {typeof(TViewModel).Name}, got {viewModel?.GetType().Name ?? "null"}.");
            return InitializeAsync(typed, scope, ct);
        }

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
