using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the animation/transition-hardening cluster's CRITICAL finding:
    // LoadingState.OnEnterAsync used to invoke a consumer-supplied onLoaded callback that called
    // back into GameLifecycleManager.ChangeStateAsync<TNext> — nested inside the outer
    // ChangeStateAsync<LoadingState>'s still-true _isTransitioning guard, so the nested call
    // silently no-op'd and TNext was never actually entered. See
    // plans/260802-1358-animation-transition-hardening/plan.md, Finding 1.
    public class GameLifecycleManagerLoadSceneTests
    {
        // Immediate completion — these tests exercise state-machine sequencing, not real scene IO.
        private sealed class FakeSceneLoader : ISceneLoader
        {
            internal string LastLoadedScene { get; private set; }

            public UniTask LoadAsync(string sceneName, LoadSceneMode mode, CancellationToken ct = default,
                System.IProgress<float> progress = null)
            {
                LastLoadedScene = sceneName;
                return UniTask.CompletedTask;
            }

            public UniTask UnloadAsync(string sceneName, CancellationToken ct = default) => UniTask.CompletedTask;
        }

        private sealed class FakeGameState : FakeViewState, IGameState
        {
            public string SceneName => null;
            public bool PausesGameTime => false;
        }

        // Distinct type from FakeGameState — UIStateMachine.RegisterState<T> keys off the static
        // registered type, and this test needs two independently-trackable game states: the one
        // "already current" before loading starts, and the one loading transitions into.
        private sealed class PreviousState : FakeViewState, IGameState
        {
            public string SceneName => null;
            public bool PausesGameTime => false;
        }

        private static (GameLifecycleManager glm, UIStateMachine stateMachine, FakeSceneLoader sceneLoader)
            BuildGlm()
        {
            var stateMachine = new UIStateMachine();
            var navigator = new UINavigator(new NavigationStack(null), stateMachine, null,
                System.Array.Empty<UIViewRegistration>(), null);
            var sceneLoader = new FakeSceneLoader();
            var loadingContext = new LoadingContext();
            var glm = new GameLifecycleManager(stateMachine, navigator, new NullTransitionOverlay(),
                loadingContext, new BootState(), new LoadingState(sceneLoader, loadingContext));
            return (glm, stateMachine, sceneLoader);
        }

        [UnityTest]
        public IEnumerator LoadSceneAndChangeStateAsync_EntersTargetState() => UniTask.ToCoroutine(async () =>
        {
            var (glm, stateMachine, sceneLoader) = BuildGlm();
            var next = new FakeGameState();
            glm.RegisterState(next);

            // Core regression: before the fix, this call would complete "successfully" (no
            // exception) but next.OnEnterCount would still be 0 — the nested ChangeStateAsync call
            // LoadingState used to trigger was silently dropped by GLM's own reentrancy guard.
            await glm.LoadSceneAndChangeStateAsync<FakeGameState>("SomeScene");

            Assert.AreEqual("SomeScene", sceneLoader.LastLoadedScene);
            Assert.AreEqual(1, next.OnEnterCount);
            Assert.AreSame(next, stateMachine.CurrentState);
        });

        [UnityTest]
        public IEnumerator LoadSceneAndChangeStateAsync_DoesNotDoubleExitPreviousState() => UniTask.ToCoroutine(async () =>
        {
            var (glm, stateMachine, _) = BuildGlm();
            var previous = new PreviousState();
            var next = new FakeGameState();
            stateMachine.RegisterState(previous);
            glm.RegisterState(next);

            // Set up "already in a game state" before loading starts, bypassing GLM/navigator —
            // mirrors whatever state the game was actually in prior to calling LoadSceneAndChangeStateAsync.
            await stateMachine.ChangeStateAsync<PreviousState>();
            Assert.AreEqual(1, previous.OnEnterCount);
            Assert.AreEqual(0, previous.OnExitCount);

            // The two ChangeStateAsync calls inside LoadSceneAndChangeStateAsync are sequential
            // siblings, not nested — by the time the second (into FakeGameState) runs,
            // _currentState is already correctly promoted to LoadingState, so LoadingState (whose
            // OnExitAsync is a no-op) is what gets exited, not a second OnExitAsync on `previous`.
            await glm.LoadSceneAndChangeStateAsync<FakeGameState>("SomeScene");

            Assert.AreEqual(1, previous.OnExitCount, "previous state's OnExitAsync must run exactly once, not twice");
            Assert.AreEqual(1, next.OnEnterCount);
            Assert.AreSame(next, stateMachine.CurrentState);
        });
    }
}
