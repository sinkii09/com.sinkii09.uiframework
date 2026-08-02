using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the animation/transition-hardening cluster's WARNING #2: DOTweenUIAnimator
    // used to restore CanvasGroup.interactable/blocksRaycasts = true immediately after its own tween
    // (or synchronously, for a null transition) completed — before UIViewBase.ShowAsync's subsequent
    // `await OnShowAsync(ct)` had a chance to run. A view with real OnShowAsync setup work (or no
    // show transition at all, exposing the whole gap) was clickable before it finished initializing.
    // See plans/260802-1358-animation-transition-hardening/plan.md, Finding 2.
    public class DOTweenUIAnimatorInteractableTimingTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        // OnShowAsync stays suspended until the test explicitly releases it — lets the test observe
        // CanvasGroup state at the exact moment production code would previously have already made
        // the view interactable.
        private sealed class GatedShowView : UIView<TestViewModel>
        {
            private readonly UniTaskCompletionSource _gate = new();
            protected override void BindViewModel(TestViewModel vm) { }
            protected override async UniTask OnShowAsync(CancellationToken ct) => await _gate.Task;
            internal void ReleaseGate() => _gate.TrySetResult();
        }

        [UnityTest]
        public IEnumerator ShowAsync_InteractableStaysFalse_UntilOnShowAsyncCompletes() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<GatedShowView>("GatedShowView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);
            var view = (GatedShowView)await factory.CreateAsync(typeof(GatedShowView), typeof(TestViewModel), "GatedShowView");
            _gos.Track(view);

            // No _showTransition assigned (runtime-created view, nothing set in Inspector) — the
            // null-transition path, which the review flagged as the WIDEST gap: DOTweenUIAnimator
            // used to set interactable=true synchronously before OnShowAsync even started.
            var showTask = view.ShowAsync();

            Assert.IsFalse(view.CanvasGroup.interactable, "must not be interactable while OnShowAsync is still running");
            Assert.IsFalse(view.CanvasGroup.blocksRaycasts);
            Assert.IsFalse(view.IsVisible);

            view.ReleaseGate();
            await showTask;

            Assert.IsTrue(view.CanvasGroup.interactable);
            Assert.IsTrue(view.CanvasGroup.blocksRaycasts);
            Assert.IsTrue(view.IsVisible);
        });
    }
}
