using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // Shared fixtures for the Phase 05 correctness-cluster regression tests (UIViewFactory
    // concurrency, UINavigator/UIStateMachine state routing, DOTween transition cancel-restore,
    // and view/state cancellation propagation). Mirrors SaveTestHelpers.cs / FakeStorageBackend.cs
    // style: internal static/sealed types, WHY-comments, no production seams added.
    // See plans/260801-2148-correctness-cluster/phase-05-tests-and-verification.md for the matrix
    // these fixtures back.

    // Destroys tracked GameObjects in [TearDown]. PlayMode tests share one scene across the whole
    // run, so anything created by CreateAsync/factory tests must not leak into later tests.
    internal sealed class GameObjectTracker
    {
        private readonly List<GameObject> _tracked = new();

        internal T Track<T>(T component) where T : Component
        {
            _tracked.Add(component.gameObject);
            return component;
        }

        internal GameObject Track(GameObject go)
        {
            _tracked.Add(go);
            return go;
        }

        internal void DestroyAll()
        {
            foreach (var go in _tracked)
                if (go != null) UnityEngine.Object.Destroy(go);
            _tracked.Clear();
        }
    }

    // Stands in for ResourcesUILoader/AddressablesUILoader. "Prefabs" are built at runtime
    // (new GameObject + AddComponent) instead of loaded from disk — RegisterPrefab<T>(key) must be
    // called before a matching LoadAsync<T'>(key) is exercised (T' at the UIViewFactory call site
    // is always UIViewBase; the concrete type actually instantiated is looked up by key here).
    internal sealed class FakeUILoader : IUILoader
    {
        private readonly GameObjectTracker _tracker;
        private readonly Dictionary<string, Type> _prefabTypes = new();
        private readonly Dictionary<string, int> _loadCounts = new();
        private readonly Dictionary<string, int> _unloadCounts = new();

        // Delays LoadAsync by N frames before returning — forces two concurrent CreateAsync calls
        // to actually interleave instead of one completing synchronously before the second is even
        // issued. Frame-based (not wall-clock) per the plan's flake mitigation.
        internal int LoadDelayFrames { get; set; }
        // Set to make the next LoadAsync throw this exact exception.
        internal Exception LoadError { get; set; }

        internal FakeUILoader(GameObjectTracker tracker) => _tracker = tracker;

        internal void RegisterPrefab<TConcrete>(string key) where TConcrete : Component
            => _prefabTypes[key] = typeof(TConcrete);

        internal int LoadCount => Sum(_loadCounts);
        internal int UnloadCount => Sum(_unloadCounts);

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component
        {
            Increment(_loadCounts, key);
            if (LoadDelayFrames > 0)
                await UniTask.DelayFrame(LoadDelayFrames, cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            if (LoadError != null) throw LoadError;

            if (!_prefabTypes.TryGetValue(key, out var concreteType))
                throw new InvalidOperationException(
                    $"[FakeUILoader] No prefab registered for key '{key}'. Call RegisterPrefab<T>(key) first.");

            var go = _tracker.Track(new GameObject($"FakePrefab_{key}", typeof(RectTransform)));
            var component = go.AddComponent(concreteType);
            return (T)(object)component;
        }

        public UniTask UnloadAsync(string key, CancellationToken ct = default)
        {
            Increment(_unloadCounts, key);
            return UniTask.CompletedTask;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
            => counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;

        private static int Sum(Dictionary<string, int> counts)
        {
            var total = 0;
            foreach (var v in counts.Values) total += v;
            return total;
        }
    }

    // Minimal concrete UIView<TViewModel> pair. BindViewModel is intentionally empty — these tests
    // exercise UIViewFactory/UINavigator/UIStateMachine plumbing, not R3 bindings.
    internal class TestViewModel : ViewModelBase
    {
        internal int OnShowCount { get; private set; }
        // OnHide only ever runs inside NotifyHide (ViewModelBase.NotifyHide, guarded by _disposed),
        // so counting it here doubles as the NotifyHide-call counter the P1 tests need.
        internal int NotifyHideCount { get; private set; }

        public override void OnShow()
        {
            base.OnShow();
            OnShowCount++;
        }

        protected override void OnHide()
        {
            base.OnHide();
            NotifyHideCount++;
        }
    }

    internal class TestView : UIView<TestViewModel>
    {
        internal TestViewModel VM => ViewModel;

        protected override void BindViewModel(TestViewModel vm) { }

        // P1 fixture: throws whenever the caller's token is already cancelled, otherwise completes
        // immediately. Deterministic instead of racing a real delay against test-driven cancellation.
        protected override UniTask OnShowAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        protected override UniTask OnHideAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    // Second view/VM pair. Needed wherever a test must distinguish "this view" from "some other
    // view" — e.g. asserting the preload set contains exactly one of two registered views, or that
    // evicting one cached type leaves the other alone. TestView alone cannot express that.
    internal class SecondTestViewModel : ViewModelBase { }

    internal class SecondTestView : UIView<SecondTestViewModel>
    {
        protected override void BindViewModel(SecondTestViewModel vm) { }
    }

    // Carries an explicit [UIViewKey], so policy/key tests can prove UIViewKeys.For prefers the
    // attribute over the type name — the one case where the two derivations could have drifted
    // apart back when UIViewFactory and UIViewRegistry each implemented the rule themselves.
    [UIViewKey(Key)]
    internal class TestViewWithKey : UIView<TestViewModel>
    {
        internal const string Key = "custom/test-view-key";

        protected override void BindViewModel(TestViewModel vm) { }
    }

    // Proves the C1 DI binding fix: RegisterInstance(instance, viewType) binds the CONCRETE view
    // type, so a ViewModel that injects the concrete view resolves regardless of which CreateAsync
    // overload created it (previously RegisterInstance(instance) bound UIViewBase only, on the
    // type-erased path).
    internal sealed class TestViewModelInjectingView : ViewModelBase
    {
        internal TestViewInjectingSelf InjectedView { get; private set; }

        [Inject]
        private void Construct(TestViewInjectingSelf view) => InjectedView = view;
    }

    internal sealed class TestViewInjectingSelf : UIView<TestViewModelInjectingView>
    {
        internal TestViewModelInjectingView VM => ViewModel;

        protected override void BindViewModel(TestViewModelInjectingView vm) { }
    }

    // IViewState fake used directly by UIStateMachine tests, and subclassed per test file (private
    // nested types) for UINavigator/GameLifecycleManager tests. RegisterState<T> keys off the
    // STATIC type of the registered instance, so distinct participants in one test need distinct
    // C# types — subclassing (not reusing one FakeViewState instance/type twice) is required, not
    // just convenient.
    internal class FakeViewState : IViewState
    {
        internal int OnEnterCount { get; private set; }
        internal int OnExitCount { get; private set; }
        internal bool CancelDuringEnter { get; set; }
        internal bool CancelDuringExit { get; set; }
        internal Exception ThrowDuringEnter { get; set; }
        // Invoked synchronously inside OnEnterAsync (after the count increments, before any
        // throw/cancel) so tests can observe collaborator state — e.g. NavigationStack.Count — at
        // the exact moment a transition considers this state "entered".
        internal Action OnEnterCallback { get; set; }

        public UniTask OnEnterAsync(CancellationToken ct = default)
        {
            OnEnterCount++;
            OnEnterCallback?.Invoke();
            if (ThrowDuringEnter != null) throw ThrowDuringEnter;
            if (CancelDuringEnter) throw new OperationCanceledException();
            return UniTask.CompletedTask;
        }

        public UniTask OnExitAsync(CancellationToken ct = default)
        {
            OnExitCount++;
            if (CancelDuringExit) throw new OperationCanceledException();
            return UniTask.CompletedTask;
        }
    }

    internal static class UITestHelpers
    {
        // Bare VContainer root registering only what a view's [Inject] Construct(IUIAnimator) needs
        // to resolve via scope.InjectGameObject. UIRootLayerRefs and IUILoader are deliberately NOT
        // registered here — UIViewFactory takes them as direct constructor arguments in these tests
        // (layers is often literally null; ReparentToLayer no-ops on a null _layers field,
        // UIViewFactory.cs:279 at plan-authoring time). VContainer's RegisterInstance dereferences
        // the instance to read its runtime type (InstanceRegistrationBuilder ctor), which throws
        // NullReferenceException for a null instance — so "no layers" cannot be expressed as a
        // container registration anyway; passing null straight to the constructor is the only way.
        internal static IObjectResolver BuildContainer()
        {
            var builder = new ContainerBuilder();
            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            return builder.Build();
        }
    }
}
