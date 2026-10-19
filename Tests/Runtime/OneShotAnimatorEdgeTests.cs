#if UNITY_EDITOR
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.OneShotAnimatorTestRig;

namespace Sinkii09.UIFramework.Tests
{
    // The run's own state, not the configured one, drives every later step — and runs end even when the
    // state loops or something else switches the Animator off.
    public class OneShotAnimatorEdgeTests
    {
        private readonly OneShotAnimatorTestRig _rig = new();

        [TearDown]
        public void TearDown() => _rig.DestroyAll();

        [Test]
        public void Disable_MidRunOfOtherState_SnapsThatStatesEndPose()
        {
            var subject = _rig.Create();
            subject.Play("Other");

            subject.gameObject.SetActive(false);
            subject.gameObject.SetActive(true);

            Assert.AreEqual(4f, ScaleX(subject), 0.01f);
        }

        [UnityTest]
        public IEnumerator Play_OtherStateWhileInactive_PlaysThatStateWhenEnabled() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create(active: false);
            subject.Play("Other");

            subject.gameObject.SetActive(true);
            await WaitUntil(() => !subject.IsPlaying);

            Assert.AreEqual(4f, ScaleX(subject), 0.01f);
        });

        [Test]
        public void Play_InvalidStateMidRun_TurnsAnimatorOff()
        {
            var subject = _rig.Create();
            subject.Play();
            LogAssert.Expect(LogType.Error, new Regex("has no state 'Missing'"));

            subject.Play("Missing");

            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        }

        [Test]
        public void Complete_WhilePending_SnapsEndPoseWhenEnabled()
        {
            var subject = _rig.Create(active: false);
            subject.Play();

            subject.Complete();
            Assert.IsFalse(subject.IsPlaying);

            subject.gameObject.SetActive(true);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        }

        [UnityTest]
        public IEnumerator Play_LoopingState_StopsAfterFirstCycle() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            subject.Play("Loop");

            await WaitUntil(() => !subject.IsPlaying);

            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        });

        [UnityTest]
        public IEnumerator AnimatorDisabledExternally_EndsRunAndStaysOff() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            var animator = subject.GetComponent<Animator>();
            subject.Play();

            animator.enabled = false;
            await UniTask.Yield();

            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(animator.enabled);
        });

        [UnityTest]
        public IEnumerator PlayOnEnable_Activated_Plays() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create(active: false, playOnEnable: true);

            subject.gameObject.SetActive(true);
            Assert.IsTrue(subject.IsPlaying);

            await WaitUntil(() => !subject.IsPlaying);
            Assert.AreEqual(2f, ScaleX(subject), 0.01f);
        });
    }
}
#endif
