using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // RollingNumberLabel's double (idle-notation) path, and its hand-off with the long path.
    public class JuiceRollDoubleTests
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
        public void SetValue_IntArgument_BindsToLongOverload()
        {
            var roller = CreateRoller(out var label);

            // The long path formats with "N0"; the double path would print "1.23K".
            roller.SetValue(1234);

            Assert.AreEqual(1234L.ToString("N0"), label.text);
        }

        [Test]
        public void SetValueDouble_FirstValue_IsInstantInIdleNotation()
        {
            var roller = CreateRoller(out var label);

            roller.SetValue(1_234_567d);

            Assert.IsFalse(roller.IsRolling);
            Assert.AreEqual("1.23M", label.text);
            Assert.AreEqual(1_234_567d, roller.ValueDouble);
        }

        [UnityTest]
        public IEnumerator SetValueDouble_Roll_LandsOnExactTarget() => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1e15d);
            roller.SetValue(3.7e16d);
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");

            await WaitUntil(() => !roller.IsRolling);

            Assert.AreEqual(3.7e16d, roller.DisplayedValueDouble);
            Assert.AreEqual("37.0aa", label.text);
        });

        [Test]
        public void ModeSwitch_LongToDoubleToLong_RewritesTheText()
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1500L);
            var longText = label.text;

            roller.SetValue(1500d);
            Assert.AreEqual("1.50K", label.text);
            Assert.IsFalse(roller.IsRolling, "a mode switch snaps instead of rolling");

            roller.SetValue(1500L);
            Assert.AreEqual(longText, label.text);
        }

        // A ratio-based duration would be log10(0) or log10(negative) here: -Infinity or NaN. A tween
        // with such a duration still reports IsRolling, so the proof is that the roll FINISHES in time.
        [UnityTest]
        public IEnumerator SetValueDouble_RollToZero_FinishesOnTarget() =>
            RollFinishesOnTarget(0d, "0");

        [UnityTest]
        public IEnumerator SetValueDouble_RollToNegative_FinishesOnTarget() =>
            RollFinishesOnTarget(-250d, "-250");

        private IEnumerator RollFinishesOnTarget(double target, string expectedText) => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(5000d);
            roller.SetValue(target);
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");

            await WaitUntil(() => !roller.IsRolling, timeoutSeconds: 2f);

            Assert.AreEqual(target, roller.DisplayedValueDouble);
            Assert.AreEqual(expectedText, label.text);
        });

        [Test]
        public void SetValueDouble_ChangeWithinSameVisibleText_DoesNotRewriteLabel()
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1234d);
            // A sentinel only a real write could replace: 1239 prints "1.23K" too, so the dedupe
            // must skip SetText entirely.
            label.text = "sentinel";

            roller.SetValue(1239d, animate: false);

            Assert.AreEqual("sentinel", label.text);
            Assert.AreEqual(1239d, roller.DisplayedValueDouble);
        }

        [UnityTest]
        public IEnumerator SetValueDouble_DisabledMidRoll_SnapsToTarget() => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1e3d);
            roller.SetValue(1e9d);
            await WaitRealtime(0.05f);
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");

            roller.gameObject.SetActive(false);

            Assert.IsFalse(roller.IsRolling);
            Assert.AreEqual("1.00B", label.text);
        });

        [UnityTest]
        public IEnumerator ModeSwitch_DoubleToLongMidRoll_SnapsAndStaysLong() => UniTask.ToCoroutine(async () =>
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(1e3d);
            roller.SetValue(1e9d);
            await WaitRealtime(0.05f);
            Assert.IsTrue(roller.IsRolling, "precondition: roll in flight");

            roller.SetValue(42L);
            await WaitRealtime(0.05f);

            Assert.IsFalse(roller.IsRolling, "the double roll was killed, not left writing");
            Assert.AreEqual(42L.ToString("N0"), label.text);
        });

        [Test]
        public void SetValueDouble_NaNThenFinite_SnapsToFinite()
        {
            UIFormatUtils.ResetIdleNaNWarningForTests();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("FormatIdle received NaN"));
            var roller = CreateRoller(out var label);
            roller.SetValue(10d);

            roller.SetValue(double.NaN);
            roller.SetValue(5000d);

            Assert.IsFalse(roller.IsRolling, "no roll can start from NaN");
            Assert.AreEqual("5.00K", label.text);
        }

        [Test]
        public void SetValueDouble_Infinity_ShowsInstantly()
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(10d);

            roller.SetValue(double.PositiveInfinity);

            Assert.IsFalse(roller.IsRolling);
            Assert.AreEqual("∞", label.text);
            Assert.AreEqual(long.MaxValue, roller.Value, "saturated, not undefined");
        }

        [Test]
        public void SetValueDouble_SetWhileInactive_IsAppliedWithoutRolling()
        {
            var roller = CreateRoller(out var label);
            roller.SetValue(100d);
            roller.gameObject.SetActive(false);

            roller.SetValue(2e6d);
            roller.gameObject.SetActive(true);

            Assert.IsFalse(roller.IsRolling);
            Assert.AreEqual("2.00M", label.text);
        }
    }
}
