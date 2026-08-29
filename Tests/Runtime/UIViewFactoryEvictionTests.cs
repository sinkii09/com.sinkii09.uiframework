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
    // UIViewFactory.SweepAsync — idle-based cache eviction.
    //
    // Timing is expressed through the graceSeconds PARAMETER, never wall-clock: grace 0 means
    // "evict everything eligible now", grace 999 means "nothing is old enough yet". Both
    // branches are deterministic, so no clock seam and no flaky waits.
    public sealed class UIViewFactoryEvictionTests
    {
        private GameObjectTracker _tracker;
        private FakeUILoader _loader;
        private IObjectResolver _container;
        private UIViewFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _tracker = new GameObjectTracker();
            _loader = new FakeUILoader(_tracker);
            _container = UITestHelpers.BuildContainer();
            _factory = new UIViewFactory(_loader, _container, null);
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            _tracker.DestroyAll();
        }

        private static readonly Func<Type, bool> NoneResident = _ => false;
        private static readonly IReadOnlyList<IUIView> NoLiveViews = Array.Empty<IUIView>();

        // UnityEngine.Object.Destroy is deferred to the end of the frame, so a destroyed-ness
        // assertion straight after SweepAsync would read the object as still alive. Cross a frame
        // boundary first. (DestroyImmediate in the factory would be wrong — it is illegal during
        // physics/animation callbacks.)
        private static UniTask SettleDestroysAsync() => UniTask.NextFrame();

        private async UniTask<TestView> CreateHiddenTestViewAsync()
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var view = await _factory.CreateAsync<TestView, TestViewModel>();
            // CreateCoreAsync leaves the GameObject active; a view that has never been shown (or
            // has been hidden) is what eviction actually targets.
            view.gameObject.SetActive(false);
            return view;
        }

        // --- Core behaviour ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator IdleView_IsDestroyedAndUnloaded() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();

            var evicted = await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);
            await SettleDestroysAsync();

            Assert.AreEqual(1, evicted);
            Assert.IsTrue(view == null, "Evicted view's GameObject should be destroyed.");
            Assert.AreEqual(1, _loader.UnloadCount, "Eviction must release the loader handle.");
        });

        [UnityTest]
        public IEnumerator WithinGracePeriod_IsNotEvicted() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();

            var evicted = await _factory.SweepAsync(NoLiveViews, 999f, NoneResident);

            Assert.AreEqual(0, evicted);
            Assert.IsFalse(view == null);
            Assert.AreEqual(0, _loader.UnloadCount);
        });

        [UnityTest]
        public IEnumerator ResidentPolicy_IsNotEvicted() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();

            var evicted = await _factory.SweepAsync(NoLiveViews, 0f, t => t == typeof(TestView));

            Assert.AreEqual(0, evicted);
            Assert.IsFalse(view == null);
        });

        [UnityTest]
        public IEnumerator EvictedView_NextCreateBuildsFreshInstance() => UniTask.ToCoroutine(async () =>
        {
            var first = await CreateHiddenTestViewAsync();
            await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);

            var second = await _factory.CreateAsync<TestView, TestViewModel>();

            Assert.AreNotSame(first, second);
            Assert.AreEqual(2, _loader.LoadCount, "A re-create after eviction must load again, not hit the cache.");
        });

        // --- Guard conditions -------------------------------------------------------------

        [UnityTest]
        public IEnumerator ViewOnNavigationStack_IsNotEvicted() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();

            var evicted = await _factory.SweepAsync(new IUIView[] { view }, 0f, NoneResident);

            Assert.AreEqual(0, evicted);
            Assert.IsFalse(view == null);
        });

        // Regression: a view created straight from the factory (the HUD-channel pattern, which
        // SetScopeContainer exists to support) is cached but never enters the navigation stack.
        // Eligibility keyed only on "not on stack" would destroy it while it is on screen.
        [UnityTest]
        public IEnumerator VisibleViewOffStack_IsNotEvicted() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var view = await _factory.CreateAsync<TestView, TestViewModel>();
            await view.ShowAsync();

            Assert.IsTrue(view.IsVisible, "Precondition: the view is actually shown.");

            var evicted = await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);

            Assert.AreEqual(0, evicted, "A visible view must survive even though it is not on the stack.");
            Assert.IsFalse(view == null);
        });

        // Regression: CreateCoreAsync fills _cache and clears _pending BEFORE the caller calls
        // ShowAsync, and NavigationStack only adds to its list AFTER ShowAsync completes. During
        // the entrance animation the view is cached, not pending and not live. IsVisible is still
        // false (it is set at the END of ShowAsync), so only activeSelf keeps it alive here.
        [UnityTest]
        public IEnumerator ViewActiveButNotYetVisible_IsNotEvicted() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();
            view.gameObject.SetActive(true);   // what ShowAsync does before awaiting the animation

            Assert.IsFalse(view.IsVisible, "Precondition: mid-entrance means not yet IsVisible.");

            var evicted = await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);

            Assert.AreEqual(0, evicted, "A view mid-entrance must not be destroyed under it.");
            Assert.IsFalse(view == null);
        });

        // Named for what it actually proves. It does NOT exercise the _pending guard: on a fresh
        // load the type isn't in _cache until after the await, and SweepAsync only iterates
        // _cache, so the sweep simply finds nothing. (A type is in both _cache and _pending only
        // during a cache-HIT re-create, whose remaining work is synchronous — hence the _pending
        // check is defence-in-depth, not a reachable path with the current UIView<T>.) What this
        // does prove is real: a sweep running concurrently with a create doesn't corrupt it.
        [UnityTest]
        public IEnumerator SweepDuringInFlightCreate_LeavesItIntact() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            _loader.LoadDelayFrames = 4;

            var pending = _factory.CreateAsync<TestView, TestViewModel>();   // deliberately not awaited

            var evicted = await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);
            Assert.AreEqual(0, evicted);

            var view = await pending;
            Assert.IsFalse(view == null, "The in-flight creation must complete unharmed.");
            Assert.AreEqual(1, _loader.LoadCount);
            Assert.AreEqual(0, _loader.UnloadCount);
        });

        // The re-stamp is what lets the factory avoid a hide/pop hook: a view's idle clock starts
        // when it stops appearing in `live`, not when it was created. Sweep once with the view
        // live (re-stamping it), then again without it — under a grace large enough that only the
        // re-stamped timestamp could keep it alive, it must still be there.
        [UnityTest]
        public IEnumerator LiveView_IdleClockRestartsOnEachSweep() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();

            await _factory.SweepAsync(new IUIView[] { view }, 999f, NoneResident);
            var evicted = await _factory.SweepAsync(NoLiveViews, 999f, NoneResident);

            Assert.AreEqual(0, evicted, "Still inside grace measured from the last time it was live.");
            Assert.IsFalse(view == null);
        });

        // Regression for the live-set being keyed by INSTANCE rather than by view type: _cache is
        // keyed by the requested type, but a prefab root may be a SUBCLASS of it, so
        // instance.GetType() and the cache key differ. A type-keyed live set misses that and
        // evicts a view that is on the stack.
        [UnityTest]
        public IEnumerator LiveSubclassInstance_IsMatchedAgainstItsCacheEntry() => UniTask.ToCoroutine(async () =>
        {
            // FakeUILoader instantiates the registered CONCRETE type while the cache key stays the
            // requested type, which is exactly the mismatch being guarded.
            _loader.RegisterPrefab<DerivedTestView>(nameof(TestView));
            var view = await _factory.CreateAsync<TestView, TestViewModel>();
            view.gameObject.SetActive(false);

            Assert.AreNotEqual(typeof(TestView), view.GetType(), "Precondition: instance type differs from cache key.");

            var evicted = await _factory.SweepAsync(new IUIView[] { view }, 0f, NoneResident);

            Assert.AreEqual(0, evicted, "A live view must be matched by identity, not by its type name.");
            Assert.IsFalse(view == null);
        });

        [UnityTest]
        public IEnumerator OnlyIdleTypesAreEvicted() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            _loader.RegisterPrefab<SecondTestView>(nameof(SecondTestView));

            var idle = await _factory.CreateAsync<TestView, TestViewModel>();
            idle.gameObject.SetActive(false);
            var keep = await _factory.CreateAsync<SecondTestView, SecondTestViewModel>();
            keep.gameObject.SetActive(false);

            var evicted = await _factory.SweepAsync(new IUIView[] { keep }, 0f, NoneResident);
            await SettleDestroysAsync();

            Assert.AreEqual(1, evicted);
            Assert.IsTrue(idle == null);
            Assert.IsFalse(keep == null);
        });

        // --- Handle accounting ------------------------------------------------------------

        // Eviction must clear _cacheKeys alongside _cache. Dispose() iterates _cache, so a
        // leftover _cacheKeys entry is unreachable; and the type must not be unloaded a second
        // time on Dispose after already being unloaded by the sweep.
        [UnityTest]
        public IEnumerator EvictThenRecreateThenDispose_UnloadsOncePerLoad() => UniTask.ToCoroutine(async () =>
        {
            var first = await CreateHiddenTestViewAsync();

            await _factory.SweepAsync(NoLiveViews, 0f, NoneResident);
            Assert.AreEqual(1, _loader.UnloadCount, "Sweep releases the first handle.");

            await _factory.CreateAsync<TestView, TestViewModel>();
            Assert.AreEqual(2, _loader.LoadCount);
            Assert.AreEqual(1, _loader.UnloadCount, "Re-create must not unload anything.");

            _factory.Dispose();
            Assert.AreEqual(2, _loader.UnloadCount, "Dispose releases the second handle — exactly one unload per load.");
        });

        // A destroyed GameObject (scene unload) still owns a real loader handle, so the sweep
        // must clear the entry AND unload rather than skipping it.
        [UnityTest]
        public IEnumerator DestroyedCachedView_IsClearedAndUnloaded() => UniTask.ToCoroutine(async () =>
        {
            var view = await CreateHiddenTestViewAsync();
            UnityEngine.Object.DestroyImmediate(view.gameObject);

            var evicted = await _factory.SweepAsync(NoLiveViews, 999f, NoneResident);

            Assert.AreEqual(1, evicted, "A destroyed entry is swept regardless of grace.");
            Assert.AreEqual(1, _loader.UnloadCount);
        });

        [UnityTest]
        public IEnumerator EmptyCache_SweepIsNoOp() => UniTask.ToCoroutine(async () =>
        {
            Assert.AreEqual(0, await _factory.SweepAsync(NoLiveViews, 0f, NoneResident));
            Assert.AreEqual(0, await _factory.SweepAsync(null, 0f, null));
        });
    }
}
