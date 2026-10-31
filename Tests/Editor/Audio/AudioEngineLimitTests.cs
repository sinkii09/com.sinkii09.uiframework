using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class AudioEngineLimitTests
    {
        private FakeVoiceBackend _backend;
        private AudioEngine _engine;

        private void Build(int pool, int headroom = 4)
        {
            _backend = new FakeVoiceBackend(pool + headroom);
            _engine = new AudioEngine(_backend, pool, 0.1f, null);
        }

        [Test]
        public void Play_EventCapWithReject_RejectsAndCounts()
        {
            Build(8);
            var group = TestSounds.Group(perEvent: 2, onLimit: LimitPolicy.Reject);
            var sound = TestSounds.Event(_engine, "a", group);

            _engine.Play(sound);
            _engine.Play(sound);
            var status = _engine.PlayStatusOf(sound);

            Assert.AreEqual(PlayStatus.Rejected, status);
            Assert.AreEqual(2, _engine.PlayingCount);
            Assert.AreEqual(1, _engine.Counters.Rejected);
        }

        [Test]
        public void Play_EventCapWithStealOldest_FadesOldestAndStartsNew()
        {
            Build(8);
            var group = TestSounds.Group(perEvent: 2, onLimit: LimitPolicy.StealOldest);
            var sound = TestSounds.Event(_engine, "a", group);

            var first = _engine.Play(sound);
            var second = _engine.Play(sound);
            var third = _engine.Play(sound);

            Assert.IsFalse(first.IsPlaying, "Oldest is stolen.");
            Assert.IsTrue(first.IsValid, "A stolen voice is still alive while it fades.");
            Assert.IsTrue(second.IsPlaying);
            Assert.IsTrue(third.IsPlaying);
            Assert.AreEqual(1, _engine.Counters.Stolen);
            Assert.AreEqual(10, _backend.Stops[0].FadeMs, "Steal uses the group's StopFadeMs, never a cut.");
        }

        [Test]
        public void Play_GroupCap_AppliesAcrossEvents()
        {
            Build(8);
            var group = TestSounds.Group(maxVoices: 2, onLimit: LimitPolicy.Reject);
            var a = TestSounds.Event(_engine, "a", group);
            var b = TestSounds.Event(_engine, "b", group);

            _engine.Play(a);
            _engine.Play(b);

            Assert.AreEqual(PlayStatus.Rejected, _engine.PlayStatusOf(a));
        }

        [Test]
        public void Play_StealLowestPriority_TakesLowestAndTiesGoToOldest()
        {
            Build(8);
            var group = TestSounds.Group(maxVoices: 3, onLimit: LimitPolicy.StealLowestPriority);
            var low1 = TestSounds.Event(_engine, "low1", group);
            var low2 = TestSounds.Event(_engine, "low2", group);
            var high = TestSounds.Event(_engine, "high", group);
            low1.Priority = 10;
            low2.Priority = 10;
            high.Priority = 200;

            var h1 = _engine.Play(low1);
            var h2 = _engine.Play(low2);
            var h3 = _engine.Play(high);
            _engine.Play(high);

            Assert.IsFalse(h1.IsPlaying, "Lowest priority, oldest of the tie.");
            Assert.IsTrue(h2.IsPlaying);
            Assert.IsTrue(h3.IsPlaying);
        }

        [Test]
        public void Play_StealLowestPriority_NeverStealsHigherPriority()
        {
            Build(8);
            var group = TestSounds.Group(maxVoices: 1, onLimit: LimitPolicy.StealLowestPriority);
            var high = TestSounds.Event(_engine, "high", group);
            var low = TestSounds.Event(_engine, "low", group);
            high.Priority = 200;
            low.Priority = 10;

            var kept = _engine.Play(high);

            Assert.AreEqual(PlayStatus.Rejected, _engine.PlayStatusOf(low));
            Assert.IsTrue(kept.IsPlaying);
        }

        [Test]
        public void Play_GlobalStealWithHeadroom_Succeeds()
        {
            Build(pool: 2, headroom: 2);
            var group = TestSounds.Group();
            var sound = TestSounds.Event(_engine, "a", group);

            var first = _engine.Play(sound);
            _engine.Play(sound);
            var third = _engine.Play(sound);

            Assert.IsTrue(third.IsPlaying, "The victim fades on a headroom voice while the new one starts.");
            Assert.IsFalse(first.IsPlaying);
            Assert.AreEqual(2, _engine.PlayingCount, "The pool cap counts Playing only.");
        }

        [Test]
        public void Play_HeadroomExhausted_StoppingVoicesHoldTheirSourcesAndRequestIsRejected()
        {
            Build(pool: 1, headroom: 1);
            var group = TestSounds.Group();
            var sound = TestSounds.Event(_engine, "a", group);

            _engine.Play(sound);
            _engine.Play(sound); // steals #1, which fades on the headroom voice
            var status = _engine.PlayStatusOf(sound); // steals #2, but no source is free

            Assert.AreEqual(PlayStatus.Rejected, status);
            Assert.AreEqual(2, _backend.ActiveCount, "Both fading voices still hold a source.");

            _backend.CompleteFades();
            _engine.Tick(0.016f);
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(sound), "Sources come back once fades finish.");
        }

        [Test]
        public void Play_ReservedVoices_AreHeldBackFromOtherGroups()
        {
            Build(pool: 4);
            var ui = TestSounds.Group("ui", reserved: 2, onLimit: LimitPolicy.Reject);
            var sfx = TestSounds.Group("sfx", onLimit: LimitPolicy.Reject);
            var click = TestSounds.Event(_engine, "click", ui);
            var coin = TestSounds.Event(_engine, "coin", sfx);

            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(coin));
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(coin));
            Assert.AreEqual(PlayStatus.Rejected, _engine.PlayStatusOf(coin), "Two of four are reserved for UI.");
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(click));
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(click));
        }

        [Test]
        public void Play_GlobalSteal_NeverTakesFromAnotherGroupsReservation()
        {
            Build(pool: 3);
            var ui = TestSounds.Group("ui", reserved: 2);
            var sfx = TestSounds.Group("sfx");
            var click = TestSounds.Event(_engine, "click", ui);
            var coin = TestSounds.Event(_engine, "coin", sfx);

            var c1 = _engine.Play(click);
            var c2 = _engine.Play(click);
            _engine.Play(coin);
            var status = _engine.PlayStatusOf(coin); // pool full: may steal only the other coin

            Assert.AreEqual(PlayStatus.Started, status);
            Assert.IsTrue(c1.IsPlaying);
            Assert.IsTrue(c2.IsPlaying);
        }

        [Test]
        public void Play_InstanceTableFullOfPending_RejectsAndCounts()
        {
            Build(pool: 1, headroom: 0);
            var group = TestSounds.Group();
            var delayed = TestSounds.Event(_engine, "delayed", group, delay: 1f);

            // Table = voices (1) + pool (1) = 2 slots.
            _engine.Play(delayed);
            _engine.Play(delayed);

            Assert.AreEqual(PlayStatus.Rejected, _engine.PlayStatusOf(delayed));
            Assert.AreEqual(1, _engine.Counters.Rejected);
        }
    }
}
