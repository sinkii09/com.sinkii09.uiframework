using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the C3 finding: DOTween's Tween.OnKill setter is overwritten by
    // TweenExtensions.AwaitAsync (which owns OnComplete/OnKill for every tween it awaits — DOTween
    // setters assign, they do not chain), so a transition's restore-on-cancel logic can never live
    // on the tween itself. It must run from DOTweenUIAnimator's catch(OperationCanceledException)
    // block via UITransition.RestoreOnCancel. See plans/260801-2148-correctness-cluster/
    // phase-03-transition-cancel-restore.md.
    // Deliberately never names a DOTween type — the test asmdef does not reference DOTween.dll.
    public class TransitionCancelRestoreTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        [UnityTest]
        public IEnumerator ScaleTransition_ShowCancelled_RestoresRestingScale() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("ScaleView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            var transition = ScriptableObject.CreateInstance<ScaleTransition>();
            transition.Duration = 5f; // long enough that a 2-frame delay always lands mid-tween
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            // C3-1: the C3 regression. Cancel mid-tween and assert the transform lands back on its
            // resting pose via RestoreOnCancel, not wherever DOTween's Kill() happened to leave it.
            var showTask = animator.ShowAsync(view, transition, cts.Token);
            await UniTask.DelayFrame(2);
            cts.Cancel();

            var error = await CaptureAsync(async () => await showTask);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreEqual(Vector3.one, view.transform.localScale);
        });

        [UnityTest]
        public IEnumerator SlideTransition_HideCancelled_RestoresRestingPosition() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("SlideView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            var transition = ScriptableObject.CreateInstance<SlideTransition>();
            transition.Duration = 5f;
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            // C3-2: same guarantee as C3-1 for the position-based transition — RestoreOnCancel
            // resets anchoredPosition back to its resting pose (Vector2.zero) regardless of how far
            // through the slide the cancellation landed.
            var hideTask = animator.HideAsync(view, transition, cts.Token);
            await UniTask.DelayFrame(2);
            cts.Cancel();

            var error = await CaptureAsync(async () => await hideTask);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreEqual(Vector2.zero, view.RectTransform.anchoredPosition);
        });

        [Test]
        public void SequenceTransition_RestoreOnCancel_ForwardsToChildren()
        {
            var view = new GameObject("SeqView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            view.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            view.RectTransform.anchoredPosition = new Vector2(42f, 0f);

            var scale = ScriptableObject.CreateInstance<ScaleTransition>();
            var slide = ScriptableObject.CreateInstance<SlideTransition>();
            var sequence = ScriptableObject.CreateInstance<SequenceTransition>();
            sequence.Transitions = new UITransition[] { scale, slide };

            // C3-3: nested transitions must not be silently skipped — SequenceTransition has no
            // transform state of its own, so RestoreOnCancel must forward to every child instead of
            // being a no-op inherited from the UITransition base.
            sequence.RestoreOnCancel(view);

            Assert.AreEqual(Vector3.one, view.transform.localScale);
            Assert.AreEqual(Vector2.zero, view.RectTransform.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator ShowAsync_CompletesNormally_DoesNotRestore() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("NoRestoreView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            var transition = ScriptableObject.CreateInstance<ScaleTransition>();
            transition.Duration = 0.05f;
            var animator = new DOTweenUIAnimator();

            // C3-4: guards the other half of the fix. RestoreOnCancel — and the
            // interactable/blocksRaycasts=false reset — only happen inside the
            // catch(OperationCanceledException) branch; asserting the SUCCESS branch's
            // postconditions here proves that branch (and therefore RestoreOnCancel) never ran.
            // The old Tween.OnKill(...) restore fired on every completion, including success — this
            // is the regression test for that half.
            await animator.ShowAsync(view, transition, CancellationToken.None);

            Assert.AreEqual(Vector3.one, view.transform.localScale);
            Assert.IsTrue(view.CanvasGroup.interactable);
            Assert.IsTrue(view.CanvasGroup.blocksRaycasts);
        });
    }
}
