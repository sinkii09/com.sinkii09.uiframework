using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // TooltipService's state machine is driven by Tick() off unscaled delta time rather than by
    // awaited delays, so these are plain synchronous [Test]s: set the durations to zero, call
    // Tick(), assert. No wall-clock anywhere, and nothing to flake.
    public class TooltipServiceTests
    {
        private GameObjectTracker _tracker;
        private IObjectResolver _container;
        private UIFrameworkConfig _config;
        private UIRootLayerRefs _layers;
        private FakeTransitionOverlay _overlay;
        private TestTooltipView _view;

        [SetUp]
        public void SetUp()
        {
            _tracker = new GameObjectTracker();
            _container = UITestHelpers.BuildContainer();
            _config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            _overlay = new FakeTransitionOverlay();

            var tooltipLayer = _tracker.Track(new GameObject(
                "TooltipLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            var overlayLayer = _tracker.Track(new GameObject(
                "OverlayLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));

            _layers = new UIRootLayerRefs
            {
                Tooltip = tooltipLayer.transform,
                Overlay = overlayLayer.transform,
            };

            _view = _tracker.Track(new GameObject(
                "TooltipView", typeof(RectTransform), typeof(CanvasGroup))
                .AddComponent<TestTooltipView>());
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate, not the tracker's deferred Destroy: PlayMode shares one scene and
            // these are synchronous [Test]s, so a deferred view is still alive when the next
            // SetUp runs. TooltipService.Initialize would then find two views under the empty key,
            // log a duplicate-key error, and keep the stale one — every later assertion would be
            // checking a view that was never bound.
            if (_view != null) UnityEngine.Object.DestroyImmediate(_view.gameObject);
            _tracker.DestroyAll();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        private TooltipService BuildService(float showDelay = 0f, float hideGrace = 0f,
            IUIAnimator animator = null)
        {
            var resolver = _container;
            if (animator != null)
            {
                // The shared container registers the real DOTweenUIAnimator, which short-circuits
                // on a null transition. Tests that need a genuinely in-flight animation supply
                // their own.
                var builder = new ContainerBuilder();
                builder.RegisterInstance(animator).As<IUIAnimator>();
                resolver = builder.Build();
            }

            var service = new TooltipService(_layers, _config, resolver, _overlay);
            service.Initialize();
            service._showDelay = showDelay;
            service._hideGrace = hideGrace;
            // Zeroed so a test never gets a free instant show it did not ask for.
            service._reShowWindow = 0f;
            return service;
        }

        private RectTransform NewAnchor(string name = "Anchor")
            => (RectTransform)_tracker.Track(new GameObject(name, typeof(RectTransform))).transform;

        private static TooltipRequest Request(RectTransform anchor, TooltipSource source)
            => new(anchor, new TooltipContent { Title = "T" }, TooltipPlacement.Auto, source);

        [Test]
        public void Show_HoverSource_StaysPendingUntilDelayElapses()
        {
            var service = BuildService(showDelay: 10f);
            service.Show(Request(NewAnchor(), TooltipSource.Hover));

            Assert.IsFalse(service.IsShown, "Hover must wait out the show delay.");
            service.Tick();
            Assert.IsFalse(service.IsShown, "One tick of unscaled time must not clear a 10s delay.");
        }

        [Test]
        public void Show_ClickSource_ShowsWithoutWaitingForDelay()
        {
            var service = BuildService(showDelay: 10f);
            service.Show(Request(NewAnchor(), TooltipSource.Click));

            // A click is a deliberate act; any delay reads as lag.
            Assert.IsTrue(service.IsShown);
        }

        [Test]
        public void Show_FocusSource_ShowsWithoutWaitingForDelay()
        {
            var service = BuildService(showDelay: 10f);
            service.Show(Request(NewAnchor(), TooltipSource.Focus));
            Assert.IsTrue(service.IsShown);
        }

        [Test]
        public void Show_HoverWithZeroDelay_ShowsOnNextTick()
        {
            var service = BuildService();
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Hover));
            service.Tick();

            Assert.IsTrue(service.IsShown);
            Assert.AreSame(anchor, service.CurrentAnchor);
            Assert.AreEqual(1, _view.BindCount);
        }

        [Test]
        public void Hide_FromAnAnchorThatIsNotCurrent_IsIgnored()
        {
            var service = BuildService();
            var shown = NewAnchor("Shown");
            service.Show(Request(shown, TooltipSource.Click));

            // Moving between two triggers fires the new enter before the old exit; hiding on that
            // stale exit would kill the tooltip that was just shown.
            service.Hide(NewAnchor("Stale"));

            Assert.IsTrue(service.IsShown);
            Assert.AreSame(shown, service.CurrentAnchor);
        }

        [Test]
        public void Tick_AnchorDestroyed_HidesWithoutAnExitEvent()
        {
            var service = BuildService();
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Click));

            // A consumed item sends no OnPointerExit.
            UnityEngine.Object.DestroyImmediate(anchor.gameObject);
            service.Tick();

            Assert.IsFalse(service.IsShown);
        }

        [Test]
        public void Tick_AnchorDeactivated_HidesWithoutAnExitEvent()
        {
            var service = BuildService();
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Click));

            // CellPool.Return deactivates a recycled cell — also no exit event.
            anchor.gameObject.SetActive(false);
            service.Tick();

            Assert.IsFalse(service.IsShown);
        }

        [Test]
        public void Show_WhileTransitionCurtainIsUp_IsRefused()
        {
            var service = BuildService();
            _overlay.IsShown = true;

            service.Show(Request(NewAnchor(), TooltipSource.Click));

            // The curtain stays up for the whole load and never blocks lower raycasters, so a
            // hover during it would otherwise pop a tooltip above the curtain with nothing to clear it.
            Assert.IsFalse(service.IsShown);
        }

        [Test]
        public void Tick_CurtainRaisedWhileShown_HidesTheTooltip()
        {
            var service = BuildService();
            service.Show(Request(NewAnchor(), TooltipSource.Click));
            Assert.IsTrue(service.IsShown);

            _overlay.IsShown = true;
            service.Tick();

            Assert.IsFalse(service.IsShown);
        }

        [Test]
        public void SecondAnchor_StealsTheTooltipFromTheFirst()
        {
            var service = BuildService();
            var first = NewAnchor("First");
            var second = NewAnchor("Second");

            service.Show(Request(first, TooltipSource.Click));
            service.Show(Request(second, TooltipSource.Click));

            Assert.AreSame(second, service.CurrentAnchor);
            Assert.AreEqual(2, _view.BindCount, "Stealing must rebind, not reuse stale content.");
        }

        [Test]
        public void HideImmediate_ClearsTheAnchorWithoutWaitingOutTheGrace()
        {
            var service = BuildService(hideGrace: 10f);
            service.Show(Request(NewAnchor(), TooltipSource.Click));

            service.HideImmediate();

            Assert.IsFalse(service.IsShown);
            Assert.IsNull(service.CurrentAnchor);
        }

        [Test]
        public void Hide_WithGraceRemaining_KeepsTheTooltipShown()
        {
            var service = BuildService(hideGrace: 10f);
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Click));

            service.Hide(anchor);
            service.Tick();

            Assert.IsTrue(service.IsShown, "Grace keeps the tooltip up so a pointer can return to it.");
        }

        [Test]
        public void Hide_WithZeroGrace_HidesOnTheNextTick()
        {
            var service = BuildService(hideGrace: 0f);
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Click));

            service.Hide(anchor);
            service.Tick();

            Assert.IsFalse(service.IsShown);
        }

        // A repeated Hide from the same anchor must not re-arm the grace timer — a pointer sitting
        // just outside the widget can raise several exits, and each one restarting the countdown
        // would keep a dead tooltip alive indefinitely.
        [Test]
        public void Hide_CalledTwice_DoesNotRestartTheGraceCountdown()
        {
            var service = BuildService(hideGrace: 10f);
            var anchor = NewAnchor();
            service.Show(Request(anchor, TooltipSource.Click));

            service.Hide(anchor);
            service._hideGrace = 0f;   // a later Hide must not adopt the new, shorter grace either
            service.Hide(anchor);
            service.Tick();

            Assert.IsTrue(service.IsShown, "The second Hide must be a no-op while already in grace.");
        }

        [Test]
        public void Show_WithNoTooltipLayer_FallsBackToTheOverlayLayer()
        {
            _layers.Tooltip = null;
            var service = BuildService();

            // UIRootLayerRefs serialises by field name, so a pre-v1.7 UIRoot deserialises Tooltip
            // as null. Falling back beats failing invisibly — SetLayerInteractable returns silently
            // on a null transform.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "no Tooltip layer.*falling back to Overlay"));

            service.Show(Request(NewAnchor(), TooltipSource.Click));

            Assert.IsTrue(service.IsShown);
            Assert.AreSame(_layers.Overlay, _view.transform.parent);
        }

        // LayoutRebuilder.ForceRebuildLayoutImmediate strips disabled behaviours, so a
        // ContentSizeFitter on an inactive GameObject never runs. Binding and measuring before
        // activation would size every appearance against the PREVIOUS payload.
        [Test]
        public void Show_ActivatesTheViewBeforeBindingSoLayoutCanMeasure()
        {
            var service = BuildService();
            service.Show(Request(NewAnchor(), TooltipSource.Click));

            Assert.IsTrue(_view.ActiveOnBind,
                "The view must be active before Bind so layout can actually rebuild.");
        }

        // The C2 regression only exists while a hide ANIMATION is still running: with no transition
        // assigned the animator returns synchronously, the queue is never occupied, and a show can
        // never land mid-hide. So this test supplies an animator whose hide can be held open —
        // without that it would pass against the broken code and prove nothing.
        [UnityTest]
        public IEnumerator ReShow_DuringAnUnfinishedHideAnimation_LeavesTheViewActive() =>
            UniTask.ToCoroutine(async () =>
            {
                var animator = new GatedAnimator();
                var service = BuildService(animator: animator);
                var anchor = NewAnchor();

                service.Show(Request(anchor, TooltipSource.Click));
                await WaitUntilFrames(() => _view.gameObject.activeSelf);

                animator.HoldHide = true;
                service.HideImmediate();
                await WaitUntilFrames(() => animator.HideInFlight);

                // Lands while the hide animation is still running.
                service.Show(Request(anchor, TooltipSource.Click));
                animator.ReleaseHide();

                await WaitUntilFrames(() => _view.gameObject.activeSelf);

                // Before the fix: the show skipped ShowAsync (the in-flight hide had not cleared
                // IsVisible yet) and the hide's tail then deactivated the view — state Shown,
                // GameObject inactive, invisible with nothing left to revive it.
                Assert.IsTrue(service.IsShown);
                Assert.IsTrue(_view.gameObject.activeSelf, "A re-show must leave the view active.");
            });

        [Test]
        public void Show_WithAnUnknownViewKey_TearsDownTheCurrentTooltip()
        {
            var service = BuildService();
            service.Show(Request(NewAnchor(), TooltipSource.Click));
            Assert.IsTrue(service.IsShown);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "No tooltip view registered under key 'nope'"));

            service.Show(new TooltipRequest(NewAnchor(), new KeyedPayload("nope"),
                TooltipPlacement.Auto, TooltipSource.Click));

            // The state assertions alone are NOT enough — a bare Reset() already produced both.
            // The actual defect was _active staying set and the view staying on screen with the
            // watchdog switched off, so the activeSelf check is what really pins this.
            Assert.IsFalse(service.IsShown);
            Assert.IsNull(service.CurrentAnchor);
            Assert.IsFalse(_view.gameObject.activeSelf,
                "A failed show must tear the previous tooltip down, not merely forget about it.");
        }

        [Test]
        public void HideImmediate_DoesNotGrantAnInstantReShowWindow()
        {
            var service = BuildService(showDelay: 10f);
            service._reShowWindow = 10f;
            service.Show(Request(NewAnchor(), TooltipSource.Click));

            service.HideImmediate();
            service.Show(Request(NewAnchor(), TooltipSource.Hover));

            Assert.IsFalse(service.IsShown,
                "A navigation teardown must not let the next screen raise tooltips with zero dwell.");
        }

        // The service is already Idle by this point, so HideImmediate takes its early-out. It must
        // still revoke the window the grace-hide stamped a moment earlier, or navigating away
        // right after a tooltip closes hands the next screen a free zero-dwell hover.
        [Test]
        public void HideImmediate_AfterAGraceHide_StillRevokesTheReShowWindow()
        {
            var service = BuildService(showDelay: 10f, hideGrace: 0f);
            service._reShowWindow = 10f;
            var anchor = NewAnchor();

            service.Show(Request(anchor, TooltipSource.Click));
            service.Hide(anchor);
            service.Tick();
            Assert.IsFalse(service.IsShown);

            service.HideImmediate();
            service.Show(Request(NewAnchor(), TooltipSource.Hover));

            Assert.IsFalse(service.IsShown);
        }

        [Test]
        public void HideAfterGrace_GrantsTheInstantReShowWindow()
        {
            var service = BuildService(showDelay: 10f, hideGrace: 0f);
            service._reShowWindow = 10f;
            var anchor = NewAnchor();

            service.Show(Request(anchor, TooltipSource.Click));
            service.Hide(anchor);
            service.Tick();
            Assert.IsFalse(service.IsShown);

            service.Show(Request(NewAnchor(), TooltipSource.Hover));

            Assert.IsTrue(service.IsShown,
                "Sweeping to a neighbour just after a hide must show instantly, not re-wait the dwell.");
        }

        // Guards the UILayer ordinal after inserting Tooltip between Popup and Overlay: a tooltip
        // raised from inside a modal popup must stay interactive.
        [Test]
        public void BlockLayersBelow_Popup_LeavesTheTooltipLayerInteractive()
        {
            var popup = _tracker.Track(new GameObject(
                "PopupLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            var screen = _tracker.Track(new GameObject(
                "ScreenLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            _layers.Popup = popup.transform;
            _layers.Screen = screen.transform;

            _layers.BlockLayersBelow(UILayer.Popup);

            Assert.IsTrue(_layers.Tooltip.GetComponent<GraphicRaycaster>().enabled,
                "Tooltip sorts above Popup, so it must remain interactive under a modal.");
            Assert.IsFalse(screen.GetComponent<GraphicRaycaster>().enabled);
        }

        private sealed class TestTooltipView : TooltipViewBase
        {
            public int BindCount;
            public ITooltipPayload LastPayload;
            public bool ActiveOnBind;

            public override void Bind(ITooltipPayload payload)
            {
                BindCount++;
                LastPayload = payload;
                ActiveOnBind = gameObject.activeInHierarchy;
            }
        }

        private sealed class KeyedPayload : ITooltipPayload
        {
            public KeyedPayload(string key) => ViewKey = key;
            public string ViewKey { get; }
        }

        private static async UniTask WaitUntilFrames(Func<bool> condition, int maxFrames = 60)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                await UniTask.Yield();

            Assert.IsTrue(condition(), $"Condition not met within {maxFrames} frames.");
        }

        // Show completes instantly; Hide can be held open so a show can genuinely land mid-hide.
        private sealed class GatedAnimator : IUIAnimator
        {
            public bool HoldHide;
            public bool HideInFlight { get; private set; }

            private UniTaskCompletionSource _gate;

            public UniTask ShowAsync(IUIView view, UITransition transition, CancellationToken ct = default)
                => UniTask.CompletedTask;

            public async UniTask HideAsync(IUIView view, UITransition transition, CancellationToken ct = default)
            {
                if (!HoldHide) return;

                _gate = new UniTaskCompletionSource();
                HideInFlight = true;
                try { await _gate.Task; }
                finally { HideInFlight = false; }
            }

            public void ReleaseHide()
            {
                HoldHide = false;
                _gate?.TrySetResult();
            }
        }

        private sealed class FakeTransitionOverlay : ITransitionOverlay
        {
            public bool IsShown { get; set; }

            public UniTask ShowAsync(CancellationToken ct = default)
            {
                IsShown = true;
                return UniTask.CompletedTask;
            }

            public UniTask HideAsync(CancellationToken ct = default)
            {
                IsShown = false;
                return UniTask.CompletedTask;
            }
        }
    }
}
