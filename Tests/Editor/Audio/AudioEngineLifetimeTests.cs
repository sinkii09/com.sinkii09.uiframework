using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Handles, pending starts, cooldown, reclaim and the tick.
    public class AudioEngineLifetimeTests
    {
        private FakeVoiceBackend _backend;
        private AudioEngine _engine;
        private GroupDefinition _group;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakeVoiceBackend(12);
            _engine = new AudioEngine(_backend, 8, 0.1f, null);
            _group = TestSounds.Group();
        }

        [Test]
        public void Handle_AfterSlotReuse_IsStaleAndStopIsNoOp()
        {
            var sound = TestSounds.Event(_engine, "a", _group);
            var old = _engine.Play(sound);
            old.Stop(0);
            _engine.Tick(0.016f);
            var reused = _engine.Play(sound);

            Assert.AreEqual(old.Slot, reused.Slot, "Precondition: the slot was reused.");
            Assert.IsFalse(old.IsValid);
            old.Stop(0);
            Assert.IsTrue(reused.IsPlaying, "A stale handle must not stop the slot's new owner.");
        }

        [Test]
        public void Handle_Default_IsInvalidAndSafe()
        {
            PlayHandle handle = default;
            Assert.IsFalse(handle.IsValid);
            Assert.IsFalse(handle.IsPlaying);
            Assert.DoesNotThrow(() => handle.Stop());
        }

        [Test]
        public void Pending_StartsOnlyWhenDelayExpires_AndHoldsNoVoice()
        {
            var sound = TestSounds.Event(_engine, "a", _group, delay: 0.25f);
            var handle = _engine.Play(sound);

            Assert.IsTrue(handle.IsValid);
            Assert.IsFalse(handle.IsPlaying);
            Assert.AreEqual(0, _backend.Starts);

            _engine.Tick(0.1f);
            _engine.Tick(0.1f);
            Assert.AreEqual(0, _backend.Starts);
            _engine.Tick(0.1f);
            Assert.IsTrue(handle.IsPlaying);
        }

        [Test]
        public void Pending_StopBeforeStart_CancelsIt()
        {
            var sound = TestSounds.Event(_engine, "a", _group, delay: 0.1f);
            var handle = _engine.Play(sound);
            handle.Stop();
            _engine.Tick(0.1f);

            Assert.AreEqual(0, _backend.Starts);
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void Pending_ContextLockedAtExpiry_IsDropped()
        {
            var sound = TestSounds.Event(_engine, "a", _group, delay: 0.1f);
            var handle = _engine.Play(sound);
            _engine.ContextRunning = false;
            _engine.Tick(0.1f);

            Assert.AreEqual(0, _backend.Starts, "Would otherwise join Unity's replay-on-unlock queue.");
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(1, _engine.Counters.Dropped);
        }

        [Test]
        public void Cooldown_IsMeasuredFromLastAcceptedRequest()
        {
            var sound = TestSounds.Event(_engine, "a", TestSounds.Group(cooldown: 0.2f));

            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(sound));
            _engine.Tick(0.1f);
            Assert.AreEqual(PlayStatus.Cooldown, _engine.PlayStatusOf(sound));
            _engine.Tick(0.1f);
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(sound),
                "The refused request at 0.1 s must not have restarted the cooldown.");
        }

        [Test]
        public void Tick_LargeDelta_IsClampedToMax()
        {
            _engine.Tick(5f);
            Assert.AreEqual(0.1, _engine.Now, 1e-6);
        }

        [Test]
        public void Tick_WhilePaused_DoesNotReclaimPausedVoices()
        {
            var sound = TestSounds.Event(_engine, "a", _group);
            var handle = _engine.Play(sound);
            _engine.Paused = true;
            _backend.Finish(0); // the first play takes voice 0; a paused source reports not playing

            _engine.Tick(0.016f);
            Assert.IsTrue(handle.IsPlaying, "Paused is not finished.");

            _engine.Paused = false;
            _engine.Tick(0.016f);
            Assert.IsFalse(handle.IsValid, "Once unpaused, a voice that really ended is reclaimed.");
        }

        [Test]
        public void Play_WhilePaused_IsDroppedAndHoldsNoVoice()
        {
            var sound = TestSounds.Event(_engine, "a", _group);
            _engine.Paused = true;

            Assert.AreEqual(PlayStatus.Paused, _engine.PlayStatusOf(sound));
            Assert.AreEqual(0, _backend.Starts, "A source started under AudioListener.pause would fire on resume.");
            Assert.AreEqual(1, _engine.Counters.Dropped);
        }

        [Test]
        public void UnregisterGroup_ReleasesItsReservationAndDuckerSlot()
        {
            var engine = new AudioEngine(_backend, 2, 0.1f, null);
            var old = TestSounds.Group("old", reserved: 2);
            old.Ducks = true;
            old.DuckTarget = AudioBus.Music;
            old.DuckDb = -6f;
            engine.RegisterGroup(old);
            var sfx = TestSounds.Event(engine, "coin", TestSounds.Group("sfx", onLimit: LimitPolicy.Reject));
            Assert.AreEqual(PlayStatus.Rejected, engine.PlayStatusOf(sfx), "Precondition: both voices reserved.");

            engine.UnregisterGroup(old);
            var reloaded = TestSounds.Group("old-reloaded");
            reloaded.Ducks = true;
            reloaded.DuckTarget = AudioBus.Music;
            engine.RegisterGroup(reloaded);

            Assert.AreEqual(PlayStatus.Started, engine.PlayStatusOf(sfx));
            Assert.AreEqual(2, engine.GroupCount);
        }

        [Test]
        public void Reclaim_NaturalEnd_FreesVoiceAndReleasesClip()
        {
            var sound = TestSounds.Event(_engine, "a", _group);
            var handle = _engine.Play(sound);
            _backend.Finish(0);
            _engine.Tick(0.016f);

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, _engine.PlayingCount);
            Assert.Contains((0, 0), _backend.Stops, "Reclaim cuts the source so it drops its clip reference.");
        }

        [Test]
        public void Missing_ReturnsInvalidAndCounts()
        {
            var handle = _engine.Play(SoundId.FromPath("nope"), default, out var status);
            Assert.AreEqual(PlayStatus.Missing, status);
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(1, _engine.Counters.Missing);
        }

        [Test]
        public void UnregisterEvent_CutsActiveAndPending()
        {
            var sound = TestSounds.Event(_engine, "a", _group);
            var later = TestSounds.Event(_engine, "b", _group, delay: 1f);
            var active = _engine.Play(sound);
            var pending = _engine.Play(later);

            _engine.UnregisterEvent(sound.Id);
            _engine.UnregisterEvent(later.Id);

            Assert.IsFalse(active.IsValid);
            Assert.IsFalse(pending.IsValid);
            Assert.AreEqual(0, _backend.ActiveCount);
            Assert.AreEqual(0, _engine.PlayingCount);
            _engine.Tick(1f);
            Assert.AreEqual(1, _backend.Starts, "Only the immediate play started; the cancelled pending one never does.");
        }

        [Test]
        public void RegisterEvent_Collision_IsRefusedWithError()
        {
            string error = null;
            var engine = new AudioEngine(_backend, 8, 0.1f, e => error = e);
            TestSounds.Event(engine, "a", _group);
            var impostor = new EventDefinition(SoundId.FromPath("a"), _group, 1) { Path = "a-copy" };

            Assert.IsFalse(engine.RegisterEvent(impostor));
            StringAssert.Contains("collision", error);
        }
    }
}
