using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // Covers UIBindingExtensions.BindButtonAsync.
    //
    // Every re-entrancy assertion here gates the handler on a UniTaskCompletionSource and proves the
    // handler is STILL RUNNING before the second press. That is deliberate: v1.7.0 shipped two
    // regression tests for an async race that passed against the unfixed code, because the async
    // path completed synchronously in the test configuration and the race could never occur. A
    // guard test whose handler has already finished asserts nothing.
    //
    // onClick.Invoke() is used rather than a simulated pointer click — it bypasses the interactable
    // check, which is exactly what the restore test needs.
    public class AsyncButtonBindingTests
    {
        private readonly GameObjectTracker _gos = new();
        private DisposableBag _bag;

        [SetUp]
        public void SetUp() => _bag = new DisposableBag();

        [TearDown]
        public void TearDown()
        {
            _bag.Dispose();
            _gos.DestroyAll();
        }

        private Button NewButton()
        {
            var go = _gos.Track(new GameObject("Btn", typeof(RectTransform), typeof(Button)));
            return go.GetComponent<Button>();
        }

        [UnityTest]
        public IEnumerator SecondPress_WhileHandlerRunning_IsIgnored() => UniTask.ToCoroutine(async () =>
        {
            var button = NewButton();
            var gate = new UniTaskCompletionSource();
            int invocations = 0;
            bool finished = false;

            button.BindButtonAsync(async _ =>
            {
                invocations++;
                await gate.Task;
                finished = true;
            }, ref _bag);

            button.onClick.Invoke();

            Assert.AreEqual(1, invocations);
            Assert.IsFalse(finished,
                "Handler completed synchronously — this test would prove nothing about the guard.");

            button.onClick.Invoke();
            Assert.AreEqual(1, invocations, "A press while the handler is in flight must be dropped.");

            gate.TrySetResult();
            await UniTask.DelayFrame(2);
            Assert.IsTrue(finished);

            button.onClick.Invoke();
            Assert.AreEqual(2, invocations, "The guard must release once the handler completes.");
        });

        [UnityTest]
        public IEnumerator Interactable_IsRestoredToCapturedValue_NotHardcodedTrue() => UniTask.ToCoroutine(async () =>
        {
            var button = NewButton();
            // Stands in for a ViewModel that has bound this button to false via BindToInteractable.
            button.interactable = false;

            var gate = new UniTaskCompletionSource();
            button.BindButtonAsync(_ => gate.Task, ref _bag, default, disableWhileRunning: true);

            button.onClick.Invoke();
            Assert.IsFalse(button.interactable);

            gate.TrySetResult();
            await UniTask.DelayFrame(2);

            Assert.IsFalse(button.interactable,
                "Restoring a hardcoded true would silently re-enable a deliberately disabled button.");
        });

        [UnityTest]
        public IEnumerator Interactable_IsUntouched_WhenDisableWhileRunningIsOff() => UniTask.ToCoroutine(async () =>
        {
            var button = NewButton();
            var gate = new UniTaskCompletionSource();

            // The default — the guard alone provides correctness.
            button.BindButtonAsync(_ => gate.Task, ref _bag);

            button.onClick.Invoke();
            Assert.IsTrue(button.interactable, "Default mode must not touch interactable at all.");

            gate.TrySetResult();
            await UniTask.DelayFrame(2);
            Assert.IsTrue(button.interactable);
        });

        [UnityTest]
        public IEnumerator ThrowingHandler_LogsAndReleasesTheGuard() => UniTask.ToCoroutine(async () =>
        {
            var button = NewButton();
            int invocations = 0;

            button.BindButtonAsync(async _ =>
            {
                invocations++;
                await UniTask.Yield();
                throw new InvalidOperationException("handler boom");
            }, ref _bag);

            // Each Expect must be registered BEFORE the log it consumes, so the second press gets
            // its own — the handler throws again.
            LogAssert.Expect(LogType.Exception, new Regex("handler boom"));
            button.onClick.Invoke();
            await UniTask.DelayFrame(2);

            LogAssert.Expect(LogType.Exception, new Regex("handler boom"));
            button.onClick.Invoke();
            await UniTask.DelayFrame(2);

            Assert.AreEqual(2, invocations,
                "A faulted handler must not leave the button dead for the rest of the view's life.");
        });

        [UnityTest]
        public IEnumerator CancelledHandler_DoesNotLogAnException() => UniTask.ToCoroutine(async () =>
        {
            var button = NewButton();
            var cts = new CancellationTokenSource();
            int invocations = 0;

            button.BindButtonAsync(async ct =>
            {
                invocations++;
                await UniTask.Never(ct);
            }, ref _bag, cts.Token);

            button.onClick.Invoke();
            Assert.AreEqual(1, invocations);

            cts.Cancel();
            await UniTask.DelayFrame(2);

            // No LogAssert.Expect here on purpose: an unexpected Debug.LogException fails a Unity
            // test by default, so silence IS the assertion. Hiding a view mid-operation is routine.
            cts.Dispose();
        });

        // Named for what it can actually prove. The "doesn't touch interactable" half is NOT
        // testable from outside: moving the ct check after the disable would still disable and
        // restore synchronously within this call, so the post-hoc read is `true` either way.
        [Test]
        public void AlreadyCancelledToken_DoesNotInvokeHandler()
        {
            var button = NewButton();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            int invocations = 0;

            button.BindButtonAsync(_ =>
            {
                invocations++;
                return UniTask.CompletedTask;
            }, ref _bag, cts.Token, disableWhileRunning: true);

            button.onClick.Invoke();

            Assert.AreEqual(0, invocations);
            cts.Dispose();
        }

        [Test]
        public void DisposingTheBag_RemovesTheListener()
        {
            var button = NewButton();
            int invocations = 0;

            button.BindButtonAsync(_ =>
            {
                invocations++;
                return UniTask.CompletedTask;
            }, ref _bag);

            _bag.Dispose();
            _bag = new DisposableBag();

            button.onClick.Invoke();
            Assert.AreEqual(0, invocations);
        }
    }
}
