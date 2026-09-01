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
    public sealed class GameLifecycleManager : IAsyncStartable, IDisposable
    {
        private readonly IUIStateMachine _stateMachine;
        private readonly UINavigator _navigator; // concrete: ChangeStateAsync is internal
        private readonly ITransitionOverlay _overlay;
        private readonly ILoadingContext _loadingContext;
        private readonly CancellationToken _exitToken;
        private readonly NavigationRequestQueue _queue;
        private bool _isTransitioning;
        // Gates the queue until boot owns the state machine. An IInitializable bootstrap runs
        // BEFORE IAsyncStartable.StartAsync, so without this an enqueue from there would find the
        // manager idle, drain immediately, and enter a state that BootState then clobbers.
        private bool _hasStarted;

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
            // A queued item must not run until every guard that could refuse it is clear: this
            // manager's own, and the navigator's — game code calling navigator.ShowAsync/PopAsync
            // directly holds the latter while this manager is perfectly idle.
            _queue = new NavigationRequestQueue(
                () => _hasStarted && !_isTransitioning && !_navigator.IsTransitioning, _exitToken);
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
            // Both assignments are synchronous and adjacent, so a queued drain — which can only
            // resume on a later player-loop tick — cannot observe "started but not transitioning".
            _hasStarted = true;
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                // IAsyncStartable cannot return a result, so a refused boot would otherwise leave
                // the game on an empty stack with nothing but a warning to explain it.
                NavigationResult result = await _navigator.ChangeStateAsync<BootState>(cts.Token);
                if (result != NavigationResult.Completed)
                    Debug.LogError($"[GameLifecycleManager] Boot transition was {result} — the game has no initial state.");
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        // ct = default means only Application.exitCancellationToken cancels this transition.
        public async UniTask<NavigationResult> ChangeStateAsync<T>(CancellationToken ct = default) where T : IGameState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameLifecycleManager] Transitioning — ChangeStateAsync<{typeof(T).Name}> ignored.");
                return NavigationResult.Rejected;
            }
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                // Propagated, not discarded: the navigator's own guard can refuse independently.
                return await _navigator.ChangeStateAsync<T>(cts.Token);
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
        public async UniTask<NavigationResult> LoadSceneAndChangeStateAsync<TNext>(string sceneName, CancellationToken ct = default)
            where TNext : IGameState
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameLifecycleManager] Transitioning — LoadSceneAndChangeStateAsync<{typeof(TNext).Name}> ignored.");
                return NavigationResult.Rejected;
            }
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                _loadingContext.Set(sceneName);
                // If the scene-load step is refused, the second transition would run against the
                // wrong state — report the refusal instead of continuing past it.
                NavigationResult loading = await _navigator.ChangeStateAsync<LoadingState>(cts.Token);
                if (loading != NavigationResult.Completed)
                {
                    // The scene was never loaded, so the pending target must not outlive this call —
                    // a later LoadingState would otherwise read a scene name nobody asked for.
                    _loadingContext.Reset();
                    return loading;
                }
                return await _navigator.ChangeStateAsync<TNext>(cts.Token);
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
        public async UniTask<NavigationResult> RestartCurrentStateAsync(CancellationToken ct = default)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning("[GameLifecycleManager] Transitioning — RestartCurrentStateAsync ignored.");
                return NavigationResult.Rejected;
            }
            var current = _stateMachine.CurrentState;
            if (current == null)
            {
                // Previously refused in complete silence. Reachable before StartAsync has run, or
                // after ResetState() — a Retry button wired up too early would simply do nothing.
                Debug.LogWarning("[GameLifecycleManager] No current state — RestartCurrentStateAsync ignored.");
                return NavigationResult.Rejected;
            }
            _isTransitioning = true;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _exitToken);
                await ShowOverlaySafeAsync(cts.Token);
                await current.OnExitAsync(cts.Token);
                // Clear any views OnExitAsync left on the navigator stack. If OnExitAsync already
                // called CloseAllAsync, this is a no-op (ClearAsync on empty stack exits immediately).
                NavigationResult cleared = await _navigator.CloseAllAsync(cts.Token);
                if (cleared != NavigationResult.Completed)
                {
                    // Deliberately NOT aborting, and deliberately NOT returning `cleared`.
                    // OnExitAsync has already run: bailing out here would leave the state exited but
                    // never re-entered — a worse, harder-to-diagnose condition than stale views. And
                    // returning Rejected would misreport a restart that does in fact complete below.
                    // A refusal here means something started navigation concurrently with the
                    // restart, which is a caller bug, so it is an error rather than a warning.
                    Debug.LogError(
                        "[GameLifecycleManager] Stack clear was refused during RestartCurrentStateAsync — " +
                        "the state is being re-entered on top of views that should have been closed. " +
                        "Something navigated concurrently with the restart.");
                }
                await current.OnEnterAsync(cts.Token);
                return NavigationResult.Completed;
            }
            finally
            {
                await HideOverlaySafeAsync();
                _isTransitioning = false;
            }
        }

        /// <summary>
        /// Queued, fire-and-forget <see cref="ChangeStateAsync{T}"/>: runs now if idle, otherwise
        /// after the in-flight transition finishes, instead of being refused outright.
        ///
        /// <para>Use this for navigation driven by code that cannot wait — a win condition and a
        /// timeout racing in the same frame, a collision or timer changing state mid-transition.
        /// Use <see cref="ChangeStateAsync{T}"/> when you need the result and know you are not
        /// inside a transition.</para>
        ///
        /// <para>Returns <c>void</c> deliberately, and that is the safety mechanism rather than an
        /// oversight: a queued request runs after the current one, so a caller running inside the
        /// current one — a state's <c>OnEnterAsync</c>, a view's <c>OnHideAsync</c>, a ViewModel
        /// teardown — would deadlock the queue if it could await its own request. Do not
        /// reintroduce that by waiting on a queued request's observable effects either (e.g.
        /// <c>await UniTask.WaitUntil(() =&gt; stateMachine.CurrentState is T)</c>).</para>
        ///
        /// <para><paramref name="ct"/> is read when the request comes up to run, which is after
        /// this method returns. Disposing its source before then does NOT cancel the request — a
        /// disposed source's token reads as never-cancelled — so cancel it, do not merely dispose
        /// it, if the intent is to call the request off. Note also that minting a fresh token
        /// source per call defeats duplicate collapsing.</para>
        /// </summary>
        public void EnqueueStateChange<T>(CancellationToken ct = default) where T : IGameState
            => _queue.Enqueue(
                new NavigationRequestQueue.Identity(NavigationRequestQueue.Kind.ChangeState, typeof(T)),
                token => ChangeStateAsync<T>(token), ct);

        /// <summary>
        /// Queued, fire-and-forget <see cref="RestartCurrentStateAsync"/>. See
        /// <see cref="EnqueueStateChange{T}"/> for the void rationale and the token lifetime rule.
        /// </summary>
        public void EnqueueRestart(CancellationToken ct = default)
            => _queue.Enqueue(
                new NavigationRequestQueue.Identity(NavigationRequestQueue.Kind.Restart),
                token => RestartCurrentStateAsync(token), ct);

        /// <summary>
        /// Queued, fire-and-forget <see cref="LoadSceneAndChangeStateAsync{TNext}"/>. See
        /// <see cref="EnqueueStateChange{T}"/> for the void rationale and the token lifetime rule.
        /// Two queued loads of different scenes are kept separate even when TNext matches.
        /// </summary>
        public void EnqueueSceneLoad<TNext>(string sceneName, CancellationToken ct = default)
            where TNext : IGameState
            => _queue.Enqueue(
                new NavigationRequestQueue.Identity(
                    NavigationRequestQueue.Kind.LoadScene, typeof(TNext), sceneName),
                token => LoadSceneAndChangeStateAsync<TNext>(sceneName, token), ct);

        // VContainer disposes singleton entry points that implement IDisposable. Drops every
        // queued request and stops the drain — nothing awaits a queued item, so nothing is
        // stranded by discarding them.
        public void Dispose() => _queue.Dispose();
    }
}
