using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the 2026-10-17 animation review (plan: t-i-ko-n-m-r-curried-cherny):
    // C1 Scale/Slide shows played invisible after a hide, W1 a foreign kill on the view root cancelled
    // the show, W3 a cancelled hide unlocked input, W5 TabIndicator froze at timeScale 0.
    // Deliberately never names a DOTween type — the test asmdef does not reference DOTween.dll.
    public class TransitionAlphaAndOwnershipTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            _gos.DestroyAll();
        }

        private TestView CreateView(string name)
        {
            var view = new GameObject(name, typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            return view;
        }

        private static T Transition<T>(float duration) where T : UITransition
        {
            var t = ScriptableObject.CreateInstance<T>();
            t.Duration = duration;
            return t;
        }

        [UnityTest]
        public IEnumerator ScaleShow_AfterHideLeftAlphaZero_IsVisibleMidTween() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("ScaleAlphaView");
            view.CanvasGroup.alpha = 0f; // what every hide leaves behind
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var showTask = animator.ShowAsync(view, Transition<ScaleTransition>(5f), cts.Token);
            await UniTask.DelayFrame(2);

            Assert.AreEqual(1f, view.CanvasGroup.alpha, "Scale show must be visible while it plays, not pop in at the end");
            cts.Cancel();
            await CaptureAsync(async () => await showTask);
        });

        [UnityTest]
        public IEnumerator SlideShow_AfterHideLeftAlphaZero_IsVisibleMidTween() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("SlideAlphaView");
            view.CanvasGroup.alpha = 0f;
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var showTask = animator.ShowAsync(view, Transition<SlideTransition>(5f), cts.Token);
            await UniTask.DelayFrame(2);

            Assert.AreEqual(1f, view.CanvasGroup.alpha);
            cts.Cancel();
            await CaptureAsync(async () => await showTask);
        });

        // Documents a KNOWN limitation, not desired behaviour: a Fade child's From(0) applies when the
        // sequence is built, so the Scale leg before it still plays at alpha 0. Use ZoomOutFade or Pop
        // for scale+fade. If this starts failing, the limitation was fixed — update the vault note.
        [UnityTest, Category("KnownLimitation")]
        public IEnumerator SequenceScaleThenFade_ScaleLegPlaysAtFadeStartAlpha() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("SeqAlphaView");
            var sequence = ScriptableObject.CreateInstance<SequenceTransition>();
            sequence.Transitions = new UITransition[] { Transition<ScaleTransition>(5f), Transition<FadeTransition>(5f) };
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var showTask = animator.ShowAsync(view, sequence, cts.Token);
            await UniTask.DelayFrame(2);

            Assert.Less(view.CanvasGroup.alpha, 0.01f);
            cts.Cancel();
            await CaptureAsync(async () => await showTask);
        });

        [UnityTest]
        public IEnumerator PunchOnViewRoot_MidShow_WarnsAndDoesNotCancelShow() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("PunchRootView");
            var animator = new DOTweenUIAnimator();
            var showTask = animator.ShowAsync(view, Transition<ScaleTransition>(0.3f), CancellationToken.None);
            await UniTask.DelayFrame(2);

            LogAssert.Expect(LogType.Warning, new Regex("targets a UIViewBase root"));
            var punch = view.gameObject.AddComponent<PunchFeedback>();
            punch.PunchScale(); // DOKill(root transform) — used to kill the show tween outright

            var error = await CaptureAsync(async () => await showTask);
            Assert.IsNull(error, "a juice kill on the root must not read as a cancelled show");
            Assert.AreEqual(1f, view.CanvasGroup.alpha);
            // The show tween survives the kill and lands the root at rest. Only holds while the show
            // outlasts the punch — which is exactly why the warning exists.
            Assert.AreEqual(Vector3.one, view.transform.localScale);
        });

        [UnityTest]
        public IEnumerator HideCancelled_LeavesInputLocked() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("HideCancelView");
            view.CanvasGroup.interactable = true;
            view.CanvasGroup.blocksRaycasts = true;
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var hideTask = animator.HideAsync(view, Transition<SlideTransition>(5f), cts.Token);
            await UniTask.DelayFrame(2);
            cts.Cancel();

            var error = await CaptureAsync(async () => await hideTask);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsFalse(view.CanvasGroup.interactable);
            Assert.IsFalse(view.CanvasGroup.blocksRaycasts);
        });

        [UnityTest]
        public IEnumerator ShowCancelled_ThenShownAgain_IsVisibleMidTween() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView("ReShowView");
            var animator = new DOTweenUIAnimator();
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();

            // A cancelled show leaves alpha 0 — the second show must not play invisible from there.
            var firstTask = animator.ShowAsync(view, Transition<ScaleTransition>(5f), first.Token);
            await UniTask.DelayFrame(2);
            first.Cancel();
            await CaptureAsync(async () => await firstTask);
            Assert.AreEqual(0f, view.CanvasGroup.alpha, "precondition: cancel path hides the view");

            var secondTask = animator.ShowAsync(view, Transition<ScaleTransition>(5f), second.Token);
            await UniTask.DelayFrame(2);

            Assert.AreEqual(1f, view.CanvasGroup.alpha);
            second.Cancel();
            await CaptureAsync(async () => await secondTask);
        });

        private TabIndicator CreateTabIndicator(out RectTransform indicator, out RectTransform target)
        {
            var root = _gos.Track(new GameObject("Tabs", typeof(RectTransform)));
            indicator = new GameObject("Indicator", typeof(RectTransform)).GetComponent<RectTransform>();
            indicator.SetParent(root.transform, false);
            target = new GameObject("Tab", typeof(RectTransform)).GetComponent<RectTransform>();
            target.SetParent(root.transform, false);
            target.anchoredPosition = new Vector2(100f, 0f);

            var tabIndicator = root.AddComponent<TabIndicator>();
            typeof(TabIndicator)
                .GetField("_indicator", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(tabIndicator, indicator);
            return tabIndicator;
        }

        [UnityTest]
        public IEnumerator TabIndicator_MovesWhileTimeScaleZero() => UniTask.ToCoroutine(async () =>
        {
            var tabs = CreateTabIndicator(out var indicator, out var target);
            Time.timeScale = 0f; // pause menu

            tabs.MoveTo(target);
            await WaitRealtime(0.35f); // default duration 0.2s

            Assert.AreEqual(target.anchoredPosition.x, indicator.anchoredPosition.x, 0.01f);
        });
    }
}
