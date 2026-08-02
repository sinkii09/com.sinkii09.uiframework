using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the animation/transition-hardening cluster's WARNING #3:
    // CanvasGroup.alpha is documented as animator-owned (UITransition.cs), but DOTweenUIAnimator only
    // actually set it on the null-transition and cancellation paths. FadeTransition/ZoomOutFadeTransition
    // drive alpha themselves via DOFade, but ScaleTransition/SlideTransition never touch it — so mixing
    // e.g. a Fade hide with a Scale show left alpha stuck at whatever the last Fade left it, forever.
    // See plans/260802-1358-animation-transition-hardening/plan.md, Finding 3.
    // Deliberately never names a DOTween type — the test asmdef does not reference DOTween.dll.
    public class DOTweenUIAnimatorAlphaNormalizationTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        [UnityTest]
        public IEnumerator ShowAsync_ScaleTransition_NormalizesAlphaToOne_EvenIfPreviouslyFadedOut() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("AlphaShowView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            view.CanvasGroup.alpha = 0f; // simulates state a prior FadeTransition hide left behind
            var transition = ScriptableObject.CreateInstance<ScaleTransition>();
            transition.Duration = 0.05f;
            var animator = new DOTweenUIAnimator();

            // ScaleTransition never touches alpha itself — before the fix this stayed 0 forever;
            // the view would scale into place but remain invisible.
            await animator.ShowAsync(view, transition, CancellationToken.None);

            Assert.AreEqual(1f, view.CanvasGroup.alpha);
        });

        [UnityTest]
        public IEnumerator HideAsync_ScaleTransition_NormalizesAlphaToZero() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("AlphaHideView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            view.CanvasGroup.alpha = 1f;
            var transition = ScriptableObject.CreateInstance<ScaleTransition>();
            transition.Duration = 0.05f;
            var animator = new DOTweenUIAnimator();

            await animator.HideAsync(view, transition, CancellationToken.None);

            Assert.AreEqual(0f, view.CanvasGroup.alpha);
        });
    }
}
