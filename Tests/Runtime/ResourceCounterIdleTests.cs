using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    // The double readouts are additive: the int Current/Max text must stay byte-identical, and the
    // two kinds of setter must hand the label back and forth cleanly.
    public class ResourceCounterIdleTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private ResourceCounter CreateCounter(out TextMeshProUGUI label)
        {
            var go = _gos.Track(new GameObject("Counter", typeof(RectTransform)));
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform);
            label = labelGo.AddComponent<TextMeshProUGUI>();

            var counter = go.AddComponent<ResourceCounter>();
            typeof(ResourceCounter)
                .GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(counter, label);
            return counter;
        }

        [Test]
        public void IntProperties_CurrentSetBeforeMax_ShowsSameTextAsBefore()
        {
            var counter = CreateCounter(out var label);

            counter.Current = 7;
            Assert.AreEqual("0/0", label.text);
            counter.Max = 10;
            Assert.AreEqual("7/10", label.text);
            counter.Current = 12_000;
            Assert.AreEqual("10/10", label.text);
        }

        [Test]
        public void SetAmount_LargeValue_ShowsIdleNotationWithoutMax()
        {
            var counter = CreateCounter(out var label);

            counter.SetAmount(1_234_567d);

            Assert.AreEqual("1.23M", label.text);
        }

        [Test]
        public void SetValues_CurrentAboveMax_CapsCurrentOnlyWhenDisplayed()
        {
            var counter = CreateCounter(out var label);

            counter.SetValues(9000d, 5000d);

            Assert.AreEqual("5.00K/5.00K", label.text);
        }

        [Test]
        public void SetAmount_NegativeOrNaN_ShowsZero()
        {
            var counter = CreateCounter(out var label);

            counter.SetAmount(-50d);
            Assert.AreEqual("0", label.text);
            counter.SetAmount(double.NaN);
            Assert.AreEqual("0", label.text);
        }

        [Test]
        public void IntSetter_AfterIdleMode_SwitchesBackToIntText()
        {
            var counter = CreateCounter(out var label);
            counter.Current = 3;
            counter.Max = 8;

            counter.SetAmount(2_500d);
            Assert.AreEqual("2.50K", label.text, "precondition");

            counter.Max = 8;
            Assert.AreEqual("3/8", label.text);
        }

        [Test]
        public void SetAmount_DoesNotTouchIntValues()
        {
            var counter = CreateCounter(out _);
            counter.Current = 3;
            counter.Max = 8;

            counter.SetAmount(1e9d);

            Assert.AreEqual(3, counter.Current);
            Assert.AreEqual(8, counter.Max);
        }
    }
}
