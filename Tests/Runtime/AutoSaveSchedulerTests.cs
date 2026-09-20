using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // AutoSaveScheduler's job is deciding WHEN to write. Every assertion here is about that decision,
    // driven through the internal Tick(dt) seam so a debounce test can say "not yet, now" instead of
    // zeroing the timers and asserting whatever the implementation happens to do.
    //
    // NOT covered here: OnApplicationPause itself. Unity never raises it in a test run, in either
    // mode. FlushSynchronously is called directly instead, and the wiring from the message to this
    // method is one Subscribe in the constructor — the part that only a device can prove is that
    // Android delivers the message at all, which is why the plan keeps a manual kill-from-recents
    // check that no test replaces.
    public class AutoSaveSchedulerTests
    {
        private const string Key = "AutoSaveTestKey";
        private const float Debounce = 2f;
        private const float MaxLatency = 15f;

        private static AutoSaveScheduler NewScheduler(CountingSaveService saves)
            => new(saves, Debounce, MaxLatency);

        [Test]
        public void ManyMarksInsideOneWindow_ProduceExactlyOneWrite()
        {
            // The reason this class exists: ISaveService rewrites the entire JSON payload per save,
            // so ten marks must not mean ten rewrites of the player's whole progress.
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            var payload = new CountingSavePayload();
            scheduler.Register(Key, () => payload);

            for (var i = 0; i < 10; i++)
            {
                scheduler.MarkDirty(Key);
                scheduler.Tick(0.1f);
            }

            Assert.That(saves.AsyncWrites, Is.Zero, "Still inside the debounce window.");

            scheduler.Tick(Debounce);
            Assert.That(saves.AsyncWrites, Is.EqualTo(1));
        }

        [Test]
        public void KeyRemarkedForever_StillWritesAtTheLatencyCap()
        {
            // Without the cap, state that changes every frame restarts the debounce forever and is
            // never written at all — the failure mode debouncing introduces if left alone.
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());

            for (var elapsed = 0f; elapsed < MaxLatency - 1f; elapsed += 0.5f)
            {
                scheduler.MarkDirty(Key);       // debounce never gets to expire
                scheduler.Tick(0.5f);
            }

            Assert.That(saves.AsyncWrites, Is.Zero, "Cap not reached yet.");

            scheduler.MarkDirty(Key);
            scheduler.Tick(1.5f);
            Assert.That(saves.AsyncWrites, Is.EqualTo(1), "The cap must fire even while marks keep arriving.");
        }

        [Test]
        public void NothingDirty_NeverWrites()
        {
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());

            for (var i = 0; i < 100; i++) scheduler.Tick(1f);

            Assert.That(saves.AsyncWrites, Is.Zero);
        }

        [Test]
        public void SnapshotIsTakenWhenTheWriteStarts_NotWhenTheKeyIsMarked()
        {
            // The whole point of taking a Func rather than a value: a mark is cheap because it copies
            // nothing. If the snapshot were captured at mark time, coalescing ten marks would write
            // the FIRST state and discard the other nine.
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            var live = new CountingSavePayload { Value = 1 };
            var snapshots = 0;

            // Returns a COPY, which is what IAutoSaveScheduler asks for — and what makes this test
            // able to fail. Handing back the same live instance would read 42 either way, so the
            // assertion would hold even if the snapshot were taken at mark time.
            scheduler.Register(Key, () =>
            {
                snapshots++;
                return new CountingSavePayload { Value = live.Value };
            });

            scheduler.MarkDirty(Key);
            Assert.That(snapshots, Is.Zero, "Marking must not read the payload — that is what makes it cheap.");

            live.Value = 42;
            scheduler.Tick(Debounce);

            Assert.That(snapshots, Is.EqualTo(1));
            Assert.That(saves.Payloads, Has.Count.EqualTo(1));
            Assert.That(((CountingSavePayload)saves.Payloads[0]).Value, Is.EqualTo(42),
                "A snapshot captured at mark time would have written 1.");
        }

        [Test]
        public void MarkDirty_OnAnUnregisteredKey_Throws()
        {
            // Fails fast on a programmer error. Ignoring it would mean this exact key is the one that
            // silently never saves, discovered by a player instead of by a test.
            var scheduler = NewScheduler(new CountingSaveService());

            Assert.That(() => scheduler.MarkDirty("NeverRegistered"),
                Throws.InstanceOf<System.InvalidOperationException>());
        }

        [Test]
        public void PauseFlush_WritesSynchronously_AndClearsTheKey()
        {
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());
            scheduler.MarkDirty(Key);

            scheduler.FlushSynchronously();

            Assert.That(saves.SyncWrites, Is.EqualTo(1));
            Assert.That(saves.AsyncWrites, Is.Zero, "The pause path must not await anything.");

            // Already clean: a later tick must not rewrite the same state.
            scheduler.Tick(MaxLatency);
            Assert.That(saves.SyncWrites + saves.AsyncWrites, Is.EqualTo(1));
        }

        [Test]
        public void PauseFlush_WithoutSyncSupport_FallsBackAndWarnsRatherThanDoingNothing()
        {
            // AsyncOnlySaveService does not implement ISynchronousSaveService at all, which is what
            // the scheduler actually probes for. An earlier version of this test used a fake that
            // implemented the interface and returned false, so it never reached this branch.
            var saves = new AsyncOnlySaveService();
            var scheduler = new AutoSaveScheduler(saves, Debounce, MaxLatency);
            scheduler.Register(Key, () => new CountingSavePayload());
            scheduler.MarkDirty(Key);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("AutoSave.*synchronously"));
            scheduler.FlushSynchronously();

            Assert.That(saves.AsyncWrites, Is.EqualTo(1), "Racing the OS still beats writing nothing.");
        }

        [UnityTest]
        public IEnumerator FlushAsync_WithNothingDirty_WritesNothing() => UniTask.ToCoroutine(async () =>
        {
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());

            await scheduler.FlushAsync();

            Assert.That(saves.AsyncWrites, Is.Zero);
        });

        [UnityTest]
        public IEnumerator FlushAsync_AfterTheFirstOneLands_DoesNotRewrite() => UniTask.ToCoroutine(async () =>
        {
            var saves = new CountingSaveService();
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());
            scheduler.MarkDirty(Key);

            await scheduler.FlushAsync();
            await scheduler.FlushAsync();

            Assert.That(saves.AsyncWrites, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator FailedWrite_LeavesTheKeyDirtySoTheNextWindowRetries() => UniTask.ToCoroutine(async () =>
        {
            // A transient failure must not consume the change. Clearing the dirty flag on failure
            // means the next successful save writes state that never included it.
            var saves = new CountingSaveService { FailNextAsyncWrite = true };
            var scheduler = NewScheduler(saves);
            scheduler.Register(Key, () => new CountingSavePayload());
            scheduler.MarkDirty(Key);

            // try/catch rather than Assert.That(async () => ...): that overload binds to NUnit's
            // AsyncTestDelegate, which pumps a blocking single-threaded context. It survives here
            // only because the fake never yields, and would deadlock against a UniTask continuation
            // waiting on the player loop the moment a real save service were used.
            var threw = false;
            try
            {
                await scheduler.FlushAsync();
            }
            catch (System.InvalidOperationException)
            {
                threw = true;
            }

            Assert.That(threw, Is.True, "A failed write must surface to whoever asked for the flush.");

            await scheduler.FlushAsync();
            Assert.That(saves.AsyncWrites, Is.EqualTo(1), "The retry must actually land.");
        });
    }
}
