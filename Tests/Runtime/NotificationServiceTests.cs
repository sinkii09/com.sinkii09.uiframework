using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Pins the notification service added in Phase 4.
    ///
    /// <para>Time is supplied explicitly through the internal <c>Tick(float)</c> seam rather than
    /// read from the frame clock, so a duration test can assert "alive before, gone after" instead
    /// of zeroing every timer — which would pass against almost any implementation.</para>
    ///
    /// <para>The rules with the most room for an implementer to diverge are pinned deliberately:
    /// visibility is sticky (an arrival never displaces a visible toast, whatever its priority),
    /// the lifetime cap never pauses, and a slot frees only once its fade completes.</para>
    /// </summary>
    public class NotificationServiceTests
    {
        private sealed class FakeOverlay : ITransitionOverlay
        {
            internal bool Shown;
            public bool IsShown => Shown;
            public UniTask ShowAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask HideAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        }

        private UIFrameworkConfig _config;
        private FakeOverlay _overlay;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            _config.NotificationDurationSeconds = 4f;
            _config.NotificationMaxLifetimeSeconds = 15f;
            _config.NotificationMaxVisible = 3;
            _config.NotificationFadeSeconds = 0f;   // instant fades unless a test opts in
            _overlay = new FakeOverlay();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) Object.DestroyImmediate(_config);

            // Host-free tests reach DiscoverHost on every promotion, so a host leaked by a failing
            // assert would be adopted by every later test and make it log an unexpected error.
            foreach (var host in Object.FindObjectsByType<NotificationHostView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(host.gameObject);
            foreach (var item in Object.FindObjectsByType<NotificationItemView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item != null) Object.DestroyImmediate(item.gameObject);
        }

        // No layers and no resolver: without a host nothing resolves a layer or injects anything,
        // which keeps these tests on the service's own logic.
        private NotificationService NewService()
            => new NotificationService(new UIRootLayerRefs(), _config, null, _overlay);

        private static NotificationRequest Req(string id, int qty = 1,
            NotificationPriority priority = NotificationPriority.Normal, float duration = 0f)
            => new NotificationRequest("test", id,
                new NotificationContent(id, quantity: qty, priority: priority, durationSeconds: duration));

        // ------------------------------------------------------------------ merging

        [Test]
        public void Notify_OnEmptyService_CreatesOneEntry()
        {
            var svc = NewService();
            svc.Notify(Req("a"));
            Assert.AreEqual(1, svc.ActiveCount);
        }

        [Test]
        public void Notify_BlankKey_IsRejected()
        {
            var svc = NewService();
            LogAssert.Expect(LogType.Error, new Regex("blank NotificationKey"));
            svc.Notify(new NotificationRequest(default, new NotificationContent("orphan")));
            Assert.AreEqual(0, svc.ActiveCount, "A keyless notification would merge with every other one.");
        }

        [Test]
        public void Notify_SameKeyTwice_MergesIntoOneEntry()
        {
            var svc = NewService();
            svc.Notify(Req("ore", qty: 2));
            svc.Notify(Req("ore", qty: 3));
            Assert.AreEqual(1, svc.ActiveCount, "Same key must edit one toast, not stack two.");
        }

        [Test]
        public void Notify_DifferentKeys_DoNotMerge()
        {
            var svc = NewService();
            svc.Notify(Req("ore"));
            svc.Notify(Req("gem"));
            Assert.AreEqual(2, svc.ActiveCount);
        }

        [UnityTest]
        public IEnumerator Merge_ResetsTheDismissTimer() => UniTask.ToCoroutine(async () =>
        {
            // Non-zero duration and counted ticks: with durations zeroed this would pass whether or
            // not the merge reset anything.
            var svc = NewService();
            svc.Notify(Req("ore", duration: 1f));
            svc.Tick(0f);     // binds it, so the dismiss timer actually starts running
            svc.Tick(0.9f);
            Assert.AreEqual(1, svc.ActiveCount, "Precondition: not yet expired.");

            svc.Notify(Req("ore"));          // merge restarts the clock
            svc.Tick(0.5f);                  // past the ORIGINAL expiry, not the restarted one

            Assert.AreEqual(1, svc.ActiveCount, "The merge should have restarted the dismiss timer.");
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Merge_DoesNotResetTheLifetimeCap() => UniTask.ToCoroutine(async () =>
        {
            // The reason the cap exists: a notification repeating faster than its duration would
            // otherwise never be dismissible.
            var svc = NewService();
            svc._maxLifetime = 1f;
            svc.Notify(Req("ore", duration: 10f));

            for (int i = 0; i < 20; i++)
            {
                svc.Notify(Req("ore"));      // merge on every tick
                svc.Tick(0.1f);
            }

            Assert.AreEqual(0, svc.ActiveCount, "The lifetime cap must survive repeated merges.");
            await UniTask.Yield();
        });

        [Test]
        public void Merge_RaisesPriority_ButNeverLowersIt()
        {
            _config.NotificationMaxVisible = 1;
            var svc = NewService();
            // One slot, and 'b' arrives FIRST, so insertion order favours 'b'. Only a surviving
            // Error priority on 'a' can win the slot — a demoting merge fails this.
            svc.Notify(Req("b", priority: NotificationPriority.Normal));
            svc.Notify(Req("a", priority: NotificationPriority.Error));
            svc.Notify(Req("a", priority: NotificationPriority.Normal));   // must NOT demote
            svc.Tick(0f);

            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "a")),
                "The merge demoted the Error to Normal, so the older 'b' took the slot.");
        }

        // ------------------------------------------------------------------ ordering + preemption

        [Test]
        public void Promotion_PrefersHigherPriority_ThenOlder()
        {
            _config.NotificationMaxVisible = 1;
            var svc = NewService();

            svc.Notify(Req("normal-old", priority: NotificationPriority.Normal));
            svc.Notify(Req("error", priority: NotificationPriority.Error));
            svc.Notify(Req("important", priority: NotificationPriority.Important));
            svc.Tick(0f);

            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "error")),
                "The single slot must take the Error, not the first-arrived Normal.");

            // Freeing it must promote Important next, not the older Normal.
            svc.Dismiss(new NotificationKey("test", "error"));
            svc.Tick(0f);
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "important")),
                "Promotion must prefer priority over arrival order.");
        }

        [UnityTest]
        public IEnumerator Arrival_NeverPreemptsAVisibleEntry() => UniTask.ToCoroutine(async () =>
        {
            // Slots must be SATURATED first, or the arrival simply takes a free slot and
            // "does not preempt" passes for the wrong reason.
            _config.NotificationMaxVisible = 2;
            var svc = NewService();

            svc.Notify(Req("n1", priority: NotificationPriority.Normal, duration: 10f));
            svc.Notify(Req("n2", priority: NotificationPriority.Normal, duration: 10f));
            svc.Tick(0f);   // both bound, slots saturated

            svc.Notify(Req("boom", priority: NotificationPriority.Error, duration: 10f));
            svc.Tick(0f);

            Assert.AreEqual(3, svc.ActiveCount, "Nothing should have been evicted.");

            // The Error must still be WAITING: freeing a slot promotes it, proving it was not bound.
            svc.Dismiss(new NotificationKey("test", "n1"));
            svc.Tick(0f);
            Assert.AreEqual(2, svc.ActiveCount);
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Merge_RaisingAWaiterAboveAVisibleEntry_DoesNotDisplaceIt() => UniTask.ToCoroutine(async () =>
        {
            _config.NotificationMaxVisible = 1;
            var svc = NewService();

            svc.Notify(Req("visible", priority: NotificationPriority.Normal, duration: 10f));
            svc.Tick(0f);
            svc.Notify(Req("waiter", priority: NotificationPriority.Normal, duration: 10f));
            svc.Notify(Req("waiter", priority: NotificationPriority.Error));   // merge raises it
            svc.Tick(0f);

            Assert.AreEqual(2, svc.ActiveCount);
            // 'visible' keeps the slot: dismissing it is what lets the raised waiter in.
            svc.Dismiss(new NotificationKey("test", "visible"));
            svc.Tick(0f);
            Assert.AreEqual(1, svc.ActiveCount, "The visible entry must have survived the raise.");
            await UniTask.Yield();
        });

        // ------------------------------------------------------------------ expiry + slots

        [UnityTest]
        public IEnumerator Entry_AutoDismissesAfterItsDuration() => UniTask.ToCoroutine(async () =>
        {
            var svc = NewService();
            svc.Notify(Req("a", duration: 1f));
            svc.Tick(0f);     // binds it — the dismiss timer only runs while an entry is VISIBLE

            svc.Tick(0.5f);
            Assert.AreEqual(1, svc.ActiveCount, "Must still be alive before its duration elapses.");

            svc.Tick(0.6f);
            Assert.AreEqual(0, svc.ActiveCount, "Must be gone once its duration elapses.");
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Slot_FreesOnlyAfterTheFadeCompletes() => UniTask.ToCoroutine(async () =>
        {
            _config.NotificationMaxVisible = 1;
            _config.NotificationFadeSeconds = 1f;
            var svc = NewService();

            svc.Notify(Req("first", duration: 1.5f));
            svc.Notify(Req("second", duration: 10f));
            svc.Tick(0f);     // 'first' takes the slot, alpha 0, fading in

            // Let it reach full alpha first. An entry that expires mid-fade-IN correctly fades out
            // from its current alpha, which completes instantly and would hide the rule under test.
            svc.Tick(0.5f);   // fade 0 -> 0.5
            svc.Tick(0.5f);   // fade 0.5 -> 1, held

            svc.Tick(0.6f);   // dismiss timer runs out: starts fading out from 1 -> 0.4
            Assert.AreEqual(2, svc.ActiveCount,
                "The expired entry must persist until its fade-out completes, holding the slot.");

            svc.Tick(0.5f);   // fade reaches 0: slot frees and the entry is finally removed
            Assert.AreEqual(1, svc.ActiveCount);
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator TwoSlotsFreeingInOneFrame_PromoteTwoDifferentWaiters() => UniTask.ToCoroutine(async () =>
        {
            // Promotion runs once per tick from the entry list. Driving it from a per-slot
            // continuation would let both freed slots pick the same waiter.
            _config.NotificationMaxVisible = 2;
            var svc = NewService();

            svc.Notify(Req("v1", duration: 1f));
            svc.Notify(Req("v2", duration: 1f));
            svc.Notify(Req("w1", duration: 10f));
            svc.Notify(Req("w2", duration: 10f));
            svc.Tick(0f);

            svc.Tick(1.1f);   // both visible entries expire and free their slots in the same tick
            svc.Tick(0f);

            // Survival alone cannot fail: if both slots promoted the SAME waiter, both waiters
            // would still be in _entries and the count would be identical. Assert that two
            // DIFFERENT entries are bound.
            Assert.AreEqual(2, svc.VisibleCount, "Both freed slots must promote different waiters.");
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "w1")));
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "w2")));
            await UniTask.Yield();
        });

        // ------------------------------------------------------------------ curtain

        [UnityTest]
        public IEnumerator Curtain_PausesTheDismissTimer_ButNotTheLifetimeCap() => UniTask.ToCoroutine(async () =>
        {
            // A toast on the Notification layer sits UNDER the loading curtain, so its dismiss timer
            // pauses. The lifetime cap must NOT pause: an overlay that never hides would otherwise
            // make every entry immortal and every later Notify a permanent drop.
            var svc = NewService();
            svc._maxLifetime = 10f;
            svc.Notify(Req("a", duration: 1f));
            svc.Tick(0f);     // bind FIRST: a waiting entry's timer does not run either way, so
                              // raising the curtain before this would pass without pausing anything

            _overlay.Shown = true;
            for (int i = 0; i < 5; i++) svc.Tick(0.9f);   // 4.5s, far past the 1s dismiss duration
            Assert.AreEqual(1, svc.ActiveCount,
                "The dismiss timer must be paused while the toast is hidden behind the curtain.");

            // ...but the lifetime cap must NOT pause, or an overlay that never hides makes every
            // entry immortal and every later Notify a permanent drop.
            for (int i = 0; i < 7; i++) svc.Tick(0.9f);   // total ticked time now exceeds 10s
            Assert.AreEqual(0, svc.ActiveCount,
                "The lifetime cap must keep running behind the curtain.");
            await UniTask.Yield();
        });

        // ------------------------------------------------------------------ dismissal + overflow

        [Test]
        public void Dismiss_RemovesOnlyThatEntry_AndUnknownKeysAreNoOps()
        {
            var svc = NewService();
            svc.Notify(Req("a"));
            svc.Notify(Req("b"));
            svc.Tick(0f);

            svc.Dismiss(new NotificationKey("test", "nope"));
            svc.Tick(0f);
            Assert.AreEqual(2, svc.ActiveCount, "An unknown key must be a no-op.");

            svc.Dismiss(new NotificationKey("test", "a"));
            svc.Tick(0f);
            Assert.AreEqual(1, svc.ActiveCount);
        }

        [Test]
        public void DismissAll_ClearsVisibleAndWaiting()
        {
            _config.NotificationMaxVisible = 1;
            var svc = NewService();
            svc.Notify(Req("a"));
            svc.Notify(Req("b"));
            svc.Notify(Req("c"));
            svc.Tick(0f);

            svc.DismissAll();
            svc.Tick(0f);

            Assert.AreEqual(0, svc.ActiveCount);
        }

        [Test]
        public void Overflow_DropsTheLowestPriorityWaiter_NeverAVisibleOne()
        {
            // The visible entries are deliberately the LOWEST priority in the system, so a rule that
            // dropped "the lowest priority anywhere" would evict a visible toast and fail here.
            _config.NotificationMaxVisible = 2;
            var svc = NewService();

            svc.Notify(Req("vis1", priority: NotificationPriority.Normal, duration: 100f));
            svc.Notify(Req("vis2", priority: NotificationPriority.Normal, duration: 100f));
            svc.Tick(0f);

            for (int i = 0; i < NotificationService.MaxQueued - 2; i++)
                svc.Notify(Req($"w{i}", priority: NotificationPriority.Error, duration: 100f));

            Assert.AreEqual(NotificationService.MaxQueued, svc.ActiveCount);

            LogAssert.Expect(LogType.Warning, new Regex("Queue full"));
            svc.Notify(Req("overflow", priority: NotificationPriority.Error, duration: 100f));

            Assert.AreEqual(NotificationService.MaxQueued, svc.ActiveCount, "Cap must hold.");

            // The count alone cannot fail: dropping a VISIBLE entry also removes one and adds one.
            // Assert identity instead — both low-priority visible entries must have survived.
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "vis1")),
                "A visible entry was evicted; overflow must only drop waiting entries.");
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "vis2")),
                "A visible entry was evicted; overflow must only drop waiting entries.");
            Assert.AreEqual(2, svc.VisibleCount);
        }

        [Test]
        public void Overflow_RefusesAnArrivalThatOutranksNothing()
        {
            // Every waiter is Error; a Normal arrival must be refused rather than evicting one.
            _config.NotificationMaxVisible = 1;
            var svc = NewService();
            for (int i = 0; i < NotificationService.MaxQueued; i++)
                svc.Notify(Req($"e{i}", priority: NotificationPriority.Error, duration: 100f));
            svc.Tick(0f);

            LogAssert.Expect(LogType.Warning, new Regex("every waiting entry ranks at least as high"));
            svc.Notify(Req("low", priority: NotificationPriority.Normal, duration: 100f));

            Assert.IsFalse(svc.Contains(new NotificationKey("test", "low")),
                "A Normal arrival must not evict a queued Error.");
            Assert.AreEqual(NotificationService.MaxQueued, svc.ActiveCount);
        }

        [Test]
        public void Overflow_EvictsTheOldestLowestWaiter_WhenTheArrivalOutranksIt()
        {
            // The complement, and the branch that actually removes an entry — untested until now.
            _config.NotificationMaxVisible = 1;
            var svc = NewService();
            for (int i = 0; i < NotificationService.MaxQueued; i++)
                svc.Notify(Req($"n{i}", priority: NotificationPriority.Normal, duration: 100f));
            svc.Tick(0f);

            LogAssert.Expect(LogType.Warning, new Regex("dropped waiting notification"));
            svc.Notify(Req("boom", priority: NotificationPriority.Error, duration: 100f));

            Assert.IsTrue(svc.Contains(new NotificationKey("test", "boom")),
                "An Error arrival must be admitted over a Normal waiter.");
            Assert.AreEqual(NotificationService.MaxQueued, svc.ActiveCount, "Cap must hold.");
            // n0 is bound (visible); n1 is the oldest WAITING Normal and is the one that goes.
            Assert.IsFalse(svc.Contains(new NotificationKey("test", "n1")));
            Assert.IsTrue(svc.IsBound(new NotificationKey("test", "n0")),
                "The visible entry must never be the one evicted.");
        }

        [UnityTest]
        public IEnumerator DismissInterleavedWithExpiry_LeavesEveryEntryConsistent() => UniTask.ToCoroutine(async () =>
        {
            // The invariant, stated rather than "does not corrupt": ActiveCount stays correct and
            // every surviving entry is still promotable, so the queue keeps draining. Note this is
            // interleaving, not re-entrancy — the Dismiss lands between ticks, not inside one.
            _config.NotificationMaxVisible = 2;
            var svc = NewService();

            svc.Notify(Req("a", duration: 1f));
            svc.Notify(Req("b", duration: 100f));
            svc.Notify(Req("c", duration: 100f));
            svc.Tick(0f);

            svc.Tick(1.1f);              // 'a' expires during the tick
            svc.Dismiss(new NotificationKey("test", "b"));   // dismissed right after
            svc.Tick(0f);

            Assert.AreEqual(1, svc.ActiveCount, "Only 'c' should remain.");
            svc.Dismiss(new NotificationKey("test", "c"));
            svc.Tick(0f);
            Assert.AreEqual(0, svc.ActiveCount, "The queue must still drain to empty.");
            await UniTask.Yield();
        });

        // ------------------------------------------------------------------ key + layer + null-object

        [Test]
        public void NotificationKey_EqualityAndHash()
        {
            var a = new NotificationKey("loot", "ore");
            var b = new NotificationKey("loot", "ore");
            var differentId = new NotificationKey("loot", "gem");
            var differentCategory = new NotificationKey("quest", "ore");

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, differentId);
            Assert.AreNotEqual(a, differentCategory);
            Assert.IsTrue(a == b);
            Assert.IsTrue(a != differentId);
            Assert.IsTrue(a.IsValid);
            Assert.IsFalse(default(NotificationKey).IsValid);
        }

        [Test]
        public void ResolveLayer_WithBothLayersNull_LogsExactlyOneError()
        {
            // Both null on purpose: with only Notification missing, the resolved Overlay transform
            // is cached and the second call early-returns, so the latch would never be exercised
            // and the test would pass without proving anything.
            var svc = NewService();

            LogAssert.Expect(LogType.Error, new Regex("no Notification layer"));
            svc.ResolveLayer();
            svc.ResolveLayer();   // must NOT log a second time
        }

        [Test]
        public void NullNotificationService_NoOpsWithoutThrowing()
        {
            INotificationService svc = new NullNotificationService();
            Assert.DoesNotThrow(() =>
            {
                svc.Notify(Req("a"));
                svc.Dismiss(new NotificationKey("test", "a"));
                svc.DismissAll();
            });
            Assert.AreEqual(0, svc.ActiveCount);
        }

        // ------------------------------------------------------------------ host-backed

        private NotificationHostView NewHost()
        {
            var itemGo = new GameObject("Item", typeof(RectTransform), typeof(CanvasGroup));
            var item = itemGo.AddComponent<NotificationItemView>();

            // Built INACTIVE on purpose: AddComponent runs Awake immediately on an active object,
            // and UIViewValidator (v1.9.0) would then correctly report _itemPrefab as unassigned,
            // because reflection cannot wire it until after the component exists.
            var hostGo = new GameObject("Host", typeof(RectTransform), typeof(CanvasGroup));
            hostGo.SetActive(false);
            var host = hostGo.AddComponent<NotificationHostView>();

            // Private [SerializeField]; reflection is the only way to wire it from a test assembly.
            typeof(NotificationHostView)
                .GetField("_itemPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(host, item);

            hostGo.SetActive(true);   // Awake runs here, with the reference already assigned
            return host;
        }

        [UnityTest]
        public IEnumerator Host_IsActivatedWhenAToastAppears_AndHiddenWhenIdle() => UniTask.ToCoroutine(async () =>
        {
            // Regression test for a real defect: the host pre-hides itself in Awake and nothing
            // ever brought it back, so every timer drained correctly and NOTHING was ever drawn.
            // Nothing threw, because every view access is null-guarded by design.
            var host = NewHost();
            var svc = NewService();
            LogAssert.Expect(LogType.Error, new Regex("no Notification layer"));
            svc.Initialize();
            Assert.IsFalse(host.gameObject.activeSelf, "Precondition: pre-hidden in Awake.");

            svc.Notify(Req("a", duration: 1f));
            svc.Tick(0f);

            Assert.IsTrue(host.gameObject.activeSelf, "The host must be shown when a toast binds.");
            Assert.AreEqual(1f, host.CanvasGroup.alpha, 0.001f);

            svc.Tick(1.1f);   // expires, fade is instant, slot frees
            Assert.IsFalse(host.gameObject.activeSelf, "The host must hide again once nothing is shown.");

            Object.DestroyImmediate(host.gameObject);
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Host_NeverTakesRaycasts() => UniTask.ToCoroutine(async () =>
        {
            // A full-screen host on the Notification layer would otherwise swallow every click
            // above Popup, because the layer canvas carries a GraphicRaycaster.
            var host = NewHost();
            Assert.IsFalse(host.CanvasGroup.blocksRaycasts, "Awake must leave the host non-blocking.");

            await host.ShowAsync();
            Assert.IsFalse(host.CanvasGroup.blocksRaycasts,
                "UIViewBase.ShowAsync re-enables raycasts; the host must re-assert non-blocking.");
            Assert.IsFalse(host.CanvasGroup.interactable);

            await host.HideAsync();
            Assert.IsFalse(host.CanvasGroup.blocksRaycasts);

            Object.DestroyImmediate(host.gameObject);
        });

        [UnityTest]
        public IEnumerator HostDestroyedMidDisplay_EntriesStillExpire() => UniTask.ToCoroutine(async () =>
        {
            // The entry list is the source of truth: losing the host degrades what is drawn, never
            // whether the queue drains.
            var host = NewHost();
            var svc = NewService();
            LogAssert.Expect(LogType.Error, new Regex("no Notification layer"));
            svc.Initialize();

            svc.Notify(Req("a", duration: 1f));
            svc.Tick(0f);
            Object.DestroyImmediate(host.gameObject);

            Assert.DoesNotThrow(() => svc.Tick(1.1f));
            Assert.AreEqual(0, svc.ActiveCount, "Losing the host must not wedge the queue.");
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator FallbackLayer_DoesNotPauseBehindTheCurtain() => UniTask.ToCoroutine(async () =>
        {
            // On the Overlay fallback the toast draws OVER the curtain, so it is plainly visible and
            // must keep counting down — the opposite of the on-layer behaviour.
            var svc = NewService();
            LogAssert.Expect(LogType.Error, new Regex("no Notification layer"));
            svc.ResolveLayer();          // both layers null -> fallback latch set

            svc.Notify(Req("a", duration: 1f));
            svc.Tick(0f);
            _overlay.Shown = true;
            svc.Tick(1.1f);

            Assert.AreEqual(0, svc.ActiveCount,
                "A toast on the fallback layer is visible over the curtain and must still expire.");
            await UniTask.Yield();
        });
    }
}
