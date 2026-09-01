using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Pins the fire-and-forget navigation queue added in Phase 1b.
    ///
    /// <para>GameLifecycleManager's awaitable entry points refuse a request that arrives while
    /// another transition runs. The queue runs those requests afterwards instead. The load-bearing
    /// detail is that the drain must WAIT for every guard that could refuse an item — this
    /// manager's transition flag, the navigator's, and boot — before invoking it. A queue without
    /// that wait drains straight into rejections and looks, from the outside, exactly like a queue
    /// that works.</para>
    ///
    /// <para>Queue mechanics (dedup, cap, cancellation, ordering, shutdown) are tested directly
    /// against <see cref="NavigationRequestQueue"/> with a controllable idle predicate. Coupling to
    /// the real guards is tested through <see cref="GameLifecycleManager"/>.</para>
    /// </summary>
    public class NavigationQueueTests
    {
        // Distinct type per state: UIStateMachine.RegisterState<T> keys off the static type, so
        // sharing one class across tests risks an "already registered" collision.
        private sealed class StateA : GateViewState { }
        private sealed class StateB : GateViewState { }
        private sealed class StateC : GateViewState { }

        /// <summary>
        /// A state whose OnEnterAsync can be held open across frames, so a test can observe the
        /// system while a transition is genuinely in flight. FakeViewState completes synchronously
        /// and cannot express that.
        /// </summary>
        private class GateViewState : IGameState
        {
            internal int OnEnterCount;
            internal int OnExitCount;
            internal UniTaskCompletionSource Gate;
            internal Exception ThrowDuringEnter;
            internal Action OnEnterCallback;

            // IGameState extends IViewState with these two; GameLifecycleManager's entry points
            // are constrained to IGameState, the navigator's only to IViewState.
            public string SceneName => null;
            public bool PausesGameTime => false;

            public async UniTask OnEnterAsync(CancellationToken ct = default)
            {
                OnEnterCount++;
                OnEnterCallback?.Invoke();
                if (ThrowDuringEnter != null) throw ThrowDuringEnter;
                if (Gate != null) await Gate.Task;
            }

            public UniTask OnExitAsync(CancellationToken ct = default)
            {
                OnExitCount++;
                return UniTask.CompletedTask;
            }
        }

        // Immediate completion — these tests exercise queue sequencing, not real scene IO.
        private sealed class FakeSceneLoader : ISceneLoader
        {
            internal string LastLoadedScene { get; private set; }

            public UniTask LoadAsync(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode,
                CancellationToken ct = default, IProgress<float> progress = null)
            {
                LastLoadedScene = sceneName;
                return UniTask.CompletedTask;
            }

            public UniTask UnloadAsync(string sceneName, CancellationToken ct = default) => UniTask.CompletedTask;
        }

        // Turns what would otherwise be a hung test run into a clean, named failure. Every wait in
        // this file goes through here for that reason.
        private static async UniTask WaitFor(Func<bool> condition, string what, int maxFrames = 300)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition()) return;
                await UniTask.Yield();
            }
            Assert.Fail($"Timed out after {maxFrames} frames waiting for: {what}");
        }

        private static UINavigator NewNavigator(UIStateMachine stateMachine)
            => new UINavigator(new NavigationStack(null), stateMachine, null,
                               Array.Empty<UIViewRegistration>(), null);

        private static GameLifecycleManager NewManager(UIStateMachine sm, UINavigator nav)
            => new GameLifecycleManager(sm, nav, new NullTransitionOverlay(), new LoadingContext(),
                                        new BootState(), new LoadingState(null, null));

        private static NavigationRequestQueue.Identity Id<T>()
            => new NavigationRequestQueue.Identity(NavigationRequestQueue.Kind.ChangeState, typeof(T));

        // ---------------------------------------------------------------- queue mechanics

        [UnityTest]
        public IEnumerator Enqueue_WhenIdle_RunsImmediately() => UniTask.ToCoroutine(async () =>
        {
            int ran = 0;
            using var queue = new NavigationRequestQueue(() => true);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);

            await WaitFor(() => ran == 1, "the item to run");
        });

        [UnityTest]
        public IEnumerator Enqueue_WhileNotIdle_WaitsInsteadOfRunning() => UniTask.ToCoroutine(async () =>
        {
            // The whole point of the phase: an item must not be invoked while a guard would refuse
            // it. Without the wait this runs immediately and the request is thrown away.
            bool idle = false;
            int ran = 0;
            using var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);

            for (int i = 0; i < 10; i++) await UniTask.Yield();
            Assert.AreEqual(0, ran, "Item ran while the idle predicate was false.");

            idle = true;
            await WaitFor(() => ran == 1, "the item to run once idle");
        });

        [UnityTest]
        public IEnumerator Enqueue_PreservesFifoOrder() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            var order = new List<string>();
            using var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(), _ => { order.Add("A"); return UniTask.FromResult(NavigationResult.Completed); }, default);
            queue.Enqueue(Id<StateB>(), _ => { order.Add("B"); return UniTask.FromResult(NavigationResult.Completed); }, default);
            queue.Enqueue(Id<StateC>(), _ => { order.Add("C"); return UniTask.FromResult(NavigationResult.Completed); }, default);

            idle = true;
            await WaitFor(() => order.Count == 3, "all three items to run");
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, order);
        });

        [UnityTest]
        public IEnumerator Enqueue_DuplicateIdentityWithSameToken_IsDeduped() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            int ran = 0;
            using var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);
            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);

            Assert.AreEqual(1, queue.PendingCount, "Identical pending request should collapse.");

            idle = true;
            await WaitFor(() => ran == 1, "the single deduped item to run");
            for (int i = 0; i < 5; i++) await UniTask.Yield();
            Assert.AreEqual(1, ran);
        });

        [UnityTest]
        public IEnumerator Enqueue_DuplicateIdentityWithDifferentTokens_IsNotDeduped() => UniTask.ToCoroutine(async () =>
        {
            // Collapsing these would keep only the older item and its token. If that token is then
            // cancelled, the second caller's still-live request disappears with it.
            bool idle = false;
            int ran = 0;
            var ctsA = new CancellationTokenSource();
            var ctsB = new CancellationTokenSource();
            try
            {
                using var queue = new NavigationRequestQueue(() => idle);

                queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, ctsA.Token);
                queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, ctsB.Token);

                Assert.AreEqual(2, queue.PendingCount, "Different token lifetimes must not collapse.");

                idle = true;
                await WaitFor(() => ran == 2, "both items to run");
            }
            finally
            {
                ctsA.Dispose();
                ctsB.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator Enqueue_DifferentSceneSameTargetState_IsNotDeduped() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            var scenes = new List<string>();
            using var queue = new NavigationRequestQueue(() => idle);

            var one = new NavigationRequestQueue.Identity(
                NavigationRequestQueue.Kind.LoadScene, typeof(StateA), "SceneOne");
            var two = new NavigationRequestQueue.Identity(
                NavigationRequestQueue.Kind.LoadScene, typeof(StateA), "SceneTwo");

            queue.Enqueue(one, _ => { scenes.Add("SceneOne"); return UniTask.FromResult(NavigationResult.Completed); }, default);
            queue.Enqueue(two, _ => { scenes.Add("SceneTwo"); return UniTask.FromResult(NavigationResult.Completed); }, default);

            Assert.AreEqual(2, queue.PendingCount, "Same target state but different scenes are different requests.");

            idle = true;
            await WaitFor(() => scenes.Count == 2, "both scene loads to run");
            CollectionAssert.AreEqual(new[] { "SceneOne", "SceneTwo" }, scenes);
        });

        [UnityTest]
        public IEnumerator Enqueue_BeyondDepthCap_IsDroppedWithWarning() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            using var queue = new NavigationRequestQueue(() => idle);

            // Distinct scene names keep every request a distinct Identity, so dedup does not
            // absorb them and the cap is what does the rejecting.
            for (int i = 0; i < NavigationRequestQueue.MaxPendingRequests; i++)
            {
                var id = new NavigationRequestQueue.Identity(
                    NavigationRequestQueue.Kind.LoadScene, typeof(StateA), $"Scene{i}");
                queue.Enqueue(id, _ => UniTask.FromResult(NavigationResult.Completed), default);
            }
            Assert.AreEqual(NavigationRequestQueue.MaxPendingRequests, queue.PendingCount);

            LogAssert.Expect(LogType.Warning, new Regex("Queue full"));
            var overflow = new NavigationRequestQueue.Identity(
                NavigationRequestQueue.Kind.LoadScene, typeof(StateA), "Overflow");
            queue.Enqueue(overflow, _ => UniTask.FromResult(NavigationResult.Completed), default);

            Assert.AreEqual(NavigationRequestQueue.MaxPendingRequests, queue.PendingCount,
                "Over-cap request must be dropped, not appended.");
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Enqueue_WithCancelledToken_IsSkipped_AndNextItemStillRuns() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            int cancelledRan = 0, nextRan = 0;
            var cts = new CancellationTokenSource();
            try
            {
                using var queue = new NavigationRequestQueue(() => idle);

                queue.Enqueue(Id<StateA>(), _ => { cancelledRan++; return UniTask.FromResult(NavigationResult.Completed); }, cts.Token);
                queue.Enqueue(Id<StateB>(), _ => { nextRan++; return UniTask.FromResult(NavigationResult.Completed); }, default);

                cts.Cancel();
                // The drain is already parked on the idle wait holding this item, so the
                // cancellation is observed on the post-wait check rather than the pre-wait one.
                LogAssert.Expect(LogType.Warning, new Regex("cancelled while queued"));
                idle = true;

                await WaitFor(() => nextRan == 1, "the surviving item to run");
                Assert.AreEqual(0, cancelledRan, "A cancelled request must not run.");
            }
            finally
            {
                cts.Dispose();
            }
        });

        [UnityTest]
        public IEnumerator Enqueue_ThrowingItem_IsLogged_AndNextItemStillRuns() => UniTask.ToCoroutine(async () =>
        {
            // One bad request must not strand every request behind it.
            bool idle = false;
            int nextRan = 0;
            using var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(),
                _ => throw new InvalidOperationException("boom"), default);
            queue.Enqueue(Id<StateB>(),
                _ => { nextRan++; return UniTask.FromResult(NavigationResult.Completed); }, default);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: boom"));
            idle = true;

            await WaitFor(() => nextRan == 1, "the item after the throwing one to run");
        });

        [UnityTest]
        public IEnumerator Enqueue_ItemThatIsRefused_LogsError() => UniTask.ToCoroutine(async () =>
        {
            // A queued item that runs but is refused would otherwise vanish into a generic
            // "Transitioning — ignored" warning, and a queue draining entirely into rejections
            // would look identical to one that works.
            using var queue = new NavigationRequestQueue(() => true);

            LogAssert.Expect(LogType.Error, new Regex("ran but was Rejected"));
            queue.Enqueue(Id<StateA>(), _ => UniTask.FromResult(NavigationResult.Rejected), default);

            await WaitFor(() => !queue.IsDraining, "the drain to finish");
        });

        [UnityTest]
        public IEnumerator Enqueue_AfterDrainCompletes_StartsANewDrain() => UniTask.ToCoroutine(async () =>
        {
            // A stuck _isDraining would silently kill the queue forever with no error anywhere.
            int ran = 0;
            using var queue = new NavigationRequestQueue(() => true);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);
            await WaitFor(() => ran == 1, "the first item to run");
            await WaitFor(() => !queue.IsDraining, "the first drain to end");

            queue.Enqueue(Id<StateB>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);
            await WaitFor(() => ran == 2, "a fresh drain to run the second item");
        });

        [UnityTest]
        public IEnumerator Enqueue_AfterDispose_IsDroppedWithoutThrowing() => UniTask.ToCoroutine(async () =>
        {
            bool idle = false;
            int ran = 0;
            var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);
            Assert.AreEqual(1, queue.PendingCount);

            queue.Dispose();
            Assert.AreEqual(0, queue.PendingCount, "Dispose must drop pending requests.");

            LogAssert.Expect(LogType.Warning, new Regex("Shutting down"));
            queue.Enqueue(Id<StateB>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, default);

            idle = true;
            for (int i = 0; i < 10; i++) await UniTask.Yield();
            Assert.AreEqual(0, ran, "Nothing may run after Dispose.");
        });

        // ---------------------------------------------------- coupling to the real guards

        [UnityTest]
        public IEnumerator EnqueueStateChange_DuringRunningTransition_RunsAfterIt() => UniTask.ToCoroutine(async () =>
        {
            // The headline behaviour. Today this request is refused and discarded.
            var sm = new UIStateMachine();
            var stateA = new StateA();
            var stateB = new StateB();
            sm.RegisterState(stateA);
            sm.RegisterState(stateB);
            var nav = NewNavigator(sm);
            using var glm = NewManager(sm, nav);

            await glm.StartAsync(CancellationToken.None);

            stateA.Gate = new UniTaskCompletionSource();
            UniTask<NavigationResult> running = glm.ChangeStateAsync<StateA>();
            await WaitFor(() => stateA.OnEnterCount == 1, "StateA to be mid-entry");

            glm.EnqueueStateChange<StateB>();
            for (int i = 0; i < 5; i++) await UniTask.Yield();
            Assert.AreEqual(0, stateB.OnEnterCount,
                "The queued request must not run while the first transition is still in flight.");

            stateA.Gate.TrySetResult();
            await running;

            await WaitFor(() => stateB.OnEnterCount == 1, "the queued transition to run after the first");
            Assert.AreEqual(1, stateA.OnExitCount, "StateA should have been exited exactly once.");
        });

        [UnityTest]
        public IEnumerator EnqueueStateChange_BeforeStartAsync_WaitsForBoot() => UniTask.ToCoroutine(async () =>
        {
            // An IInitializable bootstrap runs before IAsyncStartable.StartAsync. Without the
            // _hasStarted gate this drains immediately and enters a state BootState then clobbers.
            var sm = new UIStateMachine();
            var stateA = new StateA();
            sm.RegisterState(stateA);
            var nav = NewNavigator(sm);
            using var glm = NewManager(sm, nav);

            glm.EnqueueStateChange<StateA>();
            for (int i = 0; i < 10; i++) await UniTask.Yield();
            Assert.AreEqual(0, stateA.OnEnterCount, "Nothing may enter before boot has started.");

            await glm.StartAsync(CancellationToken.None);

            await WaitFor(() => stateA.OnEnterCount == 1, "the queued state to run after boot");
            Assert.IsInstanceOf<StateA>(sm.CurrentState, "Boot must not clobber the queued state.");
        });

        [UnityTest]
        public IEnumerator EnqueueStateChange_WhileNavigatorBusy_WaitsForNavigator() => UniTask.ToCoroutine(async () =>
        {
            // Game code calling navigator.CloseAllAsync directly holds the NAVIGATOR's flag while
            // this manager is perfectly idle. An idle predicate covering only the manager's own
            // flag would invoke the item here and have it refused at the navigator's guard.
            var sm = new UIStateMachine();
            var stateA = new StateA();
            sm.RegisterState(stateA);
            var nav = NewNavigator(sm);
            using var glm = NewManager(sm, nav);

            await glm.StartAsync(CancellationToken.None);

            var gate = new UniTaskCompletionSource();
            var busyState = new StateB { Gate = gate };
            sm.RegisterState(busyState);
            // Drives the navigator's own flag without going through the lifecycle manager.
            UniTask<NavigationResult> navBusy = nav.ChangeStateAsync<StateB>();
            await WaitFor(() => nav.IsTransitioning, "the navigator to be busy");

            glm.EnqueueStateChange<StateA>();
            for (int i = 0; i < 5; i++) await UniTask.Yield();
            Assert.AreEqual(0, stateA.OnEnterCount, "Must wait while the navigator is transitioning.");

            gate.TrySetResult();
            await navBusy;

            await WaitFor(() => stateA.OnEnterCount == 1, "the queued state to run once the navigator is free");
        });

        [UnityTest]
        public IEnumerator Enqueue_PassesAShutdownLinkedTokenToTheWork() => UniTask.ToCoroutine(async () =>
        {
            // Nothing holds a handle to a fire-and-forget transition, so the only way to stop one
            // on scope teardown is the token the queue hands it. Passing item.CallerCt straight
            // through (usually `default`) would ship green without this.
            CancellationToken observed = default;
            var gate = new UniTaskCompletionSource();
            var queue = new NavigationRequestQueue(() => true);

            queue.Enqueue(Id<StateA>(), async ct =>
            {
                observed = ct;
                await gate.Task;
                return NavigationResult.Completed;
            }, default);

            await WaitFor(() => observed.CanBeCanceled, "the work to receive a cancellable token");
            Assert.IsFalse(observed.IsCancellationRequested);

            queue.Dispose();
            Assert.IsTrue(observed.IsCancellationRequested,
                "Disposing the queue must cancel the transition it is currently running.");

            gate.TrySetResult();
            await WaitFor(() => !queue.IsDraining, "the drain to unwind");
        });

        [UnityTest]
        public IEnumerator Enqueue_WhenCallerDisposedItsTokenSourceWithoutCancelling_StillRuns() => UniTask.ToCoroutine(async () =>
        {
            // Pins a genuinely surprising runtime behaviour that the XML docs promise. Disposing a
            // CancellationTokenSource does NOT make its token report cancelled here, and neither
            // reading it, linking to it, nor registering on it throws — all three were verified
            // against this Unity runtime. So "dispose without cancel" leaves the request live, and
            // a defensive ObjectDisposedException handler around this would be dead code.
            bool idle = false;
            int ran = 0;
            var cts = new CancellationTokenSource();
            using var queue = new NavigationRequestQueue(() => idle);

            queue.Enqueue(Id<StateA>(), _ => { ran++; return UniTask.FromResult(NavigationResult.Completed); }, cts.Token);
            cts.Dispose();
            idle = true;

            await WaitFor(() => ran == 1, "the request with the disposed (but never cancelled) source to run");
        });

        [UnityTest]
        public IEnumerator EnqueueRestart_ReEntersTheCurrentState() => UniTask.ToCoroutine(async () =>
        {
            // EnqueueRestart and EnqueueSceneLoad were otherwise never called by any test, so an
            // argument swap between the Identity and the work closure would have shipped green.
            var sm = new UIStateMachine();
            var stateA = new StateA();
            sm.RegisterState(stateA);
            var nav = NewNavigator(sm);
            using var glm = NewManager(sm, nav);

            await glm.StartAsync(CancellationToken.None);
            await glm.ChangeStateAsync<StateA>();
            Assert.AreEqual(1, stateA.OnEnterCount);

            glm.EnqueueRestart();

            await WaitFor(() => stateA.OnEnterCount == 2, "the queued restart to re-enter the state");
            Assert.AreEqual(1, stateA.OnExitCount, "Restart exits the current state once before re-entering.");
        });

        [UnityTest]
        public IEnumerator EnqueueSceneLoad_LoadsTheSceneAndEntersTheTargetState() => UniTask.ToCoroutine(async () =>
        {
            var sm = new UIStateMachine();
            var target = new StateA();
            sm.RegisterState(target);
            var nav = NewNavigator(sm);
            var sceneLoader = new FakeSceneLoader();
            var loadingContext = new LoadingContext();
            using var glm = new GameLifecycleManager(sm, nav, new NullTransitionOverlay(),
                loadingContext, new BootState(), new LoadingState(sceneLoader, loadingContext));

            await glm.StartAsync(CancellationToken.None);

            glm.EnqueueSceneLoad<StateA>("QueuedScene");

            await WaitFor(() => target.OnEnterCount == 1, "the queued scene load to reach its target state");
            Assert.AreEqual("QueuedScene", sceneLoader.LastLoadedScene,
                "The scene name must survive the trip through the queue's closure.");
        });

        [UnityTest]
        public IEnumerator EnqueueStateChange_FromStateOnEnter_DoesNotHang_AndRunsAfterwards() => UniTask.ToCoroutine(async () =>
        {
            // Re-entrancy: the enqueue happens on the drain's own call stack. Because the enqueue
            // API is void, the caller cannot await it, so it cannot block the drain. The item runs
            // after _currentState has been promoted, which is what makes it safe.
            var sm = new UIStateMachine();
            var stateA = new StateA();
            var stateB = new StateB();
            sm.RegisterState(stateA);
            sm.RegisterState(stateB);
            var nav = NewNavigator(sm);
            using var glm = NewManager(sm, nav);

            await glm.StartAsync(CancellationToken.None);

            stateA.OnEnterCallback = () => glm.EnqueueStateChange<StateB>();

            NavigationResult first = await glm.ChangeStateAsync<StateA>();
            Assert.AreEqual(NavigationResult.Completed, first);

            await WaitFor(() => stateB.OnEnterCount == 1, "the re-entrant request to run after the outer transition");
            Assert.AreEqual(1, stateA.OnExitCount,
                "StateA must be exited exactly once — a nested run would double-exit the previous state.");
        });
    }
}
