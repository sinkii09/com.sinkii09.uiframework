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
        private readonly IUINavigator _navigator;
        private readonly CancellationToken _exitToken;
        private bool _isTransitioning;

        [Inject]
        public GameLifecycleManager(
            IUIStateMachine stateMachine,
            IUINavigator navigator,
            BootState bootState,
            LoadingState loadingState)
        {
            _stateMachine = stateMachine;
            _navigator = navigator;
            _exitToken = Application.exitCancellationToken;
            stateMachine.RegisterState(bootState);
            stateMachine.RegisterState(loadingState);
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
                await _stateMachine.ChangeStateAsync<BootState>(cts.Token);
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
                await _stateMachine.ChangeStateAsync<T>(cts.Token);
            }
            finally
            {
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
                await current.OnExitAsync(cts.Token);
                // Clear any views OnExitAsync left on the navigator stack. If OnExitAsync already
                // called CloseAllAsync, this is a no-op (ClearAsync on empty stack exits immediately).
                await _navigator.CloseAllAsync(cts.Token);
                await current.OnEnterAsync(cts.Token);
            }
            finally
            {
                _isTransitioning = false;
            }
        }
    }
}
