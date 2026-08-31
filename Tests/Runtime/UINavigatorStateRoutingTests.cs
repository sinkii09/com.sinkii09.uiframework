using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the C2 finding: UINavigator.ChangeStateAsync<TState> used to call
    // IUIStateMachine.ResetState() before every transition, which nulled _currentState and made
    // UIStateMachine's `previous != null` guard silently skip OnExitAsync — dropping non-view
    // cleanup (timeScale restore, subscription disposal, spawned-object teardown) on every
    // navigator-driven state change. See plans/260801-2148-correctness-cluster/
    // phase-02-lifecycle-navigator-routing.md.
    public class UINavigatorStateRoutingTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        // Distinct C# types per participant — UIStateMachine.RegisterState<T> keys off typeof(T),
        // inferred from the STATIC type of the argument, so two instances of the same FakeViewState
        // subtype would collide on registration ("already registered").
        private sealed class StateA : FakeViewState { }
        private sealed class StateB : FakeViewState { }
        private sealed class FakeGameState : FakeViewState, IGameState
        {
            public string SceneName => null;
            public bool PausesGameTime => false;
        }

        [UnityTest]
        public IEnumerator ChangeStateAsync_RunsPreviousOnExitExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            var stateMachine = new UIStateMachine();
            var stateA = new StateA();
            var stateB = new StateB();
            stateMachine.RegisterState(stateA);
            stateMachine.RegisterState(stateB);
            var navigator = new UINavigator(new NavigationStack(null), stateMachine, null,
                Array.Empty<UIViewRegistration>(), null);

            // C2-1: the C2 regression — would fail before phase 02's ResetState() removal, because
            // _currentState was nulled before UIStateMachine ever saw stateA as `previous`.
            await navigator.ChangeStateAsync<StateA>();
            await navigator.ChangeStateAsync<StateB>();

            Assert.AreEqual(1, stateA.OnExitCount);
        });

        [UnityTest]
        public IEnumerator ChangeStateAsync_ClearsNavigationStackBeforeEnter() => UniTask.ToCoroutine(async () =>
        {
            var stack = new NavigationStack(null);
            var view = new GameObject("StackView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            // Production views always reach the stack already initialized via UIViewFactory —
            // a raw AddComponent<TestView>() has a null ViewModel, which HideAsync's own
            // programmer-error guard (UIView.cs) would flag with an unhandled Debug.LogError
            // during ClearAsync below. Match production reality instead of suppressing the log.
            await view.InitializeAsync(new TestViewModel(), null);
            await stack.PushAsync(view);
            Assert.AreEqual(1, stack.Count);

            int? countDuringEnter = null;
            var stateMachine = new UIStateMachine();
            var state = new StateA { OnEnterCallback = () => countDuringEnter = stack.Count };
            stateMachine.RegisterState(state);
            var navigator = new UINavigator(stack, stateMachine, null, Array.Empty<UIViewRegistration>(), null);

            // C2-2: the stack must already be empty by the time OnEnterAsync runs — proves the
            // clear is not dead code and locks in the "clear before enter" ordering.
            await navigator.ChangeStateAsync<StateA>();

            Assert.AreEqual(0, countDuringEnter);
            Assert.AreEqual(0, stack.Count);
        });

        [UnityTest]
        public IEnumerator GameLifecycleManager_ChangeState_ClearsNavigationStack() => UniTask.ToCoroutine(async () =>
        {
            var stack = new NavigationStack(null);
            var view = new GameObject("StackView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            // See ChangeStateAsync_ClearsNavigationStackBeforeEnter — a real view is always
            // initialized before it reaches the stack; match that here too.
            await view.InitializeAsync(new TestViewModel(), null);
            await stack.PushAsync(view);

            var stateMachine = new UIStateMachine();
            var navigator = new UINavigator(stack, stateMachine, null, Array.Empty<UIViewRegistration>(), null);
            var glm = new GameLifecycleManager(stateMachine, navigator, new NullTransitionOverlay(),
                new LoadingContext(), new BootState(), new LoadingState(null, null));

            int? countDuringEnter = null;
            var gameState = new FakeGameState { OnEnterCallback = () => countDuringEnter = stack.Count };
            glm.RegisterState(gameState);

            // C2-3: GameLifecycleManager.ChangeStateAsync routes through the concrete UINavigator
            // (constructor now takes UINavigator, not IUINavigator) — same stack-clear guarantee as
            // C2-2, exercised through the public GLM API a game actually calls.
            await glm.ChangeStateAsync<FakeGameState>();

            Assert.AreEqual(0, countDuringEnter);
            Assert.AreEqual(0, stack.Count);
        });

        [Test]
        public void Container_ResolvesSameInstanceForInterfaceAndConcrete()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new UIRootLayerRefs());
            builder.RegisterInstance<IReadOnlyList<UIViewRegistration>>(Array.Empty<UIViewRegistration>());
            builder.RegisterInstance<IUILoader>(new FakeUILoader(_gos));
            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            builder.Register<IUIViewFactory, UIViewFactory>(Lifetime.Singleton);
            builder.RegisterInstance<INavigationStack>(new NavigationStack(null));
            builder.Register<IUIStateMachine, UIStateMachine>(Lifetime.Singleton);
            // UINavigator takes a UIBackdrop and an ITooltipService. Their `= null` defaults do NOT
            // make them optional — VContainer ignores C# default parameter values — so any
            // hand-built container must register both, exactly as UIFrameworkLifetimeScope does.
            // (The defaults exist only so the positional `new UINavigator(...)` calls in the other
            // fixtures keep compiling.)
            builder.RegisterInstance(new UIBackdrop(new UIViewPolicyResolver(null), null));
            builder.RegisterInstance<ITooltipService>(new NullTooltipService());
            builder.Register<IUINavigator, UINavigator>(Lifetime.Singleton).AsSelf();

            using var container = builder.Build();

            // C2-4: .AsSelf() must yield ONE singleton resolvable as both the interface and the
            // concrete type — GameLifecycleManager's constructor requires the concrete UINavigator.
            var asInterface = container.Resolve<IUINavigator>();
            var asConcrete = container.Resolve<UINavigator>();
            Assert.AreSame(asInterface, asConcrete);
        }
    }
}
