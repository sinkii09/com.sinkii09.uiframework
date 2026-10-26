using NUnit.Framework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // The portrait aspect cap is pure Rect maths; the component wiring is covered in PlayMode.
    public class UIRootSetupAspectTests
    {
        private const float NineBySixteen = 0.5625f;
        private const float Tolerance = 1e-3f;

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, "x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, "y");
            Assert.AreEqual(expected.width, actual.width, Tolerance, "width");
            Assert.AreEqual(expected.height, actual.height, Tolerance, "height");
        }

        [Test]
        public void CapAspect_Off_ReturnsAreaUnchanged()
        {
            var area = new Rect(0, 0, 1920, 1080);
            AssertRect(area, UIRootSetup.CapAspect(area, 0f));
        }

        [Test]
        public void CapAspect_PortraitPhone_ReturnsAreaUnchanged()
        {
            var area = new Rect(0, 0, 1080, 2400);
            AssertRect(area, UIRootSetup.CapAspect(area, NineBySixteen));
        }

        [Test]
        public void CapAspect_ExactlyTheCap_ReturnsAreaUnchanged()
        {
            var area = new Rect(0, 0, 1080, 1920);
            AssertRect(area, UIRootSetup.CapAspect(area, NineBySixteen));
        }

        [Test]
        public void CapAspect_Desktop16By9_CentresAPortraitColumn()
        {
            var capped = UIRootSetup.CapAspect(new Rect(0, 0, 1920, 1080), NineBySixteen);

            AssertRect(new Rect((1920 - 607.5f) / 2f, 0, 607.5f, 1080), capped);
        }

        [Test]
        public void CapAspect_Ultrawide_CentresAPortraitColumn()
        {
            var capped = UIRootSetup.CapAspect(new Rect(0, 0, 3440, 1440), NineBySixteen);

            AssertRect(new Rect((3440 - 810f) / 2f, 0, 810f, 1440), capped);
        }

        [Test]
        public void CapAspect_LandscapeWithNotchInset_StaysInsideTheSafeArea()
        {
            // A landscape phone: safe area starts 100 px in from the left edge.
            var safe = new Rect(100, 0, 2200, 1080);

            var capped = UIRootSetup.CapAspect(safe, NineBySixteen);

            Assert.GreaterOrEqual(capped.xMin, safe.xMin);
            Assert.LessOrEqual(capped.xMax, safe.xMax + Tolerance);
            Assert.AreEqual(safe.center.x, capped.center.x, Tolerance, "centred in the safe area, not the screen");
        }

        [Test]
        public void CapAspect_ZeroHeight_ReturnsAreaUnchanged()
        {
            var area = new Rect(0, 0, 800, 0);
            AssertRect(area, UIRootSetup.CapAspect(area, NineBySixteen));
        }
    }
}
