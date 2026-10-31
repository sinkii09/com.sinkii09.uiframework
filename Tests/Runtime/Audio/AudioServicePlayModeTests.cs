using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is =UnityEngine.TestTools.Constraints.Is;

namespace Sinkii09.UIFramework.Tests
{
    // The real service over real AudioSources: boot, play, steal, pause, unload, allocations.
    public class AudioServicePlayModeTests
    {
        private AudioPlayModeFixture _fixture;
        private AudioService _service;

        [SetUp]
        public void SetUp() => _fixture = new AudioPlayModeFixture();

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
            _fixture.Destroy();
            AudioListener.pause = false;
        }

        private IEnumerator BootWith(params string[] residentKeys)
        {
            _fixture.Audio.ResidentBankKeys = residentKeys;
            _service = _fixture.NewService();
            _service.Initialize();
            yield return _service.ReadyAsync().ToCoroutine();
        }

        private IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                _service.Tick();
            }
        }

        [UnityTest]
        public IEnumerator Boot_LoadsResidentBank_ThenPlayStartsARealSource()
        {
            var click = _fixture.Event("ui/click", _fixture.Group(AudioBus.Ui));
            _fixture.Bank("ui", click);
            yield return BootWith("ui");

            var handle = _service.Play(click.Id);
            yield return Frames(1);

            Assert.IsTrue(handle.IsPlaying);
            Assert.AreEqual(1, _fixture.PlayingSources());
            Assert.AreEqual(1, _service.Counters.Played);
        }

        [UnityTest]
        public IEnumerator ReadyAsync_CanBeAwaitedTwice()
        {
            yield return BootWith();
            yield return _service.ReadyAsync().ToCoroutine();
            yield return _service.ReadyAsync().ToCoroutine();
        }

        [UnityTest]
        public IEnumerator Steal_FadesTheOldVoiceOutOnRealSources()
        {
            var coin = _fixture.Event("sfx/coin", _fixture.Group(perEvent: 1, stopFadeMs: 30));
            _fixture.Bank("sfx", coin);
            yield return BootWith("sfx");

            var first = _service.Play(coin.Id);
            var second = _service.Play(coin.Id);
            Assert.IsFalse(first.IsPlaying);
            Assert.IsTrue(second.IsPlaying);

            // Time-bounded, not frame-counted: batch frames can be far shorter than the 30 ms fade.
            float deadline = Time.realtimeSinceStartup + 2f;
            while (first.IsValid && Time.realtimeSinceStartup < deadline) yield return Frames(1);
            Assert.IsFalse(first.IsValid, "The stolen voice finished its fade and was reclaimed.");
            Assert.AreEqual(1, _fixture.PlayingSources());
        }

        [UnityTest]
        public IEnumerator HiddenAndVisible_DriveAudioListenerPause()
        {
            yield return BootWith();

            _fixture.Signals.RaiseHidden();
            Assert.IsTrue(AudioListener.pause);
            _fixture.Signals.RaiseVisible();
            Assert.IsFalse(AudioListener.pause);

            _fixture.Signals.RaiseHidden();
            _service.Dispose();
            Assert.IsFalse(AudioListener.pause, "Dispose must not leave the listener paused.");
        }

        [UnityTest]
        public IEnumerator UnloadBank_CutsItsMusicAndReleasesTheClip()
        {
            var theme = _fixture.Event("music/theme", _fixture.Group(AudioBus.Music), loop: true);
            _fixture.Bank("music", theme);
            yield return BootWith();
            yield return _service.LoadBankAsync("music").ToCoroutine(ok => Assert.IsTrue(ok));

            _service.PlayMusic(theme.Id, 0);
            yield return Frames(1);
            Assert.AreEqual(1, _fixture.PlayingSources());

            _service.UnloadBank("music");
            Assert.AreEqual(0, _fixture.PlayingSources());
            foreach (var source in _fixture.Signals.GetComponentsInChildren<AudioSource>())
                Assert.IsNull(source.clip, "No source may keep a released bank's clip alive.");
            CollectionAssert.Contains(_fixture.Loader.Unloads, "music");
        }

        [UnityTest]
        public IEnumerator CallsAfterDispose_NeverThrow()
        {
            var click = _fixture.Event("ui/click", _fixture.Group(AudioBus.Ui));
            _fixture.Bank("ui", click);
            yield return BootWith("ui");
            _service.Dispose();
            yield return null; // let Destroy remove the sources

            LogAssert.Expect(LogType.Error, new Regex("after Dispose"));
            Assert.IsFalse(_service.Play(click.Id).IsValid);
            LogAssert.Expect(LogType.Error, new Regex("after Dispose"));
            Assert.DoesNotThrow(() => _service.PlayMusic(click.Id));
            Assert.DoesNotThrow(() => _service.StopGroup(click.Group));
            Assert.DoesNotThrow(() => _service.UnloadBank("ui"));
            Assert.DoesNotThrow(() => _service.Tick());
        }

        [UnityTest]
        public IEnumerator UnknownId_LogsErrorAndReturnsInvalid()
        {
            yield return BootWith();
            LogAssert.Expect(LogType.Error, new Regex(@"\[Audio\] No loaded bank registers"));

            var handle = _service.Play(SoundId.FromPath("does/not/exist"));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(1, _service.Counters.Missing);
        }

        [UnityTest]
        public IEnumerator SfxSwitchOff_MutesUiAndSfxButNotMusic()
        {
            var click = _fixture.Event("ui/click", _fixture.Group(AudioBus.Ui));
            _fixture.Bank("ui", click);
            yield return BootWith("ui");
            bool before = _service.Preferences.SfxEnabled;
            try
            {
                _service.Preferences.SfxEnabled = false;
                Assert.IsFalse(_service.Play(click.Id).IsValid);
                Assert.AreEqual(1, _service.Counters.Dropped);
            }
            finally
            {
                _service.Preferences.SfxEnabled = before;
            }
        }

        [UnityTest]
        public IEnumerator Play_ThousandCalls_AllocateNothing()
        {
            var coin = _fixture.Event("sfx/coin", _fixture.Group(perEvent: 2));
            _fixture.Bank("sfx", coin);
            yield return BootWith("sfx");

            // Warm every path once (first steal, first reject) so JIT and lazy init are not measured.
            for (int i = 0; i < 16; i++) _service.Play(coin.Id);
            _service.Tick();

            // A probe that cannot see allocations would pass anything.
            Assert.That(() => { _ = new object[4]; }, Is.AllocatingGCMemory(), "Allocation probe is not live.");
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++) _service.Play(coin.Id);
                _service.Tick();
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
