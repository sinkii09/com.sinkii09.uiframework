using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// INotificationSuspender: holding the toast queue back while something covers it.
    ///
    /// <para>The bug being fixed is a layer ordering the framework itself sets — Notification below
    /// Overlay — so a reward toast raised during a full-screen animation is shown to nobody. These
    /// assert the two halves that make deferral correct rather than merely quiet: nothing is
    /// PROMOTED while suspended, and nothing EXPIRES while suspended.</para>
    ///
    /// <para>The second half is the subtle one. NotificationService documents that an entry's
    /// Lifetime is never paused, because Lifetime is the only guarantee an entry ever leaves the
    /// queue. Suspending pauses it anyway — which is why the suspension itself is capped, and why
    /// the cap has a test of its own here.</para>
    /// </summary>
    public class NotificationSuspendTests
    {
        private sealed class FakeOverlay : ITransitionOverlay
        {
            public bool IsShown => false;
            public UniTask ShowAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask HideAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        }

        private UIFrameworkConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            _config.NotificationDurationSeconds = 4f;
            _config.NotificationMaxLifetimeSeconds = 15f;
            _config.NotificationMaxVisible = 3;
            _config.NotificationFadeSeconds = 0f;   // instant fades: slot bookkeeping is not the subject
            _config.NotificationMaxSuspendSeconds = 30f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) Object.DestroyImmediate(_config);

            // Promotion reaches DiscoverHost, which scans the OPEN SCENE. A host left behind by a
            // failing assert — or simply one sitting in whatever scene a developer had open — gets
            // adopted and reparented, then logs a missing-layer error. Without this the suite passes
            // on CI and fails on a desktop, which is the worst way to find out.
            foreach (var host in Object.FindObjectsByType<NotificationHostView>(FindObjectsInactive.Include))
                Object.DestroyImmediate(host.gameObject);
            foreach (var item in Object.FindObjectsByType<NotificationItemView>(FindObjectsInactive.Include))
                if (item != null) Object.DestroyImmediate(item.gameObject);
        }

        private NotificationService NewService()
            => new NotificationService(new UIRootLayerRefs(), _config, null, new FakeOverlay());

        private static NotificationRequest Req(string id, int qty = 1, float duration = 0f)
            => new NotificationRequest("test", id,
                new NotificationContent(id, quantity: qty, durationSeconds: duration));

        [Test]
        public void Suspend_HoldsBackPromotion_AndResumeDrains()
        {
            var svc = NewService();
            svc.Notify(Req("reward"));

            using (svc.Suspend("pull-animation"))
            {
                svc.Tick(0.5f);
                Assert.AreEqual(0, svc.VisibleCount,
                    "A toast promoted here would fade in under the caller's overlay, unseen.");
            }

            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount, "The queue drains the moment the suspension lifts.");
        }

        [Test]
        public void Suspend_KeepsAWaitingToastAlivePastItsLifetimeCap()
        {
            // The failure this exists to prevent: Lifetime accrues unconditionally, so a ten-second
            // reward animation used to outlast the toast the reward raised.
            var svc = NewService();
            svc._maxLifetime = 1f;
            svc.Notify(Req("reward", duration: 10f));

            using (svc.Suspend("pull-animation"))
            {
                for (var i = 0; i < 30; i++) svc.Tick(0.1f);   // 3s, triple the cap

                Assert.AreEqual(1, svc.ActiveCount, "It must not expire while nobody can see it.");
                Assert.AreEqual(0, svc.VisibleCount, "...and it must not have taken a slot either.");
            }

            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount);
        }

        [Test]
        public void Suspend_FreezesTheDismissTimerOfAToastAlreadyOnScreen()
        {
            var svc = NewService();
            svc.Notify(Req("reward", duration: 1f));
            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount, "Precondition: it is bound to a slot.");

            using (svc.Suspend("pull-animation"))
                svc.Tick(5f);   // five times its duration, all of it behind the caller's overlay

            Assert.AreEqual(1, svc.ActiveCount, "Time spent covered must not count as time on screen.");

            svc.Tick(1.1f);
            Assert.AreEqual(0, svc.ActiveCount, "Once visible again the ordinary timer applies.");
        }

        [Test]
        public void Suspend_StillAcceptsAndMergesNotifications()
        {
            // Deferring is about SHOWING. A game that stopped being able to report rewards during an
            // animation would have to queue them itself, which is the work this removes.
            var svc = NewService();

            using (svc.Suspend("pull-animation"))
            {
                svc.Notify(Req("ore", qty: 2));
                svc.Notify(Req("ore", qty: 3));
                svc.Notify(Req("gem"));
                svc.Tick(0.5f);
            }

            Assert.AreEqual(2, svc.ActiveCount, "Two keys, and the repeated one merged as usual.");
        }

        [Test]
        public void Suspend_IsReferenceCounted()
        {
            // The real shape of the problem: a pull overlay opens a result popup on top of itself.
            var svc = NewService();
            svc.Notify(Req("reward"));

            var outer = svc.Suspend("pull-animation");
            var inner = svc.Suspend("result-popup");

            inner.Dispose();
            svc.Tick(0f);
            Assert.AreEqual(0, svc.VisibleCount, "The outer suspension still holds the queue.");

            outer.Dispose();
            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount);
        }

        [Test]
        public void SuspendToken_DisposedTwice_DoesNotReleaseSomeoneElsesSuspension()
        {
            // A token in a `using` inside a loop, or disposed in both a finally and a cancel path,
            // would otherwise decrement the count twice and un-suspend a caller that never asked.
            var svc = NewService();
            svc.Notify(Req("reward"));

            var first = svc.Suspend("pull-animation");
            var second = svc.Suspend("result-popup");

            first.Dispose();
            first.Dispose();

            svc.Tick(0f);
            Assert.AreEqual(0, svc.VisibleCount, "The second suspension is still in force.");

            second.Dispose();
            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount);
        }

        [Test]
        public void Suspension_ExpiresAfterItsCap_AndNamesTheCaller()
        {
            // A leaked token freezes Lifetime, which is the only thing guaranteeing an entry ever
            // leaves the queue — so without expiry the queue fills to MaxQueued and then refuses
            // every later notification, permanently and silently.
            var svc = NewService();
            svc._maxSuspendSeconds = 1f;
            svc.Notify(Req("reward"));

            var leaked = svc.Suspend("pull-animation");
            Assert.That(leaked, Is.Not.Null);

            svc.Tick(0.5f);
            Assert.AreEqual(0, svc.VisibleCount, "Still inside the cap.");

            LogAssert.Expect(LogType.Error, new Regex("pull-animation"));
            svc.Tick(0.6f);

            Assert.AreEqual(1, svc.VisibleCount, "An expired suspension must stop holding the queue.");
        }

        [Test]
        public void Suspension_ExpiresEvenWhileNothingIsQueued()
        {
            // Tick returns early on an empty queue. If the suspension clock only advanced past that
            // point, a token leaked during a quiet moment would never expire and never be reported —
            // the leak would surface much later, as toasts that inexplicably stopped appearing.
            var svc = NewService();
            svc._maxSuspendSeconds = 1f;

            var leaked = svc.Suspend("leaked-while-idle");
            Assert.That(leaked, Is.Not.Null);

            LogAssert.Expect(LogType.Error, new Regex("leaked-while-idle"));
            svc.Tick(1.5f);

            svc.Notify(Req("reward"));
            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount, "An expired suspension must not still hold the queue.");
        }

        [Test]
        public void Suspend_AfterAnEarlierSuspensionExpired_StillGetsItsOwnWindow()
        {
            // A leaked token holds the depth above zero for good, so a later Suspend used to inherit
            // the already-expired clock and do nothing at all — silently, because the cap had already
            // logged once and would not log again.
            var svc = NewService();
            svc._maxSuspendSeconds = 1f;

            var leaked = svc.Suspend("leaked");
            Assert.That(leaked, Is.Not.Null);

            LogAssert.Expect(LogType.Error, new Regex("leaked"));
            svc.Tick(1.5f);

            svc.Notify(Req("reward"));
            using (svc.Suspend("pull-animation"))
            {
                svc.Tick(0.5f);
                Assert.AreEqual(0, svc.VisibleCount, "The new caller must get a fresh window.");
            }

            svc.Tick(0f);
            Assert.AreEqual(1, svc.VisibleCount);
        }

        [Test]
        public void Suspend_DoesNotRescueAToastThatHadAlreadyBegunFadingOut()
        {
            // The honest limit of "nothing expires while suspended". AdvanceSlots keeps running while
            // deferred, so a toast that had ALREADY expired and started its fade-out completes it and
            // releases its slot. Every other test here uses instant fades, which hides this entirely.
            _config.NotificationFadeSeconds = 1f;
            var svc = NewService();

            svc.Notify(Req("reward", duration: 1f));
            svc.Tick(0f);      // bind
            svc.Tick(1.1f);    // expire, begin fading out

            using (svc.Suspend("pull-animation"))
                svc.Tick(1.5f);

            Assert.AreEqual(0, svc.ActiveCount,
                "Suspending protects a toast that has not expired; it does not un-expire one.");
        }

        [Test]
        public void NullService_HandsBackAUsableToken()
        {
            // A project with no NotificationHostView gets NullNotificationService. Callers must be
            // able to write `using (suspender.Suspend(...))` without asking which one they have.
            INotificationSuspender suspender = new NullNotificationService();

            var token = suspender.Suspend("pull-animation");
            Assert.That(token, Is.Not.Null);
            Assert.That(() => { token.Dispose(); token.Dispose(); }, Throws.Nothing);
        }
    }
}
