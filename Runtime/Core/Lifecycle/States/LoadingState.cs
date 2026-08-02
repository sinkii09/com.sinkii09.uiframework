using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Loads a scene and stops — does not transition to another state on its own. Use
    // GameLifecycleManager.LoadSceneAndChangeStateAsync<TNext>(scene, ct) to load a scene and
    // then transition into TNext as one composed operation; do NOT call back into
    // GameLifecycleManager.ChangeStateAsync from any callback wired through this state — it
    // would nest inside the outer transition's _isTransitioning window and either no-op
    // (guard rejects it) or, if the guard were naively loosened, double-exit the previous state
    // (UIStateMachine only promotes _currentState after OnEnterAsync returns, so a nested call
    // would still see the pre-Loading state as "current" and re-run its OnExitAsync).
    // If Set() is never called, OnEnterAsync throws InvalidOperationException (fast-fail).
    // Overlay show/hide is owned by GameLifecycleManager around the whole call, not by this state.
    public class LoadingState : IGameState
    {
        private readonly ISceneLoader _sceneLoader;
        private readonly ILoadingContext _loadingContext;

        [Inject]
        public LoadingState(ISceneLoader sceneLoader, ILoadingContext loadingContext)
        {
            _sceneLoader = sceneLoader;
            _loadingContext = loadingContext;
        }

        public string SceneName => _loadingContext.TargetScene;
        public bool PausesGameTime => false;

        public async UniTask OnEnterAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_loadingContext.TargetScene))
                throw new InvalidOperationException(
                    "[LoadingState] TargetScene not set. Call ILoadingContext.Set() before ChangeStateAsync<LoadingState>().");

            await _sceneLoader.LoadAsync(_loadingContext.TargetScene, LoadSceneMode.Single, ct);

            // Consumed after loading to prevent stale re-use on the next LoadingState entry.
            _loadingContext.Reset();
        }

        public UniTask OnExitAsync(CancellationToken ct = default)
        {
            // Overlay is GameLifecycleManager's responsibility, not this state's.
            return UniTask.CompletedTask;
        }
    }
}
