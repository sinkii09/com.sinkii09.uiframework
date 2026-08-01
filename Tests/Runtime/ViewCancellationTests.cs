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
    // Regression tests for the P1 finding: UIViewBase.ShowAsync's OperationCanceledException catch
    // used to swallow the exception instead of rethrowing it, which let NavigationStack.PushAsync
    // add a view to the stack that was never actually shown. See plans/260801-2148-
    // correctness-cluster/phase-04-cancellation-semantics.md. HideAsync's matching catch
    // deliberately does NOT rethrow — P1-4 locks in that asymmetry as intentional, not a regression.
    public class ViewCancellationTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        [UnityTest]
        public IEnumerator ShowAsync_Cancelled_ThrowsOperationCanceled() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("View", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // P1-1: the P1 regression — ShowAsync's OperationCanceledException catch now rethrows
            // instead of swallowing it.
            var error = await CaptureAsync(async () => await view.ShowAsync(cts.Token));
            Assert.IsInstanceOf<OperationCanceledException>(error);
        });

        [UnityTest]
        public IEnumerator PushAsync_ShowCancelled_LeavesStackEmpty() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("View", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            var stack = new NavigationStack(null);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // P1-2: NavigationStack.PushAsync only Adds after ShowAsync succeeds — a rethrown
            // cancellation must not leave a phantom stack entry for a view that was never shown.
            var error = await CaptureAsync(async () => await stack.PushAsync(view, cts.Token));
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreEqual(0, stack.Count);
        });

        [UnityTest]
        public IEnumerator ShowAsync_Cancelled_NotifiesViewModelHideOnce() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);
            var view = (TestView)await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            _gos.Track(view);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // P1-3: OnShow() fires unconditionally before the animator/OnShowAsync phase; a
            // cancelled show must still pair it with exactly one NotifyHide so per-show bindings
            // (subscriptions made in OnShow) do not leak until the next FactoryReset.
            await CaptureAsync(async () => await view.ShowAsync(cts.Token));

            Assert.AreEqual(1, view.VM.OnShowCount);
            Assert.AreEqual(1, view.VM.NotifyHideCount);
        });

        [UnityTest]
        public IEnumerator HideAsync_Cancelled_DoesNotThrow_AndHides() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);
            var view = (TestView)await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            _gos.Track(view);
            await view.ShowAsync();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // P1-4: locks in the deliberate asymmetry — HideAsync's OperationCanceledException catch
            // does NOT rethrow (NavigationStack.PopAsync hides first then removes; rethrowing here
            // would abort the pop and strand an invisible view on the stack). The view must still
            // end up hidden with no exception surfacing.
            var error = await CaptureAsync(async () => await view.HideAsync(cts.Token));

            Assert.IsNull(error);
            Assert.IsFalse(view.IsVisible);
            Assert.IsFalse(view.gameObject.activeSelf);
        });
    }
}
