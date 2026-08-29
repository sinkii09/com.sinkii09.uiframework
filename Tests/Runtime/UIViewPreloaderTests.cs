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
    // UIViewPreloader — warming views into the factory cache ahead of first use.
    public sealed class UIViewPreloaderTests
    {
        private GameObjectTracker _gos;
        private FakeUILoader _loader;
        private UIViewFactory _factory;
        private UIViewPolicyConfig _policyAsset;

        private static readonly UIViewRegistration TestViewReg =
            new(typeof(TestView), typeof(TestViewModel), nameof(TestView));
        private static readonly UIViewRegistration SecondViewReg =
            new(typeof(SecondTestView), typeof(SecondTestViewModel), nameof(SecondTestView));

        [SetUp]
        public void SetUp()
        {
            _gos = new GameObjectTracker();
            _loader = new FakeUILoader(_gos);
            _factory = new UIViewFactory(_loader, UITestHelpers.BuildContainer(), null);
            _policyAsset = ScriptableObject.CreateInstance<UIViewPolicyConfig>();
            _policyAsset.Entries = new List<UIViewPolicyEntry>();
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            if (_policyAsset != null) UnityEngine.Object.DestroyImmediate(_policyAsset);
            _gos.DestroyAll();
        }

        private void Policy(string key, bool preload = false, bool resident = false)
            => _policyAsset.Entries.Add(new UIViewPolicyEntry
            {
                ViewKey = key,
                Policy = new UIViewPolicy { PreloadOnBoot = preload, Resident = resident }
            });

        private UIViewPolicyResolver Resolver(bool withAsset = true)
            => new(withAsset ? _policyAsset : null);

        private UIViewPreloader Build(UIViewPolicyResolver resolver, params UIViewRegistration[] regs)
            => new(_factory, resolver, regs);

        [UnityTest]
        public IEnumerator PreloadAllAsync_WarmsOnlyViewsMarkedPreloadOnBoot() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            _loader.RegisterPrefab<SecondTestView>(nameof(SecondTestView));
            Policy(nameof(TestView), preload: true);   // SecondTestView has no entry at all

            var warmed = await Build(Resolver(), TestViewReg, SecondViewReg).PreloadAllAsync();

            Assert.AreEqual(1, warmed);
            Assert.IsTrue(_factory.IsCachedOrPending(typeof(TestView)));
            Assert.IsFalse(_factory.IsCachedOrPending(typeof(SecondTestView)),
                "A view with no PreloadOnBoot flag must not be warmed.");
            Assert.AreEqual(1, _loader.LoadCount);
        });

        // The entire point of the feature: the first real ShowAsync must not pay the load again.
        [UnityTest]
        public IEnumerator PreloadedThenCreate_ReusesCachedInstanceWithoutReloading() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            Policy(nameof(TestView), preload: true);
            await Build(Resolver(), TestViewReg).PreloadAllAsync();
            Assert.AreEqual(1, _loader.LoadCount, "Precondition: warmed exactly once.");

            var view = await _factory.CreateAsync<TestView, TestViewModel>();

            Assert.IsNotNull(view);
            Assert.AreEqual(1, _loader.LoadCount, "A preloaded view must be served from cache, not reloaded.");
        });

        // A warmed view sits in the cache hidden. UIViewBase.ShowAsync calls SetActive(true)
        // unconditionally, so parking it inactive costs nothing and keeps it off screen until shown.
        [UnityTest]
        public IEnumerator PreloadedView_IsLeftInactive() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            Policy(nameof(TestView), preload: true);
            await Build(Resolver(), TestViewReg).PreloadAllAsync();

            var view = await _factory.CreateAsync<TestView, TestViewModel>();

            Assert.IsFalse(view.gameObject.activeSelf,
                "A warmed view must not be left on screen; only ShowAsync activates it.");
        });

        [UnityTest]
        public IEnumerator PreloadAllAsync_WithNoPolicyAsset_WarmsNothing() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));

            var warmed = await Build(Resolver(withAsset: false), TestViewReg).PreloadAllAsync();

            Assert.AreEqual(0, warmed);
            Assert.AreEqual(0, _loader.LoadCount, "No policy asset means nothing is marked for preload.");
        });

        // The hazard that makes the IsCached probe load-bearing rather than an optimisation:
        // CreateAsync's cache-hit path calls FactoryReset(), disposing the view's scope and
        // ViewModel. Doing that to a view that is currently on screen — and then deactivating it —
        // would tear down a live screen under the player.
        [UnityTest]
        public IEnumerator PreloadAsync_AlreadyCachedView_IsSkippedAndLeftAlone() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            Policy(nameof(TestView), preload: true);
            var live = await _factory.CreateAsync<TestView, TestViewModel>();
            live.gameObject.SetActive(true);

            var warmed = await Build(Resolver(), TestViewReg).PreloadAsync<TestView>();

            Assert.IsFalse(warmed, "An already-cached view is already warm.");
            Assert.AreEqual(1, _loader.LoadCount);
            Assert.IsTrue(live.gameObject.activeSelf,
                "Preloading must never deactivate a view that is already on screen.");
        });

        // The cache is written only at the END of CreateCoreAsync, while _pending is set at the
        // start — so a cache-only probe cannot see a first show that is mid-creation. The preloader
        // would join it through the dedup path and then deactivate a view another caller is in the
        // middle of showing.
        [UnityTest]
        public IEnumerator PreloadAsync_WhileCreationInFlight_IsSkipped() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            Policy(nameof(TestView), preload: true);
            _loader.LoadDelayFrames = 3;

            // An async method body runs synchronously up to its first await, so by the time this
            // returns the creation has already registered itself in _pending and parked on the
            // loader delay. No cache entry exists yet.
            var inFlight = _factory.CreateAsync<TestView, TestViewModel>();

            var warmed = await Build(Resolver(), TestViewReg).PreloadAsync<TestView>();
            var view = await inFlight;

            Assert.IsFalse(warmed, "A creation already in flight is already warming this type.");
            Assert.AreEqual(1, _loader.LoadCount, "The preloader must not start a second load.");
            Assert.IsTrue(view.gameObject.activeSelf,
                "Preload must not deactivate a view another caller is in the middle of showing.");
        });

        [UnityTest]
        public IEnumerator PreloadAsync_UnregisteredType_LogsErrorAndReturnsFalse() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.Expect(LogType.Error, new Regex("not in UIViewRegistry"));

            var warmed = await Build(Resolver()).PreloadAsync<TestView>();

            Assert.IsFalse(warmed);
            Assert.AreEqual(0, _loader.LoadCount);
        });

        // One unloadable prefab is an authoring error, not a reason to abandon the rest of the
        // warm-up — the game would then silently pay full load cost for every other view too.
        [UnityTest]
        public IEnumerator PreloadAllAsync_OneViewFails_StillWarmsTheRest() => UniTask.ToCoroutine(async () =>
        {
            // TestView is deliberately NOT registered with the loader, so its load throws.
            _loader.RegisterPrefab<SecondTestView>(nameof(SecondTestView));
            Policy(nameof(TestView), preload: true);
            Policy(nameof(SecondTestView), preload: true);
            LogAssert.Expect(LogType.Error, new Regex("Failed to preload TestView"));

            // TestView is first in the list, so the failure happens before the one that must survive.
            var warmed = await Build(Resolver(), TestViewReg, SecondViewReg).PreloadAllAsync();

            Assert.AreEqual(1, warmed);
            Assert.IsFalse(_factory.IsCachedOrPending(typeof(TestView)));
            Assert.IsTrue(_factory.IsCachedOrPending(typeof(SecondTestView)),
                "A failure on one view must not abort the remaining preloads.");
        });

        // [RC-5] PreloadOnBoot implies Resident. Without that, the sweeper would destroy exactly
        // what preload just warmed and the feature would be self-defeating.
        [UnityTest]
        public IEnumerator PreloadedView_IsResidentAndSurvivesSweep() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            Policy(nameof(TestView), preload: true, resident: false);
            var resolver = Resolver();
            await Build(resolver, TestViewReg).PreloadAllAsync();
            Assert.IsTrue(_factory.IsCachedOrPending(typeof(TestView)), "Precondition: warmed.");

            // grace 0 = evict everything eligible right now (the inverse of the config's 0 = off).
            var evicted = await _factory.SweepAsync(Array.Empty<IUIView>(), 0f, resolver.IsResident);

            Assert.AreEqual(0, evicted);
            Assert.IsTrue(_factory.IsCachedOrPending(typeof(TestView)),
                "PreloadOnBoot must imply Resident, or the sweep undoes the warm-up.");
        });

        // Cancellation is the caller's business (boot aborted), not a preload failure — it must
        // propagate rather than be logged and swallowed like a bad prefab.
        [UnityTest]
        public IEnumerator PreloadAsync_Cancelled_PropagatesInsteadOfLogging() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var cancelled = false;
            try { await Build(Resolver(), TestViewReg).PreloadAsync<TestView>(cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }

            Assert.IsTrue(cancelled, "A cancelled preload must surface as OperationCanceledException.");
            Assert.IsFalse(_factory.IsCachedOrPending(typeof(TestView)));
        });
    }
}
