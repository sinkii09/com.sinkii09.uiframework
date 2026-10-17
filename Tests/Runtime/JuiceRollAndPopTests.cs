using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // RollingNumberLabel, PopTransition and JuiceSequences. Never names a DOTween type — the test
    // asmdef does not reference DOTween.dll.
    public class JuiceRollAndPopTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private RollingNumberLabel CreateRoller(out TMP_Text label)
        {
            var go = _gos.Track(new GameObject("Roller", typeof(RectTransform)));
            label = go.AddComponent<TextMeshProUGUI>();
            return go.AddComponent<RollingNumberLabel>();
        }

        [Test]
        public void Roll_FirstValue_IsInstant()
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1234);

            Assert.IsFalse(roller.IsRolling);
            Assert.AreEqual(1234L.ToString("N0"), label.text);
        }

        [UnityTest]
        public IEnumerator Roll_InterruptedMidRoll_LandsOnNewTarget() => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(0);
            roller.SetValue(100_000);
            await WaitRealtime(0.05f);
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");

            roller.SetValue(50);
            await WaitUntil(() => !roller.IsRolling);

            Assert.AreEqual(50L, roller.DisplayedValue);
            Assert.AreEqual(50L.ToString("N0"), label.text);
        });

        [UnityTest]
        public IEnumerator Roll_MidRoll_ShowsIntermediateValues() => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(0);
            roller.SetValue(10_000);
            await WaitRealtime(0.2f);

            // The whole point of the component: the number on screen passes through the middle.
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");
            Assert.Greater(roller.DisplayedValue, 0L, "displayed value never advanced");
            Assert.Less(roller.DisplayedValue, 10_000L, "jumped straight to the target");
            Assert.AreEqual(roller.DisplayedValue.ToString("N0"), label.text);
        });

        [UnityTest]
        public IEnumerator Roll_ValueAboveFloatPrecision_LandsExactly() => UniTask.ToCoroutine(async () =>
        {
            const long target = 16_777_217; // 2^24 + 1 — not representable as float
            var roller = CreateRoller(out var label);
            roller.SetValue(0);
            roller.SetValue(target);
            Assert.IsTrue(roller.IsRolling, "precondition: animated path");

            await WaitUntil(() => !roller.IsRolling);
            Assert.AreEqual(target, roller.DisplayedValue);
            Assert.AreEqual(target.ToString("N0"), label.text);
        });

        [Test]
        public void Roll_ValueSetWhileInactive_DoesNotConsumeFirstRoll()
        {
            var roller = CreateRoller(out _);
            roller.gameObject.SetActive(false);
            roller.SetValue(5); // what R3's Subscribe-time emission does to an inactive HUD

            roller.gameObject.SetActive(true);
            roller.SetValue(500);

            Assert.IsTrue(roller.IsRolling);
        }

        [Test]
        public void Roll_ValueSetBeforeAwake_IsWrittenToTheLabel()
        {
            // Inactive BEFORE AddComponent, so Awake has never run when SetValue arrives.
            var go = _gos.Track(new GameObject("Roller", typeof(RectTransform)));
            go.SetActive(false);
            var label = go.AddComponent<TextMeshProUGUI>();
            var roller = go.AddComponent<RollingNumberLabel>();

            roller.SetValue(42);

            Assert.AreEqual(42L.ToString("N0"), label.text);
        }

        [UnityTest]
        public IEnumerator Pop_ShowCancelled_RestoresRestingScale() => UniTask.ToCoroutine(async () =>
        {
            var view = new GameObject("PopView", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(view);
            var transition = ScriptableObject.CreateInstance<PopTransition>();
            transition.Duration = 5f;
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var showTask = animator.ShowAsync(view, transition, cts.Token);
            await UniTask.DelayFrame(2);
            // OutBack crosses 1.0 late in the tween; this early it must still be below both.
            Assert.Less(view.transform.localScale.x, 1f, "precondition: pop in flight");
            Assert.Less(view.CanvasGroup.alpha, 1f, "precondition: pop fades itself");
            cts.Cancel();

            var error = await CaptureAsync(async () => await showTask);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreEqual(Vector3.one, view.transform.localScale);
        });

        private Transform[] CreateItems(out GameObject owner, int count)
        {
            owner = _gos.Track(new GameObject("Owner", typeof(RectTransform)));
            var items = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new GameObject($"Item{i}", typeof(RectTransform)).transform;
                items[i].SetParent(owner.transform, false);
            }
            return items;
        }

        [UnityTest]
        public IEnumerator Stagger_ItemsStartInOrder_AndAllEndAtOne() => UniTask.ToCoroutine(async () =>
        {
            var items = CreateItems(out var owner, 3);
            JuiceSequences.PreparePopIn(items);

            var play = JuiceSequences.PlayStaggerPopInAsync(owner.transform, items, interval: 0.3f, duration: 0.1f);
            await WaitRealtime(0.05f);
            Assert.Greater(items[0].localScale.x, 0f);
            Assert.AreEqual(Vector3.zero, items[2].localScale, "last item must not have started yet");

            await play;
            foreach (var item in items) Assert.AreEqual(Vector3.one, item.localScale);
        });

        [UnityTest]
        public IEnumerator Stagger_Cancelled_RestoresItemsAndRethrows() => UniTask.ToCoroutine(async () =>
        {
            var items = CreateItems(out var owner, 3);
            using var cts = new CancellationTokenSource();

            var play = JuiceSequences.PlayStaggerPopInAsync(owner.transform, items, cts.Token, 0.5f, 2f);
            await UniTask.DelayFrame(2);
            Assert.AreEqual(Vector3.zero, items[2].localScale, "precondition: stagger in flight");
            cts.Cancel();

            var error = await CaptureAsync(async () => await play);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            foreach (var item in items) Assert.AreEqual(Vector3.one, item.localScale);
        });

        [UnityTest]
        public IEnumerator Stagger_OwnerDestroyed_EndsTheAwait() => UniTask.ToCoroutine(async () =>
        {
            var items = CreateItems(out var owner, 2);
            var play = JuiceSequences.PlayStaggerPopInAsync(owner.transform, items, default, 0.5f, 2f);

            UnityEngine.Object.Destroy(owner);
            var error = await CaptureAsync(async () => await play.Timeout(TimeSpan.FromSeconds(2)));

            Assert.IsInstanceOf<OperationCanceledException>(error);
        });
    }
}
