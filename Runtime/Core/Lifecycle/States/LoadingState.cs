using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Before calling ChangeStateAsync<LoadingState>, configure the context:
    //   _loadingContext.Set("SceneName", ct => _lifecycle.ChangeStateAsync<GameplayState>(ct));
    // LoadingContext is reset immediately after the scene loads to prevent stale re-use.
    // If Set() is never called, OnEnterAsync throws InvalidOperationException (fast-fail).
    //
    // NOTE on OnEnterAsync/OnExitAsync ordering: OnEnterAsync triggers the next-state
    // transition via onLoaded, which causes UIStateMachine to call OnExitAsync (hide loading
    // screen) before entering the next state. This is correct, but OnExitAsync fires while
    // still on the OnEnterAsync call stack. Future LoadingView show/hide must account for this.
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

            // Overlay show/hide is owned by GameLifecycleManager around the whole
            // ChangeStateAsync<LoadingState> call (including this scene load) — not by this state.
            await _sceneLoader.LoadAsync(_loadingContext.TargetScene, LoadSceneMode.Single, ct);

            // Consume context before invoking callback to prevent stale re-entry if
            // onLoaded itself calls ChangeStateAsync<LoadingState> again.
            var onLoaded = _loadingContext.OnLoaded;
            _loadingContext.Reset();

            if (onLoaded != null)
                await onLoaded(ct);
        }

        public UniTask OnExitAsync(CancellationToken ct = default)
        {
            // See OnEnterAsync — overlay is GameLifecycleManager's responsibility, not this state's.
            return UniTask.CompletedTask;
        }
    }
}
