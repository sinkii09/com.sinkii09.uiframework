#if UNITY_EDITOR
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Destruction is the one way a run can end that the component does not drive itself — OnDestroy is
    // skipped for never-activated objects, and continuations resumed during teardown may call PlayAsync
    // again. Every one of these must complete instead of hanging its awaiter.
    public class OneShotAnimatorDestroyTests
    {
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

        private readonly OneShotAnimatorTestRig _rig = new();

        [TearDown]
        public void TearDown() => _rig.DestroyAll();

        [UnityTest]
        public IEnumerator PlayAsync_DestroyedMidRun_Completes() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            UniTask run = subject.PlayAsync();

            UnityEngine.Object.Destroy(subject.gameObject);

            await run.Timeout(Bound);
        });

        [UnityTest]
        public IEnumerator PlayAsync_DeferredOnNeverActivatedObject_CompletesOnDestroy() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create(active: false);
            UniTask run = subject.PlayAsync();

            UnityEngine.Object.Destroy(subject.gameObject);

            await run.Timeout(Bound);
        });

        // Before the destroy watch polls, a Play on the destroyed object must not hit a native property:
        // that throw would skip completing the pending run.
        [Test]
        public void Play_AfterNeverActivatedObjectDestroyed_CompletesPendingRun()
        {
            var subject = _rig.Create(active: false);
            UniTask pending = subject.PlayAsync();

            UnityEngine.Object.DestroyImmediate(subject.gameObject);
            subject.Play();

            Assert.AreEqual(UniTaskStatus.Succeeded, pending.Status);
            Assert.IsFalse(subject.IsPlaying);
        }

        [UnityTest]
        public IEnumerator PlayAsync_FromContinuationOfDestroy_CompletesInsteadOfHanging() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();
            UniTask inner = default;
            bool chained = false;

            async UniTaskVoid ChainAsync()
            {
                await subject.PlayAsync();
                inner = subject.PlayAsync();
                chained = true;
            }

            ChainAsync().Forget();
            UnityEngine.Object.Destroy(subject.gameObject);
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsTrue(chained);
            await inner.Timeout(Bound);
            Assert.AreEqual(UniTaskStatus.Succeeded, subject.PlayAsync().Status, "destroyed instance completes at once");
        });

        // OnDestroy must flag the instance destroyed BEFORE completing its run: on an inactive object the
        // continuation's PlayAsync would otherwise go pending, with nothing left to ever end it.
        [UnityTest]
        public IEnumerator PlayAsync_FromContinuationOfOnDestroyOnInactiveObject_Completes() => UniTask.ToCoroutine(async () =>
        {
            var subject = _rig.Create();            // active once, so Awake ran and OnDestroy will run
            subject.gameObject.SetActive(false);
            UniTask inner = default;
            bool chained = false;

            async UniTaskVoid ChainAsync()
            {
                await subject.PlayAsync();          // pending: the object is inactive
                inner = subject.PlayAsync();
                chained = true;
            }

            ChainAsync().Forget();
            UnityEngine.Object.Destroy(subject.gameObject);
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.IsTrue(chained);
            await inner.Timeout(Bound);
        });
    }
}
#endif
