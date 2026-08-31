using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Covers <c>ViewModelBase.ShowToken</c> — the token half of <c>_showDisposables</c>.
    ///
    /// <para>Three of these guard traps that produce no compiler diagnostic and would not surface
    /// in ordinary play: reading a disposed <c>CancellationTokenSource.Token</c> throws, a throwing
    /// cancellation callback can abort the caller mid-teardown, and a re-entrant callback can drive
    /// a double-dispose.</para>
    /// </summary>
    public class ViewModelShowTokenTests
    {
        // ShowToken is protected — these expose it, which is also how a real subclass consumes it.
        private class TestViewModel : ViewModelBase
        {
            public CancellationToken Token => ShowToken;
            public CancellationToken TokenSeenInOnHide { get; private set; }
            public int OnHideCount { get; private set; }

            protected override void OnHide()
            {
                OnHideCount++;
                TokenSeenInOnHide = ShowToken;
            }
        }

        [Test]
        public void ShowToken_BeforeHide_IsNotCancelled()
        {
            var vm = new TestViewModel();
            Assert.IsFalse(vm.Token.IsCancellationRequested);
        }

        [Test]
        public void NotifyHide_CancelsShowToken()
        {
            var vm = new TestViewModel();
            var captured = vm.Token;

            vm.NotifyHide();

            Assert.IsTrue(captured.IsCancellationRequested,
                "The token handed out before the hide must be cancelled by it.");
        }

        [Test]
        public void OnHide_SeesAnAlreadyCancelledToken()
        {
            // Ordering guard: the cancel must happen BEFORE OnHide() so an override that inspects
            // the token to decide whether to keep working sees the truth.
            var vm = new TestViewModel();

            vm.NotifyHide();

            Assert.AreEqual(1, vm.OnHideCount);
            Assert.IsTrue(vm.TokenSeenInOnHide.IsCancellationRequested);
        }

        [Test]
        public void SecondShow_IssuesAFreshToken()
        {
            var vm = new TestViewModel();
            var first = vm.Token;
            vm.NotifyHide();

            var second = vm.Token;

            Assert.IsTrue(first.IsCancellationRequested);
            Assert.IsFalse(second.IsCancellationRequested,
                "A re-shown ViewModel must not start life with a cancelled token.");
        }

        [Test]
        public void Dispose_ThenShowToken_ReturnsCancelledToken_WithoutThrowing()
        {
            // The trap: CancellationTokenSource.Token throws ObjectDisposedException after
            // Dispose(). Without the _disposed guard in the getter this line throws.
            var vm = new TestViewModel();
            vm.Dispose();

            CancellationToken token = default;
            Assert.DoesNotThrow(() => token = vm.Token);
            Assert.IsTrue(token.IsCancellationRequested);
        }

        [Test]
        public void NotifyHide_DisposesShowDisposables_EvenWhenACancelCallbackThrows()
        {
            // Cancel() runs registrations synchronously and rethrows them wrapped in an
            // AggregateException. Unguarded, that escapes NotifyHide and skips the teardown below
            // it — leaking every per-show subscription.
            var vm = new TestViewModel();
            vm.Token.Register(() => throw new InvalidOperationException("callback boom"));

            bool subscriptionDisposed = false;
            vm.AddShowDisposable(Disposable.Create(() => subscriptionDisposed = true));

            LogAssert.ignoreFailingMessages = true;   // the swallowed exception is logged
            try
            {
                Assert.DoesNotThrow(() => vm.NotifyHide());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsTrue(subscriptionDisposed,
                "A throwing cancellation callback must not abort the per-show teardown.");
        }

        [Test]
        public void Dispose_AfterNotifyHide_IsIdempotent()
        {
            var vm = new TestViewModel();
            vm.NotifyHide();

            Assert.DoesNotThrow(() => vm.Dispose());
            Assert.DoesNotThrow(() => vm.Dispose());
            Assert.AreEqual(1, vm.OnHideCount, "Dispose() must not re-run OnHide().");
        }

        [Test]
        public void NotifyHide_WhenAShowDisposableThrows_StillIssuesAFreshToken()
        {
            // The stranding trap. Without the finally around the teardown, a throwing per-show
            // disposable aborts NotifyHide before the CTS is swapped, and the ViewModel is stuck
            // on a cancelled token forever — every later show silently no-ops all gated work.
            // Reachable for real: Button.onClick.RemoveListener throws MissingReferenceException
            // once the Button is destroyed, and BindButtonAsync registers exactly that.
            var vm = new TestViewModel();
            // Explicit Action: a throw-expression lambda gives the overload resolver nothing to
            // infer a delegate type from.
            Action thrower = () => throw new InvalidOperationException("bag boom");
            vm.AddShowDisposable(Disposable.Create(thrower));

            // Assert.Catch, not Assert.Throws<T>: DisposableBag may surface the failure wrapped.
            // What matters is that it surfaces at all rather than being swallowed.
            Assert.Catch(() => vm.NotifyHide(),
                "The exception must still surface — only the stranding is prevented.");

            Assert.IsFalse(vm.Token.IsCancellationRequested,
                "A fresh token must be issued even though the teardown threw.");
        }

        [Test]
        public void NotifyHide_ReentrantFromACancelCallback_RunsOnHideOnce()
        {
            // Cancel() invokes registrations synchronously, so a callback that calls back into
            // NotifyHide() re-enters it mid-teardown. Without the _hiding guard that runs OnHide()
            // twice and leaks the inner call's freshly created source.
            var vm = new TestViewModel();
            vm.Token.Register(() => vm.NotifyHide());

            vm.NotifyHide();

            Assert.AreEqual(1, vm.OnHideCount, "Re-entrant NotifyHide must be ignored.");
        }

        [Test]
        public void NotifyHide_AfterDispose_IsANoOp()
        {
            var vm = new TestViewModel();
            vm.Dispose();

            Assert.DoesNotThrow(() => vm.NotifyHide());
            Assert.AreEqual(0, vm.OnHideCount);
        }
    }

    // _showDisposables is protected and is a mutable struct field (DisposableBag), so a test
    // subclass cannot expose it by property without copying it. Reflection on the field is the
    // only way to add to the real bag the production code will dispose.
    internal static class ViewModelTestExtensions
    {
        public static void AddShowDisposable(this ViewModelBase vm, IDisposable disposable)
        {
            var field = typeof(ViewModelBase).GetField(
                "_showDisposables", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, "ViewModelBase._showDisposables was renamed — update this helper.");

            var bag = (DisposableBag)field.GetValue(vm);
            bag.Add(disposable);
            field.SetValue(vm, bag);
        }
    }
}
