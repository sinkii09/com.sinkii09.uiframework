using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Pins the refusal contract added in Phase 1a.
    ///
    /// <para>Navigation requests get dropped in two different ways: a guard refuses one that arrives
    /// while another transition is running, and the navigation stack declines a push past its depth
    /// limit or a pop on an empty stack. All of them used to return a plain <c>UniTask</c> that
    /// completed normally, so an awaiting caller could not distinguish "the view is up" from "your
    /// request was discarded" — two guards refused with no log at all. Every guarded entry point now
    /// returns a <see cref="NavigationResult"/>.</para>
    ///
    /// <para>These tests exist because the change is otherwise invisible: reverting a guard to
    /// <c>return NavigationResult.Completed</c> would still compile, and the rest of the suite would
    /// stay green.</para>
    /// </summary>
    public class NavigationResultTests
    {
        // Distinct type per probe: UIStateMachine.RegisterState<T> keys off the static type, so
        // sharing one state class between tests risks an "already registered" collision.
        private sealed class CloseAllProbeState : FakeViewState { }
        private sealed class HideProbeState : FakeViewState { }

        private GameObjectTracker _gos;

        [SetUp]
        public void SetUp() => _gos = new GameObjectTracker();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private static UINavigator NewNavigator(UIStateMachine stateMachine)
            => new UINavigator(new NavigationStack(null), stateMachine, null,
                               Array.Empty<UIViewRegistration>(), null);

        private static UIViewRegistration Reg<TView, TVm>()
            => new UIViewRegistration(typeof(TView), typeof(TVm), typeof(TView).Name);

        [UnityTest]
        public IEnumerator CloseAllAsync_WhenIdle_ReturnsCompleted() => UniTask.ToCoroutine(async () =>
        {
            // The success half of the contract. Without it, a guard that rejected unconditionally
            // would satisfy every other test here.
            var navigator = NewNavigator(new UIStateMachine());

            NavigationResult result = await navigator.CloseAllAsync();

            Assert.AreEqual(NavigationResult.Completed, result);
        });

        [UnityTest]
        public IEnumerator ShowAsync_WhenPushDeclinedByDepthLimit_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // The defect the diff review caught: ShowAsync already DETECTED a declined push (to undo
            // its layer blocking) and then returned Completed anyway — leaving the headline bug alive
            // on the most-used entry point. A depth-limited stack is the reachable way to trigger it.
            var config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            try
            {
                config.MaxNavigationDepth = 1;
                var loader = new FakeUILoader(_gos);
                // The fake loader resolves a key to a concrete component type; without this it
                // throws rather than returning a prefab.
                loader.RegisterPrefab<TestView>(nameof(TestView));
                loader.RegisterPrefab<SecondTestView>(nameof(SecondTestView));
                var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);
                var navigator = new UINavigator(
                    new NavigationStack(config), new UIStateMachine(), factory,
                    new[] { Reg<TestView, TestViewModel>(), Reg<SecondTestView, SecondTestViewModel>() },
                    null);

                Assert.AreEqual(NavigationResult.Completed, await navigator.ShowAsync<TestView>(),
                    "First push is within the depth limit.");

                LogAssert.Expect(LogType.Warning, new Regex("Max depth"));
                NavigationResult second = await navigator.ShowAsync<SecondTestView>();

                Assert.AreEqual(NavigationResult.Rejected, second,
                    "Push was declined by the depth limit; Completed would tell the caller a view is on screen when none is.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        });

        [UnityTest]
        public IEnumerator PopAsync_OnEmptyStack_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // NavigationStack.PopAsync returns null rather than throwing, and UINavigator used to
            // discard that null and report Completed — the same false-success as the declined push.
            var navigator = NewNavigator(new UIStateMachine());

            LogAssert.Expect(LogType.Warning, new Regex("PopAsync on empty stack"));
            NavigationResult result = await navigator.PopAsync();

            Assert.AreEqual(NavigationResult.Rejected, result);
        });

        [UnityTest]
        public IEnumerator CloseAllAsync_DuringStateEnter_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // A state's OnEnterAsync runs while the navigator still holds _isTransitioning, so
            // CloseAllAsync from inside it is refused. That refusal was previously indistinguishable
            // from success.
            var stateMachine = new UIStateMachine();
            UINavigator navigator = null;
            UniTask<NavigationResult>? innerCall = null;

            var state = new CloseAllProbeState
            {
                // Capture the UniTask and await it AFTER the transition rather than draining it here
                // with GetAwaiter().GetResult(). That shortcut works only while the guard is the
                // first statement in the method — once anything is awaited before it (the queue in
                // the next phase does exactly that) the task is still pending and GetResult throws.
                OnEnterCallback = () => innerCall = navigator.CloseAllAsync()
            };
            stateMachine.RegisterState(state);
            navigator = NewNavigator(stateMachine);

            LogAssert.Expect(LogType.Warning, new Regex("CloseAllAsync ignored"));
            await navigator.ChangeStateAsync<CloseAllProbeState>();

            Assert.IsTrue(innerCall.HasValue, "OnEnterCallback did not run.");
            Assert.AreEqual(NavigationResult.Rejected, await innerCall.Value);
        });

        [UnityTest]
        public IEnumerator HideAsync_WhileTransitioning_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // One of the two guards that previously refused in total silence — no log, no result.
            var stateMachine = new UIStateMachine();
            UINavigator navigator = null;
            UniTask<NavigationResult>? innerCall = null;

            var state = new HideProbeState
            {
                OnEnterCallback = () => innerCall = navigator.HideAsync<TestView>()
            };
            stateMachine.RegisterState(state);
            navigator = NewNavigator(stateMachine);

            LogAssert.Expect(LogType.Warning, new Regex("HideAsync<TestView> ignored"));
            await navigator.ChangeStateAsync<HideProbeState>();

            Assert.IsTrue(innerCall.HasValue, "OnEnterCallback did not run.");
            Assert.AreEqual(NavigationResult.Rejected, await innerCall.Value);
        });

        [UnityTest]
        public IEnumerator HideAsync_WhenTopOfStackIsNotT_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // Only top-of-stack hide is supported. On an empty stack the top is null, so this is the
            // wrong-top refusal rather than the transitioning one above.
            var navigator = NewNavigator(new UIStateMachine());

            LogAssert.Expect(LogType.Warning, new Regex("Only top-of-stack hide is supported"));
            NavigationResult result = await navigator.HideAsync<TestView>();

            Assert.AreEqual(NavigationResult.Rejected, result);
        });

        [UnityTest]
        public IEnumerator RestartCurrentStateAsync_WithNoCurrentState_ReturnsRejected() => UniTask.ToCoroutine(async () =>
        {
            // The other previously-silent guard. Reachable whenever a Retry button is wired up
            // before StartAsync has run, where the symptom is a button that does nothing at all.
            var stateMachine = new UIStateMachine();
            var navigator = NewNavigator(stateMachine);
            var glm = new GameLifecycleManager(stateMachine, navigator, new NullTransitionOverlay(),
                new LoadingContext(), new BootState(), new LoadingState(null, null));

            LogAssert.Expect(LogType.Warning, new Regex("No current state"));
            NavigationResult result = await glm.RestartCurrentStateAsync();

            Assert.AreEqual(NavigationResult.Rejected, result);
        });
    }
}
