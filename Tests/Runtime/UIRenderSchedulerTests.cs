using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    public class UIRenderSchedulerTests
    {
        private FakeFrameProvider _host;
        private UIRenderScheduler _scheduler;
        private Action<Exception> _originalHandler;

        [SetUp]
        public void SetUp()
        {
            _host = new FakeFrameProvider();
            _scheduler = new UIRenderScheduler(_host);
            _originalHandler = ObservableSystem.GetUnhandledExceptionHandler();
        }

        [TearDown]
        public void TearDown()
        {
            // Global state: leaving a test's handler installed would silently swallow or duplicate
            // reporting for every later test in the run.
            ObservableSystem.RegisterUnhandledExceptionHandler(_originalHandler);
            _scheduler.Dispose();
            UIBindingExtensions.Scheduler = null;
        }

        // --- 16. the pump registers exactly once --------------------------------------------

        [Test]
        public void Pump_IsRegisteredExactlyOnce_AndEnsureRegisteredIsIdempotent()
        {
            Assert.That(_host.RegisterCalls, Is.EqualTo(1), "Constructing the scheduler registers the pump.");

            _scheduler.EnsureRegistered();
            _scheduler.EnsureRegistered();

            Assert.That(_host.RegisterCalls, Is.EqualTo(1),
                "Two pumps would advance the frame counter twice per frame, silently HALVING every " +
                "ThrottleLastFrame(N) window pointed at this scheduler.");
        }

        [Test]
        public void FrameCount_AdvancesOncePerHostFrame()
        {
            long before = _scheduler.GetFrameCount();
            _host.Advance(3);
            Assert.That(_scheduler.GetFrameCount() - before, Is.EqualTo(3));
        }

        // --- 13. never evicted for going idle ------------------------------------------------

        [Test]
        public void Pump_SurvivesIdleFrames()
        {
            _host.Advance(10);

            Assert.That(_host.ItemCount, Is.EqualTo(1),
                "Returning false on an empty list would have the host evict the pump, and nothing " +
                "would ever re-register it.");
        }

        // --- 11. C-2: R3's contract is honoured for FOREIGN work items ----------------------

        [Test]
        public void ForeignWorkItem_ThatReturnsFalse_IsRemoved()
        {
            var item = new ForeignWorkItem(_ => false);
            _scheduler.Register(item);

            _host.Advance();
            _host.Advance();

            Assert.That(item.Calls, Is.EqualTo(1), "Removed after returning false, per R3's contract.");
        }

        [Test]
        public void ForeignWorkItem_ThatThrows_IsRemovedAndForwardedToR3sHandler()
        {
            var seen = new List<Exception>();
            ObservableSystem.RegisterUnhandledExceptionHandler(e => seen.Add(e));

            var item = new ForeignWorkItem(_ => throw new InvalidOperationException("foreign boom"));
            _scheduler.Register(item);

            _host.Advance();
            _host.Advance();

            Assert.That(item.Calls, Is.EqualTo(1),
                "A foreign item must be DROPPED, not kept alive: re-entering a corrupt operator " +
                "every frame forever is worse. The keep-alive policy belongs to CoalescedBinding.");
            Assert.That(seen, Has.Count.EqualTo(1));
            Assert.That(seen[0].Message, Is.EqualTo("foreign boom"));
            Assert.That(_host.Escaped, Is.Empty, "It must not escape the pump.");
        }

        // --- 12 & N-2. nothing takes the pump down -------------------------------------------

        [Test]
        public void ThrowingWorkItem_DoesNotEvictThePump_OtherItemsKeepRunning()
        {
            ObservableSystem.RegisterUnhandledExceptionHandler(_ => { });

            _scheduler.Register(new ForeignWorkItem(_ => throw new InvalidOperationException("boom")));
            var survivor = new ForeignWorkItem(_ => true);
            _scheduler.Register(survivor);

            _host.Advance();
            _host.Advance();
            _host.Advance();

            Assert.That(_host.ItemCount, Is.EqualTo(1), "The pump itself is still registered.");
            Assert.That(survivor.Calls, Is.GreaterThanOrEqualTo(2),
                "One bad item must not take every other binding in the process down with it.");
        }

        [Test]
        public void ThrowingUnhandledExceptionHandler_DoesNotEvictThePump()
        {
            // R3 nests this guard in UnityFrameProvider.Run for exactly this reason: the handler is
            // user-installable and can itself throw. Without mirroring it, the throw escapes our
            // pump, the host evicts it, and ALL coalescing dies for the rest of the session.
            ObservableSystem.RegisterUnhandledExceptionHandler(_ => throw new Exception("handler boom"));

            _scheduler.Register(new ForeignWorkItem(_ => throw new InvalidOperationException("boom")));

            _host.Advance();

            Assert.That(_host.Escaped, Is.Empty, "Nothing may escape the pump.");
            Assert.That(_host.ItemCount, Is.EqualTo(1), "The pump survives.");

            ObservableSystem.RegisterUnhandledExceptionHandler(_ => { });
            var survivor = new ForeignWorkItem(_ => true);
            _scheduler.Register(survivor);
            _host.Advance();

            Assert.That(survivor.Calls, Is.EqualTo(1), "And still pumps afterwards.");
        }

        // --- 7. re-entrant registration during a pump ----------------------------------------

        [Test]
        public void ReEntrantRegistration_DoesNotCorruptIterationOrDropAnExistingItem()
        {
            var steady = new ForeignWorkItem(_ => true);
            _scheduler.Register(steady);

            bool added = false;
            var late = new ForeignWorkItem(_ => true);
            _scheduler.Register(new ForeignWorkItem(_ =>
            {
                if (!added)
                {
                    added = true;
                    _scheduler.Register(late);   // registers while the pump is mid-iteration
                }
                return true;
            }));

            _host.Advance();
            _host.Advance();

            Assert.That(_host.Escaped, Is.Empty, "Span invalidation would surface as a throw here.");
            Assert.That(steady.Calls, Is.EqualTo(2), "The pre-existing item is not dropped or skipped.");
            Assert.That(late.Calls, Is.GreaterThanOrEqualTo(1), "The re-entrant registration runs.");
        }

        // --- N-1. disposal during apply must not evict an innocent binding -------------------

        [Test]
        public void BindingDisposedDuringItsOwnApply_DoesNotEvictALaterBinding()
        {
            // The hazard needs BOTH halves in ONE call stack: the dispose frees a slot, and a
            // Register inside that same stack reuses it BEFORE the pump reaches its own Remove(i).
            // Registering the second binding from outside the pump does not construct the race at
            // all — it would pass whether or not Dispose frees its slot.
            //
            // Subjects, not ReactiveProperties: a ReactiveProperty emits during the constructor,
            // so `suicidal` would still be null inside its own first apply.
            var trigger = new Subject<int>();
            var victimSource = new Subject<int>();
            var victimGot = new List<int>();
            CoalescedBinding<int> victim = null;
            CoalescedBinding<int> suicidal = null;

            suicidal = new CoalescedBinding<int>(trigger, _scheduler, v =>
            {
                // Only on the second value, so this runs from INSIDE the pump rather than on the
                // synchronous leading edge: a binding whose write triggers its own view's hide.
                if (v != 2) return;

                suicidal.Dispose();

                // Born inside the disposing apply — this is the binding that would claim the
                // freed slot and then be evicted by the pump's Remove for `suicidal`.
                victim = new CoalescedBinding<int>(victimSource, _scheduler, victimGot.Add, null);
                victimSource.OnNext(10);   // leading edge, synchronous
                victimSource.OnNext(11);   // Register(...) from inside this very call stack
            }, null);

            trigger.OnNext(1);   // leading edge, synchronous, nothing disposed
            trigger.OnNext(2);   // schedules the disposing apply for the next pump

            _host.Advance();     // pump: dispose + re-entrant register, then Remove for `suicidal`
            Assert.That(victim, Is.Not.Null);
            victimGot.Clear();

            victimSource.OnNext(12);
            _host.Advance();

            Assert.That(victimGot, Is.EqualTo(new[] { 12 }),
                "Dispose must NOT free its own slot: a re-entrant Register reuses it, and the " +
                "pump's later Remove would then silently evict that innocent binding forever.");

            victim.Dispose();
        }

        // --- C-1 end-to-end, on the REAL scheduler ------------------------------------------

        [Test]
        public void ReEntrantPushBack_FlushesNextPump_OnTheRealScheduler()
        {
            // CoalescedBindingTests exercises the dirty re-check against a List-backed fake, whose
            // removal is by identity. Here it runs against FreeListCore's slot-index removal, so
            // the two mechanisms that must interlock — MoveNext returning true to stay registered,
            // and the pump removing by slot — are exercised TOGETHER rather than separately.
            var source = new Subject<int>();
            var got = new List<int>();
            bool pushed = false;

            using var binding = new CoalescedBinding<int>(source, _scheduler, v =>
            {
                got.Add(v);
                if (v == 5 && !pushed) { pushed = true; source.OnNext(6); }
            }, null);

            source.OnNext(1);            // leading edge, synchronous
            source.OnNext(5);

            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 1, 5 }));

            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 1, 5, 6 }),
                "The binding must have stayed in its slot across the pump that re-dirtied it.");

            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 1, 5, 6 }), "And then gone idle, not looped.");
        }

        // --- Phase 3: suspension -------------------------------------------------------------

        [Test]
        public void Suspended_AppliesNothing_ThenFlushesOnceWithTheNewestValueOnResume()
        {
            var source = new Subject<int>();
            var got = new List<int>();
            using var binding = new CoalescedBinding<int>(source, _scheduler, got.Add, null);

            source.OnNext(1);            // leading edge, synchronous
            got.Clear();

            IDisposable handle = _scheduler.Suspend();
            source.OnNext(2);
            source.OnNext(3);
            source.OnNext(4);
            _host.Advance(5);

            Assert.That(got, Is.Empty, "Nothing may be applied while suspended.");

            handle.Dispose();
            _host.Advance();

            Assert.That(got, Is.EqualTo(new[] { 4 }),
                "Exactly one apply on resume, carrying the newest value — not one per intermediate.");

            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 4 }), "And then idle.");
        }

        [Test]
        public void FrameCount_DoesNotAdvanceWhileSuspended()
        {
            long before = _scheduler.GetFrameCount();

            using (_scheduler.Suspend())
            {
                _host.Advance(10);
                Assert.That(_scheduler.GetFrameCount(), Is.EqualTo(before),
                    "A suspended scheduler must not burn frames.");
            }

            _host.Advance();
            Assert.That(_scheduler.GetFrameCount(), Is.EqualTo(before + 1),
                "And resumes advancing by exactly one per host frame.");
        }

        [Test]
        public void OverlappingSuspends_Compose_AndReleasingOneDoesNotResume()
        {
            var source = new Subject<int>();
            var got = new List<int>();
            using var binding = new CoalescedBinding<int>(source, _scheduler, got.Add, null);
            source.OnNext(1);
            got.Clear();

            IDisposable a = _scheduler.Suspend();
            IDisposable b = _scheduler.Suspend();
            Assert.That(_scheduler.IsSuspended, Is.True);

            source.OnNext(2);

            a.Dispose();
            _host.Advance();
            Assert.That(_scheduler.IsSuspended, Is.True, "The second handle is still outstanding.");
            Assert.That(got, Is.Empty, "Releasing one of two suspensions must not resume rendering.");

            b.Dispose();
            Assert.That(_scheduler.IsSuspended, Is.False);
            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void HandleDisposedTwice_DecrementsOnce()
        {
            IDisposable a = _scheduler.Suspend();
            IDisposable b = _scheduler.Suspend();

            a.Dispose();
            a.Dispose();   // must be a no-op, NOT a second decrement

            Assert.That(_scheduler.IsSuspended, Is.True,
                "A double release would underflow the refcount and silently cancel b's suspension.");

            b.Dispose();
            Assert.That(_scheduler.IsSuspended, Is.False);
        }

        [Test]
        public void ImmediateBindingLands_WhileACoalescedOneOnTheSameSchedulerIsFrozen()
        {
            // The CONTRAST is the assertion. Checking only that the immediate binding landed proves
            // nothing — an immediate binding never constructs a CoalescedBinding or touches the
            // scheduler at all, so it would hold even if suspension were entirely broken. Pairing
            // it with a coalesced binding on the SAME scheduler at the SAME moment is what makes it
            // mean "Immediate is a real escape hatch from suspension".
            UIBindingExtensions.Scheduler = _scheduler;

            var go = new GameObject("suspend-immediate");
            try
            {
                var activeProp = new ReactiveProperty<bool>(true);
                var coalescedSource = new Subject<int>();
                var coalescedGot = new List<int>();

                using var immediate = activeProp.BindToActive(go);   // Immediate by default
                using var coalesced = new CoalescedBinding<int>(
                    coalescedSource, _scheduler, coalescedGot.Add, null);

                coalescedSource.OnNext(1);   // leading edge
                coalescedGot.Clear();

                using (_scheduler.Suspend())
                {
                    activeProp.Value = false;
                    coalescedSource.OnNext(2);
                    _host.Advance(3);

                    Assert.That(go.activeSelf, Is.False,
                        "Immediate is the documented escape hatch and must still work while suspended.");
                    Assert.That(coalescedGot, Is.Empty,
                        "...and at this same instant a coalesced binding is frozen, which is what " +
                        "makes the assertion above meaningful rather than vacuous.");
                }

                _host.Advance();
                Assert.That(coalescedGot, Is.EqualTo(new[] { 2 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ForeignWorkItems_AreNotSteppedWhileSuspended()
        {
            // This is the mechanism behind the documented ObserveOn flood, and it is OUR behaviour
            // rather than R3's — worth pinning so the doc claim cannot drift from the code.
            var item = new ForeignWorkItem(_ => true);
            _scheduler.Register(item);

            using (_scheduler.Suspend())
            {
                _host.Advance(5);
                Assert.That(item.Calls, Is.Zero,
                    "Foreign operators are not stepped while suspended, so they buffer internally " +
                    "and drain all at once on resume — unlike bindings, which hold one value.");
            }

            _host.Advance();
            Assert.That(item.Calls, Is.EqualTo(1));
        }

        [Test]
        public void DisposedScheduler_DoesNotReportItselfAsPermanentlySuspended()
        {
            IDisposable handle = _scheduler.Suspend();
            Assert.That(_scheduler.IsSuspended, Is.True);

            _scheduler.Dispose();

            Assert.That(_scheduler.IsSuspended, Is.False,
                "A game polling these for liveness would otherwise read 'permanently suspended, " +
                "frame count not advancing' after teardown — a false leak alarm from the very API " +
                "that exists to detect real ones.");
            Assert.That(_scheduler.SuspendedFrames, Is.Zero);

            handle.Dispose();   // must not throw, nor resurrect state
            Assert.That(_scheduler.IsSuspended, Is.False);

            using (_scheduler.Suspend())
                Assert.That(_scheduler.IsSuspended, Is.False, "Suspending a dead scheduler is inert.");
        }

        [Test]
        public void MaxSuspendedFramesZero_DisablesTheCap()
        {
            var scheduler = new UIRenderScheduler(_host, maxSuspendedFrames: 0);
            try
            {
                using (scheduler.Suspend())
                {
                    _host.Advance(50);
                    Assert.That(scheduler.SuspendedFrames, Is.EqualTo(50));
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        [Test]
        public void IdleBindingThatWakesWhileSuspended_StillFlushesOnResume()
        {
            // The case that would break if Register() were ever gated on _suspendCount: this
            // binding has already unregistered itself, so its next value re-registers DURING the
            // suspension. Gate that and it is stranded forever, flushing on no later pump at all.
            var source = new Subject<int>();
            var got = new List<int>();
            using var binding = new CoalescedBinding<int>(source, _scheduler, got.Add, null);

            source.OnNext(1);       // leading edge
            source.OnNext(2);
            _host.Advance();        // applies 2, then goes idle (unregisters)
            got.Clear();

            using (_scheduler.Suspend())
            {
                source.OnNext(3);   // must still reach Register()
                _host.Advance(3);
                Assert.That(got, Is.Empty);
            }

            _host.Advance();
            Assert.That(got, Is.EqualTo(new[] { 3 }),
                "An idle binding woken during a suspension must still flush on resume.");
        }

        [Test]
        public void BindingDisposedDuringSuspension_IsRemovedFromTheListByTheFirstPumpAfterResume()
        {
            // Asserts the LIST LENGTH, not just "was not applied" — the latter is guaranteed by the
            // _disposed check whether or not removal ever happens, so it would prove nothing about
            // the no-sweep-needed claim.
            var source = new Subject<int>();
            var binding = new CoalescedBinding<int>(source, _scheduler, _ => { }, null);

            source.OnNext(1);       // leading edge
            source.OnNext(2);       // registers
            int registeredBefore = _scheduler.RegisteredCount;
            Assert.That(registeredBefore, Is.EqualTo(1), "Precondition: the binding is registered.");

            using (_scheduler.Suspend())
            {
                binding.Dispose();
                _host.Advance(5);
                Assert.That(_scheduler.RegisteredCount, Is.EqualTo(1),
                    "It cannot be reclaimed while the pump is stopped — that is the accumulation " +
                    "the sprint plan worried about.");
            }

            _host.Advance();
            Assert.That(_scheduler.RegisteredCount, Is.Zero,
                "One pump after resume reclaims it, which is why no explicit sweep is needed.");
        }

        [Test]
        public void SuspensionCap_LogsExactlyOneError_AndDoesNotResume()
        {
            var scheduler = new UIRenderScheduler(_host, maxSuspendedFrames: 3);
            try
            {
                using (scheduler.Suspend())
                {
                    LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex("suspended for 4 frames"));

                    _host.Advance(10);   // one error at frame 4, and nothing after

                    Assert.That(scheduler.IsSuspended, Is.True,
                        "The cap is a diagnostic, not a safety net — it must never force-resume.");
                    Assert.That(scheduler.SuspendedFrames, Is.EqualTo(10));

                    // Explicit: a second log would carry a different frame count, miss the regex,
                    // and surface here rather than being silently tolerated.
                    LogAssert.NoUnexpectedReceived();
                }

                Assert.That(scheduler.IsSuspended, Is.False);
                Assert.That(scheduler.SuspendedFrames, Is.Zero, "Reset for the next episode.");
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        [Test]
        public void PumpStaysRegisteredOnTheHost_AcrossASuspension()
        {
            using (_scheduler.Suspend())
            {
                _host.Advance(5);
                Assert.That(_host.ItemCount, Is.EqualTo(1),
                    "If the pump were evicted while suspended, nothing could ever resume it.");
            }

            _host.Advance();
            Assert.That(_host.ItemCount, Is.EqualTo(1));
        }

        // --- disposal of the scheduler --------------------------------------------------------

        [Test]
        public void DisposedScheduler_StopsPumping_AndReleasesTheHostSlot()
        {
            var item = new ForeignWorkItem(_ => true);
            _scheduler.Register(item);
            _host.Advance();
            Assert.That(item.Calls, Is.EqualTo(1));

            _scheduler.Dispose();
            _host.Advance();
            _host.Advance();

            Assert.That(item.Calls, Is.EqualTo(1), "Nothing is pumped after disposal.");
            Assert.That(_host.ItemCount, Is.Zero, "The pump returns false once, releasing the host slot.");
        }
    }
}
