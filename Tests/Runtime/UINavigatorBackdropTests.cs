using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // UINavigator <-> UIBackdrop integration, focused on the paths where the backdrop could be
    // left covering the screen with nothing above it — the softlock class of bug.
    public sealed class UINavigatorBackdropTests
    {
        private GameObjectTracker _gos;
        private FakeUILoader _loader;
        private UIViewPolicyConfig _policyAsset;
        private UIBackdrop _backdrop;
        private UIFrameworkConfig _config;

        [SetUp]
        public void SetUp()
        {
            _gos = new GameObjectTracker();
            _loader = new FakeUILoader(_gos);

            _policyAsset = ScriptableObject.CreateInstance<UIViewPolicyConfig>();
            _policyAsset.Entries = new List<UIViewPolicyEntry>
            {
                new UIViewPolicyEntry
                {
                    ViewKey = nameof(TestView),
                    Policy = new UIViewPolicy { NeedsBackdrop = true }
                }
            };

            _config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            _backdrop = new UIBackdrop(new UIViewPolicyResolver(_policyAsset), null);
        }

        [TearDown]
        public void TearDown()
        {
            _backdrop.Dispose();
            if (_policyAsset != null) UnityEngine.Object.DestroyImmediate(_policyAsset);
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
            _gos.DestroyAll();
        }

        private bool BackdropVisible =>
            _backdrop.InstanceForTests != null && _backdrop.InstanceForTests.activeSelf;

        // Unlike most fixtures in this suite, this one needs REAL layers. UIViewFactory's
        // ReparentToLayer no-ops when _layers is null, which leaves every created view parentless
        // — and UIBackdrop deliberately refuses to attach to an unparented view (see
        // UIBackdropTests.ViewWithNoParent_HidesInsteadOfAttachingToRoot). Passing null here made
        // every "backdrop is up" assertion fail and every "backdrop is down" assertion pass
        // vacuously, for a reason that had nothing to do with the navigator.
        private UIRootLayerRefs BuildLayers()
        {
            // Canvas + GraphicRaycaster because BlockLayersBelow warns on a layer that has no
            // raycaster; the unassigned layers stay null and are skipped.
            var screen = _gos.Track(new GameObject(
                "ScreenLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            return new UIRootLayerRefs { Screen = screen.transform };
        }

        private UINavigator BuildNavigator(int maxDepth, params UIViewRegistration[] registrations)
        {
            _config.MaxNavigationDepth = maxDepth;
            var container = UITestHelpers.BuildContainer();
            var layers = BuildLayers();
            var factory = new UIViewFactory(_loader, container, layers);
            return new UINavigator(new NavigationStack(_config), new UIStateMachine(),
                                   factory, registrations, layers, _backdrop);
        }

        private static UIViewRegistration Reg<TView, TVm>() =>
            new UIViewRegistration(typeof(TView), typeof(TVm), typeof(TView).Name);

        [UnityTest]
        public IEnumerator ShowingBackdropView_RaisesBackdrop() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var nav = BuildNavigator(10, Reg<TestView, TestViewModel>());

            await nav.ShowAsync<TestView>();

            Assert.IsTrue(BackdropVisible);
        });

        [UnityTest]
        public IEnumerator PoppingLastView_HidesBackdrop() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var nav = BuildNavigator(10, Reg<TestView, TestViewModel>());

            await nav.ShowAsync<TestView>();
            Assert.IsTrue(BackdropVisible, "Precondition: backdrop is up.");

            await nav.PopAsync();

            Assert.IsFalse(BackdropVisible, "An empty stack must leave no dim on screen.");
        });

        // The regression this commit exists to prevent. PushAsync declines at max depth by warning
        // and returning — it does NOT throw — so without a post-push re-refresh the navigator would
        // leave blocking (and now a full-screen raycast-blocking dim) applied for a view that never
        // made it onto the stack, with no way to dismiss it.
        [UnityTest]
        public IEnumerator PushDeclinedAtMaxDepth_DoesNotStrandBackdrop() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<SecondTestView>(nameof(SecondTestView));
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var nav = BuildNavigator(1,
                Reg<SecondTestView, SecondTestViewModel>(),
                Reg<TestView, TestViewModel>());

            await nav.ShowAsync<SecondTestView>();       // fills the stack (no backdrop policy)
            Assert.IsFalse(BackdropVisible);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("depth"));
            await nav.ShowAsync<TestView>();             // declined — stack is full

            Assert.IsFalse(BackdropVisible,
                "A declined push must not leave a full-screen dim over a view that was never pushed.");
        });

        [UnityTest]
        public IEnumerator FailedCreate_DoesNotStrandBackdrop() => UniTask.ToCoroutine(async () =>
        {
            _loader.RegisterPrefab<TestView>(nameof(TestView));
            var nav = BuildNavigator(10, Reg<TestView, TestViewModel>());
            _loader.LoadError = new InvalidOperationException("boom");

            try { await nav.ShowAsync<TestView>(); }
            catch (InvalidOperationException) { /* expected — the original exception must survive */ }

            Assert.IsFalse(BackdropVisible);
        });
    }
}
