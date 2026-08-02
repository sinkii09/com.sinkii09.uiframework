using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Top-level game orchestrator. Registered as VContainer entry point (IAsyncStartable).
    // Framework registers BootState and LoadingState automatically.
    // Game developer registers game-specific states via RegisterState<T>() from an IInitializable bootstrap.
    public sealed class GameLifecycleManager : IAsyncStartable
    {
        private readonly IUIStateMachine _stateMachine;
        private readonly UINavigator _navigator; // concrete: ChangeStateAsync is internal
        private readonly ITransitionOverlay _overlay;
        private readonly ILoadingContext _loadingContext;
        private readonly CancellationToken _exitToken;
        private bool _isTransitioning;

        [Inject]
        public GameLifecycleManager(
            IUIStateMachine stateMachine,
            UINavigator navigator,
            ITransitionOverlay overlay,
            ILoadingContext loadingContext,
            BootState bootState,
            LoadingState loadingState)
        {
            _stateMachine = stateMachine;
            _navigator = navigator;
            _overlay = overlay;
            _loadingContext = loadingContext;
            _exitToken = Application.exitCancellationToken;
            stateMachine.RegisterState(bootState);
            stateMachine.RegisterState(loadingState);
        }

        // Shows the overlay, awaits it, and swallows any failure — the overlay is decorative and
        // must never block or fail a state transition. Hide is the caller's responsibility.
        private async UniTask ShowOverlaySafeAsync(CancellationToken ct)
        {
            try
            {
                await _overlay.ShowAsync(ct);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLifecycleManager] overlay show failed: {e}");
            }
        }

        // CancellationToken.None so a cancelled/faulted transition still tears the overlay down.
        // UIViewBase links destroyCancellationToken internally, so app-shutdown still stops it.
        private async UniTask HideOverlaySafeAsync()
        {
            try
            {
                await _overlay.HideAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLifecycleManager] overlay hide failed: {e}");
            }
        }

        // Called by game developer's bootstrap IInitializable.Initialize() to register
        // game-specific states (GameplayState, PauseState, etc.) before StartAsync runs.
        public void RegisterState<T>(T state) where T : IGameState
            => _stateMachine.RegisterState(state);

        public async UniTask StartAsync(CancellationToken ct)
        {
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await _navigator.ChangeStateAsync<BootState>(cts.Token);
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        // ct = default means only Application.exitCancellationToken cancels this transition.
        public async UniTask ChangeStateAsync<T>(CancellationToken ct = default) where T : IGameState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameLifecycleManager] Transitioning — ChangeStateAsync<{typeof(T).Name}> ignored.");
                return;
            }
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                await _navigator.ChangeStateAsync<T>(cts.Token);
            }
            finally
            {
                await HideOverlaySafeAsync();
                _isTransitioning = false;
            }
        }

        // Loads a scene via LoadingState, then transitions directly into TNext — both steps under
        // one overlay/guard window. Do NOT chain ChangeStateAsync<LoadingState> +
        // ChangeStateAsync<TNext> yourself from inside a callback triggered by LoadingState: that
        // would nest a second ChangeStateAsync call inside this one's still-true _isTransitioning,
        // which either no-ops (guard rejects it) or — if the guard were loosened — double-exits the
        // previous state, since UIStateMachine only promotes _currentState after OnEnterAsync
        // returns. The two _navigator.ChangeStateAsync calls below are sequential siblings, not
        // nested, so by the time the second one runs, _currentState is already correctly promoted
        // to LoadingState.
        public async UniTask LoadSceneAndChangeStateAsync<TNext>(string sceneName, CancellationToken ct = default)
            where TNext : IGameState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameLifecycleManager] Transitioning — LoadSceneAndChangeStateAsync<{typeof(TNext).Name}> ignored.");
                return;
            }
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                _loadingContext.Set(sceneName);
                await _navigator.ChangeStateAsync<LoadingState>(cts.Token);
                await _navigator.ChangeStateAsync<TNext>(cts.Token);
            }
            finally
            {
                await HideOverlaySafeAsync();
                _isTransitioning = false;
            }
        }

        // Restarts the current state in-place (exit → enter same state).
        // Use instead of ChangeStateAsync<T> when the game is already in T and needs a full reset
        // (e.g. Retry after game-over, Restart from pause), since the state machine's same-state
        // guard would otherwise silently reject the transition.
        public async UniTask RestartCurrentStateAsync(CancellationToken ct = default)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning("[GameLifecycleManager] Transitioning — RestartCurrentStateAsync ignored.");
                return;
            }
            var current = _stateMachine.CurrentState;
            if (current == null) return;
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                await current.OnExitAsync(cts.Token);
                // Clear any views OnExitAsync left on the navigator stack. If OnExitAsync already
                // called CloseAllAsync, this is a no-op (ClearAsync on empty stack exits immediately).
                await _navigator.CloseAllAsync(cts.Token);
                await current.OnEnterAsync(cts.Token);
            }
            finally
            {
                await HideOverlaySafeAsync();
                _isTransitioning = false;
            }
        }
    }
}
