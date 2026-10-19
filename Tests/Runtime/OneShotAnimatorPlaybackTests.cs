#if UNITY_EDITOR
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.OneShotAnimatorTestRig;

namespace Sinkii09.UIFramework.Tests
{
    public class OneShotAnimatorPlaybackTests
    {
        private readonly OneShotAnimatorTestRig _rig = new();

        [TearDown]
        public void TearDown() => _rig.DestroyAll();

        [Test]
        public void Awake_AnimatorEnabledAndCulled_DisablesItAndForcesAlwaysAnimate()
        {
            var subject = _rig.Create();
            var animator = subject.GetComponent<Animator>();

            Assert.IsFalse(animator.enabled);
            Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, animator.cullingMode);
            Assert.IsFalse(subject.IsPlaying);
        }

        [UnityTest]
        public IEnumerator Play_RunsToEnd_DisablesAnimatorAtEndPose() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            var animator = subject.GetComponent<Animator>();

            subject.Play();
            Assert.IsTrue(subject.IsPlaying);
            Assert.IsTrue(animator.enabled);

            await WaitUntil(() => !subject.IsPlaying);
            Assert.IsFalse(animator.enabled);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
        });

        [UnityTest]
        public IEnumerator Play_MidRun_RestartsFromStart() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            subject.Play();
            await WaitUntil(() => ScaleX(subject) > 1.2f);

            subject.Play();

            Assert.AreEqual(1f, ScaleX(subject), 0.01f, "start pose applied in the same call");
            Assert.IsTrue(subject.IsPlaying);
        });

        [UnityTest]
        public IEnumerator Play_WhileInactive_PlaysWhenEnabled() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create(active: false);
            var animator = subject.GetComponent<Animator>();

            // No Animator assertion while pending: on a never-activated object Awake has not run, so the
            // component's enabled flag is still its default — and an inactive Animator cannot run anyway.
            subject.Play();
            Assert.IsTrue(subject.IsPlaying, "pending counts as playing");

            subject.gameObject.SetActive(true);
            Assert.IsTrue(animator.enabled, "the pending run starts on enable");
            Assert.AreEqual(1f, ScaleX(subject), 0.01f, "from the start pose");

            await WaitUntil(() => !subject.IsPlaying);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
        });

        [Test]
        public void Disable_MidRun_SnapsEndPoseOnReEnable()
        {
            var subject = _rig.Create();
            subject.Play();

            subject.gameObject.SetActive(false);
            Assert.IsFalse(subject.IsPlaying);

            subject.gameObject.SetActive(true);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        }

        [Test]
        public void Complete_MidRun_SnapsEndPoseAndStops()
        {
            var subject = _rig.Create();
            subject.Play();

            subject.Complete();

            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
        }

        [UnityTest]
        public IEnumerator AnimatorMovedToOtherState_TreatsRunAsFinished() => UniTask.ToCoroutine(async () =>
        {
            // "Long" (10 s) cannot finish within two frames, so only the state change can end this run.
            var subject = _rig.Create();
            var animator = subject.GetComponent<Animator>();
            subject.Play("Long");

            animator.Play("Other", 0, 0f);
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(animator.enabled);
        });

        [Test]
        public void Play_NoController_LogsErrorOnce()
        {
            var subject = _rig.Create(withController: false);
            LogAssert.Expect(LogType.Error, new Regex(@"\[OneShotAnimator\].*has no Animator Controller"));

            subject.Play();
            subject.Play();

            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        }

        [Test]
        public void Play_UnknownState_LogsErrorAndStaysOff()
        {
            var subject = _rig.Create();
            LogAssert.Expect(LogType.Error, new Regex("has no state 'Missing' on layer 0"));

            subject.Play("Missing");

            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        }

        [Test]
        public void UnscaledTime_TrueSetsUnscaled_FalseKeepsAuthoredMode()
        {
            var unscaled = _rig.Create(authoredUpdateMode: AnimatorUpdateMode.Fixed);
            var authored = _rig.Create(unscaledTime: false, authoredUpdateMode: AnimatorUpdateMode.Fixed);

            Assert.AreEqual(AnimatorUpdateMode.UnscaledTime, unscaled.GetComponent<Animator>().updateMode);
            Assert.AreEqual(AnimatorUpdateMode.Fixed, authored.GetComponent<Animator>().updateMode);
        }

        // Prefabs migrated from ShineRevealPlayer store `_playOnEnable`; the sample prefab sets `_state`.
        [Test]
        public void SerializedFieldNames_MatchMigratedPrefabs()
        {
            var so = new SerializedObject(_rig.Create());

            foreach (string field in new[] { "_state", "_layer", "_playOnEnable", "_unscaledTime" })
                Assert.IsNotNull(so.FindProperty(field), field);
        }
    }
}
#endif
