#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Every way a run can end must complete its PlayAsync — a stranded awaiter hangs its caller forever,
    // so each await is bounded by a timeout that turns a strand into a failure.
    public class OneShotAnimatorAwaitTests
    {
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

        private readonly OneShotAnimatorTestRig _rig = new();

        [TearDown]
        public void TearDown() => _rig.DestroyAll();

        [UnityTest]
        public IEnumerator PlayAsync_RunFinishes_Completes() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();

            await subject.PlayAsync().Timeout(Bound);

            Assert.IsFalse(subject.IsPlaying);
            Assert.AreEqual(2f, OneShotAnimatorTestRig.ScaleX(subject), 0.01f);
        });

        [UnityTest]
        public IEnumerator PlayAsync_CalledTwice_FirstCompletesOnRestartSecondOnFinish() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();

            UniTask first = subject.PlayAsync();
            UniTask second = subject.PlayAsync();

            Assert.AreEqual(UniTaskStatus.Succeeded, first.Status, "restart ends the first run");
            Assert.AreEqual(UniTaskStatus.Pending, second.Status);
            await second.Timeout(Bound);
        });

        [UnityTest]
        public IEnumerator PlayAsync_DisabledMidRun_Completes() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            UniTask run = subject.PlayAsync();

            subject.gameObject.SetActive(false);

            await run.Timeout(Bound);
        });

        [UnityTest]
        public IEnumerator PlayAsync_TokenCancelled_ThrowsAndAnimationContinues() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            using var cts = new CancellationTokenSource();
            UniTask run = subject.PlayAsync(cts.Token);

            cts.Cancel();

            var error = await CaptureAsync(async () => await run.Timeout(Bound));
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsTrue(subject.IsPlaying, "cancelling the wait must not stop the animation");
            await WaitUntil(() => !subject.IsPlaying);
        });

        [UnityTest]
        public IEnumerator PlayAsync_PreCancelledToken_ThrowsWithoutPlaying() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var error = await CaptureAsync(async () => await subject.PlayAsync(cts.Token));

            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsFalse(subject.IsPlaying);
            Assert.IsFalse(subject.GetComponent<Animator>().enabled);
        });

        [Test]
        public void PlayAsync_InvalidState_CompletesImmediately()
        {
            var subject = _rig.Create(state: "Missing");
            LogAssert.Expect(LogType.Error, new Regex("has no state 'Missing'"));

            Assert.AreEqual(UniTaskStatus.Succeeded, subject.PlayAsync().Status);
        }

        // TrySetResult resumes awaiters synchronously, so the continuation's PlayAsync runs INSIDE the
        // restarting Play — the second run must still be the live one afterwards.
        [UnityTest]
        public IEnumerator PlayAsync_ContinuationCallsPlayAsync_SecondRunNotStranded() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            UniTask second = default;
            bool chained = false;

            async UniTaskVoid ChainAsync()
            {
                await subject.PlayAsync();
                second = subject.PlayAsync();
                chained = true;
            }

            ChainAsync().Forget();
            subject.Play();   // restart: ends the first run, whose continuation starts the second

            Assert.IsTrue(chained);
            Assert.IsTrue(subject.IsPlaying);
            await second.Timeout(Bound);
            Assert.IsFalse(subject.IsPlaying);
        });

        [Test]
        public void Complete_DuringPlayAsync_Completes()
        {
            var subject = _rig.Create();
            UniTask run = subject.PlayAsync();

            subject.Complete();

            Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
        }
    }
}
#endif
