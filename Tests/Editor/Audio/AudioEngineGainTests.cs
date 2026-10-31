using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Gain composition, clamp, mute, ducking, and the clip containers.
    public class AudioEngineGainTests
    {
        private FakeVoiceBackend _backend;
        private AudioEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakeVoiceBackend(12);
            _engine = new AudioEngine(_backend, 8, 0.1f, null);
        }

        [Test]
        public void Gain_SumsEventGroupOptionsBusAndMasterInDb()
        {
            var group = TestSounds.Group();
            group.VolumeDb = -2f;
            var sound = TestSounds.Event(_engine, "a", group);
            sound.VolumeDb = -4f;
            _engine.SetBusUserDb(AudioBus.Sfx, -6f);
            _engine.SetBusUserDb(AudioBus.Master, -3f);

            _engine.Play(sound.Id, new PlayOptions(-5f), out _);

            Assert.AreEqual(AudioMath.ToLinear(-20f), _backend.Gain[0], 1e-5f);
        }

        [Test]
        public void Gain_PositiveTotal_IsClampedToUnity()
        {
            var sound = TestSounds.Event(_engine, "a", TestSounds.Group());
            sound.VolumeDb = 12f;
            _engine.Play(sound);
            Assert.AreEqual(1f, _backend.Gain[0]);
        }

        [Test]
        public void MutedBus_DropsNewPlays_AndSilencesPlayingOnes()
        {
            var sound = TestSounds.Event(_engine, "a", TestSounds.Group());
            var handle = _engine.Play(sound);

            _engine.SetBusMuted(AudioBus.Sfx, true);
            _engine.Tick(0.016f);

            Assert.AreEqual(0f, _backend.Gain[0]);
            Assert.IsTrue(handle.IsPlaying, "Muting silences; it does not stop.");
            Assert.AreEqual(PlayStatus.Muted, _engine.PlayStatusOf(sound));
        }

        [Test]
        public void ContextLocked_DropsSfxButNotMusicBus()
        {
            var sfx = TestSounds.Event(_engine, "sfx", TestSounds.Group());
            var music = TestSounds.Event(_engine, "music", TestSounds.Group("music", bus: AudioBus.Music));
            _engine.ContextRunning = false;

            Assert.AreEqual(PlayStatus.ContextLocked, _engine.PlayStatusOf(sfx));
            Assert.AreEqual(PlayStatus.Started, _engine.PlayStatusOf(music));
        }

        [Test]
        public void Ducking_AttacksWhileOwnerPlays_ThenReleases()
        {
            var voice = TestSounds.Group("voice");
            voice.Ducks = true;
            voice.DuckTarget = AudioBus.Music;
            voice.DuckDb = -10f;
            voice.DuckAttackMs = 100;
            voice.DuckReleaseMs = 200;
            var line = TestSounds.Event(_engine, "line", voice);

            var handle = _engine.Play(line);
            _engine.Tick(0.05f);
            Assert.AreEqual(-5f, _engine.GetDuckDb(AudioBus.Music), 1e-4f);
            _engine.Tick(0.05f);
            Assert.AreEqual(-10f, _engine.GetDuckDb(AudioBus.Music), 1e-4f);

            handle.Stop(0);
            _engine.Tick(0.1f);
            Assert.AreEqual(-5f, _engine.GetDuckDb(AudioBus.Music), 1e-4f);
        }

        [Test]
        public void Ducking_SecondDuckerOnSameBus_IsIgnoredWithError()
        {
            string error = null;
            var engine = new AudioEngine(_backend, 8, 0.1f, e => error = e);
            var first = TestSounds.Group("first");
            var second = TestSounds.Group("second");
            first.Ducks = second.Ducks = true;
            first.DuckTarget = second.DuckTarget = AudioBus.Music;
            first.DuckDb = second.DuckDb = -6f;
            var sound = TestSounds.Event(engine, "b", second);
            engine.RegisterGroup(first);
            TestSounds.Event(engine, "a", first);

            engine.Play(sound);
            engine.Tick(0.1f);

            StringAssert.Contains("One ducker per bus", error);
            Assert.AreEqual(-6f, engine.GetDuckDb(AudioBus.Music), 1e-4f, "The first registered ducker keeps the bus.");
        }

        [Test]
        public void RandomContainer_NeverRepeatsWithinWindow()
        {
            var sound = TestSounds.Event(_engine, "r", TestSounds.Group(), clips: 3);
            sound.AvoidRepeat = 2;
            SoundVariation.Prepare(sound);
            var random = new AudioRandom(7);

            int a = SoundVariation.PickClip(sound, ref random);
            int b = SoundVariation.PickClip(sound, ref random);
            for (int i = 0; i < 50; i++)
            {
                int c = SoundVariation.PickClip(sound, ref random);
                Assert.AreNotEqual(a, c);
                Assert.AreNotEqual(b, c);
                (a, b) = (b, c);
            }
        }

        [Test]
        public void ShuffleContainer_PlaysEachOncePerRound_AndNeverRepeatsAcrossRounds()
        {
            var sound = TestSounds.Event(_engine, "s", TestSounds.Group(), clips: 4);
            sound.Container = SoundContainerMode.Shuffle;
            SoundVariation.Prepare(sound);
            var random = new AudioRandom(3);
            int last = -1;

            for (int round = 0; round < 20; round++)
            {
                int seen = 0;
                for (int i = 0; i < 4; i++)
                {
                    int pick = SoundVariation.PickClip(sound, ref random);
                    Assert.AreNotEqual(last, pick);
                    seen |= 1 << pick;
                    last = pick;
                }
                Assert.AreEqual(0b1111, seen);
            }
        }

        [Test]
        public void SoundId_IsStableNonZeroAndDistinguishesPaths()
        {
            Assert.AreEqual(SoundId.FromPath("ui/click"), SoundId.FromPath("ui/click"));
            Assert.AreNotEqual(SoundId.FromPath("ui/click"), SoundId.FromPath("ui/Click"));
            Assert.IsTrue(SoundId.FromPath("x").IsValid);
            Assert.IsFalse(SoundId.FromPath("").IsValid);
            Assert.IsFalse(SoundId.FromPath(null).IsValid);
        }
    }
}
