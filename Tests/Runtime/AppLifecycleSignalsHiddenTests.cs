using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // The page-hidden signal (WebGL) and its wiring into AutoSaveScheduler's synchronous flush.
    // RaiseHidden is the seam the jslib's visibilitychange/pagehide handler calls; a test stands in
    // for the browser. The real scheduler constructor is used on purpose: the subscription is the
    // part under test, and the internal test ctor has none.
    public class AppLifecycleSignalsHiddenTests
    {
        private const string Key = "HiddenTestKey";

        private readonly GameObjectTracker _gos = new();
        private UIFrameworkConfig _config;
        private AutoSaveScheduler _scheduler;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<UIFrameworkConfig>();

        [TearDown]
        public void TearDown()
        {
            _scheduler?.Dispose();
            _scheduler = null;
            _gos.DestroyAll();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        private AppLifecycleSignals NewSignals()
            => _gos.Track(new GameObject("Signals")).AddComponent<AppLifecycleSignals>();

        private AutoSaveScheduler NewScheduler(CountingSaveService saves, AppLifecycleSignals signals)
        {
            _scheduler = new AutoSaveScheduler(saves, signals, _config);
            _scheduler.Register(Key, () => new CountingSavePayload());
            return _scheduler;
        }

        [Test]
        public void Hidden_WithDirtyKey_WritesSynchronouslyInsideTheCall()
        {
            var saves = new CountingSaveService();
            var signals = NewSignals();
            NewScheduler(saves, signals).MarkDirty(Key);

            // No frame may pass: a hidden tab has already stopped the player loop.
            signals.RaiseHidden();

            Assert.AreEqual(1, saves.SyncWrites);
            Assert.AreEqual(0, saves.AsyncWrites, "The hide path must not await anything.");
        }

        [Test]
        public void HiddenThenPageHide_WritesOnce()
        {
            // Browsers raise visibilitychange(hidden) and then pagehide for one tab close.
            var saves = new CountingSaveService();
            var signals = NewSignals();
            NewScheduler(saves, signals).MarkDirty(Key);

            signals.RaiseHidden();
            signals.RaiseHidden();

            Assert.AreEqual(1, saves.SyncWrites);
        }

        [Test]
        public void Hidden_WithNothingDirty_WritesNothing()
        {
            var saves = new CountingSaveService();
            var signals = NewSignals();
            NewScheduler(saves, signals);

            signals.RaiseHidden();

            Assert.AreEqual(0, saves.SyncWrites + saves.AsyncWrites);
        }

        [Test]
        public void Hidden_WhileAsyncSaveInFlight_StillWritesTheNewerState()
        {
            // Same contract as the pause path: an in-flight write may carry older data, so a Saving
            // source is not skipped and the synchronous save settles it.
            var saves = new CountingSaveService { Gate = new UniTaskCompletionSource() };
            var signals = NewSignals();
            var scheduler = NewScheduler(saves, signals);
            scheduler.MarkDirty(Key);
            scheduler.Tick(_config.AutoSaveMaxLatencySeconds);   // starts the held async write
            scheduler.MarkDirty(Key);

            signals.RaiseHidden();

            Assert.AreEqual(1, saves.SyncWrites);
            saves.Gate.TrySetResult();
        }

        [Test]
        public void Hidden_ThrowingGameSubscriber_DoesNotStopTheFlush()
        {
            // Subscribed BEFORE the scheduler, so it runs first. R3 routes an onNext exception to
            // its unhandled-exception handler (logged) rather than aborting the other observers;
            // the expectation pins both halves: it is reported, and the save still lands.
            var saves = new CountingSaveService();
            var signals = NewSignals();
            using var gameSubscription = signals.OnHiddenAsObservable
                .Subscribe(_ => throw new InvalidOperationException("game handler failed"));
            NewScheduler(saves, signals).MarkDirty(Key);
            LogAssert.Expect(LogType.Exception, new Regex("game handler failed"));

            signals.RaiseHidden();

            Assert.AreEqual(1, saves.SyncWrites);
        }

        [Test]
        public void Hidden_AfterSchedulerDisposed_WritesNothing()
        {
            // Dispose runs its own last-chance flush; a hide afterwards must not write again or
            // reach a disposed scheduler.
            var saves = new CountingSaveService();
            var signals = NewSignals();
            var scheduler = NewScheduler(saves, signals);
            scheduler.MarkDirty(Key);
            scheduler.Dispose();
            Assert.AreEqual(1, saves.SyncWrites, "precondition: Dispose flushed");

            Assert.DoesNotThrow(() => signals.RaiseHidden());

            Assert.AreEqual(1, saves.SyncWrites + saves.AsyncWrites);
        }

        [Test]
        public void Bridge_ThrowingHiddenHandler_IsLoggedNotRethrown()
        {
            // A managed exception crossing back into the jslib aborts the WebGL player.
            Action throwing = () => throw new InvalidOperationException("boom");
            WebGlBrowserBridge.Register(throwing);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[Persistence\] Page-hidden handler threw"));

                Assert.DoesNotThrow(WebGlBrowserBridge.HandleHidden);
            }
            finally
            {
                WebGlBrowserBridge.Unregister(throwing);
            }
        }

        [Test]
        public void Bridge_StaleOwnerUnregistering_DoesNotCutOffTheNewOwner()
        {
            // A component torn down after its replacement registered must not unhook the new one.
            var calls = 0;
            Action stale = () => { };
            Action current = () => calls++;
            WebGlBrowserBridge.Register(stale);
            WebGlBrowserBridge.Register(current);
            try
            {
                WebGlBrowserBridge.Unregister(stale);
                WebGlBrowserBridge.HandleHidden();

                Assert.AreEqual(1, calls);
            }
            finally
            {
                WebGlBrowserBridge.Unregister(current);
            }
        }

        [Test]
        public void Hidden_AfterDestroy_IsIgnored()
        {
            var signals = NewSignals();
            UnityEngine.Object.DestroyImmediate(signals.gameObject);

            Assert.DoesNotThrow(() => signals.RaiseHidden());
        }
    }
}
