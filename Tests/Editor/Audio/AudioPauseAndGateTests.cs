using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class AudioPauseAndGateTests
    {
        private bool _applied;
        private int _applyCalls;
        private int _resumes;

        private PauseController NewPause(bool trackFocus)
        {
            _applied = true;
            _applyCalls = 0;
            _resumes = 0;
            var pause = new PauseController(p => { _applied = p; _applyCalls++; }, () => _resumes++, trackFocus);
            pause.Reset();
            return pause;
        }

        [Test]
        public void Reset_ClearsAndAppliesUnpaused()
        {
            NewPause(true);
            Assert.IsFalse(_applied);
            Assert.AreEqual(1, _applyCalls);
        }

        [Test]
        public void HiddenThenFocusRegained_StaysPaused()
        {
            var pause = NewPause(trackFocus: true);
            pause.OnFocus(false);
            pause.OnHidden();
            pause.OnFocus(true);

            Assert.IsTrue(pause.IsPaused, "Focus must never clear Hidden.");
            Assert.IsTrue(_applied);

            pause.OnVisible();
            Assert.IsFalse(pause.IsPaused);
            Assert.IsFalse(_applied);
            Assert.AreEqual(1, _resumes, "Becoming visible resumes the audio context.");
        }

        [Test]
        public void ApplicationPauseAndHidden_BothMustClear()
        {
            var pause = NewPause(trackFocus: false);
            pause.OnApplicationPause(true);
            pause.OnHidden();
            pause.OnApplicationPause(false);
            Assert.IsTrue(pause.IsPaused);
            pause.OnVisible();
            Assert.IsFalse(pause.IsPaused);
        }

        [Test]
        public void Focus_IgnoredWhenNotTracked()
        {
            var pause = NewPause(trackFocus: false);
            pause.OnFocus(false);
            Assert.IsFalse(pause.IsPaused, "Android keyboard and WebGL click-away raise focus(false).");
        }

        [Test]
        public void Apply_OnlyOnTransitions()
        {
            var pause = NewPause(trackFocus: true);
            pause.OnHidden();
            pause.OnHidden();
            pause.OnApplicationPause(true);
            Assert.AreEqual(2, _applyCalls, "Reset plus one transition to paused.");
        }

        [Test]
        public void Gate_SuspendedThenRunning_ReportsTransitionOnce()
        {
            int state = AudioContextGate.Suspended;
            var gate = new AudioContextGate(() => state);

            Assert.IsFalse(gate.Refresh());
            Assert.IsFalse(gate.IsRunning);
            state = AudioContextGate.Running;
            Assert.IsTrue(gate.Refresh());
            Assert.IsFalse(gate.Refresh());
        }

        [Test]
        public void Gate_UnknownOrThrowing_FailsOpen()
        {
            var unknown = new AudioContextGate(() => AudioContextGate.Unknown);
            unknown.Refresh();
            Assert.IsTrue(unknown.IsRunning);

            var throwing = new AudioContextGate(() => throw new InvalidOperationException("no context"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("assuming it runs"));
            throwing.Refresh();
            throwing.Refresh();
            Assert.IsTrue(throwing.IsRunning);
        }

        [Test]
        public void Gate_QueuedMusic_LatestWins_AndIsTakenOnlyWhileRunning()
        {
            int state = AudioContextGate.Suspended;
            var gate = new AudioContextGate(() => state);
            gate.Refresh();
            gate.QueueMusic(SoundId.FromPath("a"), 100);
            gate.QueueMusic(SoundId.FromPath("b"), 200);

            Assert.IsFalse(gate.TryTakeQueuedMusic(out _, out _));
            state = AudioContextGate.Running;
            gate.Refresh();
            Assert.IsTrue(gate.TryTakeQueuedMusic(out var id, out int fade));
            Assert.AreEqual(SoundId.FromPath("b"), id);
            Assert.AreEqual(200, fade);
            Assert.IsFalse(gate.TryTakeQueuedMusic(out _, out _), "Taken once.");
        }

        [Test]
        public void Preferences_DefaultOn_PersistAndCorruptReadsAsDefault()
        {
            const string prefix = "uifw.test.audio.";
            try
            {
                PlayerPrefs.SetString(prefix + "music", "garbage");
                PlayerPrefs.SetInt(prefix + "sfx", 7);
                var prefs = new AudioPreferences(prefix);
                Assert.IsTrue(prefs.MusicEnabled);
                Assert.IsTrue(prefs.SfxEnabled);

                int changes = 0;
                prefs.Changed += () => changes++;
                prefs.MusicEnabled = false;
                prefs.MusicEnabled = false;
                Assert.AreEqual(1, changes);
                Assert.IsFalse(new AudioPreferences(prefix).MusicEnabled, "Persisted across instances.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(prefix + "music");
                PlayerPrefs.DeleteKey(prefix + "sfx");
            }
        }
    }
}
