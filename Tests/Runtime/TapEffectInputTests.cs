using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // The <Pointer>/press binding, driven through the Input System's own test fixture. The spawner is
    // built inside each test body — after base.Setup() — because its InputAction is created in Awake;
    // created earlier it would bind to the real input system and the tests would be flaky.
    public class TapEffectInputTests : InputTestFixture
    {
        private const float Tolerance = 0.01f;
        private readonly GameObjectTracker _gos = new();

        public override void TearDown()
        {
            _gos.DestroyAll();
            base.TearDown();
        }

        private static void AssertNear(Vector2 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator MousePress_ShowsEffectAtMousePosition()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            var rig = new TapEffectTestRig(_gos, maxEffects: 2, withRipple: false);
            rig.Activate();
            yield return null; // canvas rect sized

            var point = new Vector2(100f, 50f);
            Set(mouse.position, point);
            Press(mouse.leftButton);
            // Observed (2026-10-24, Input System 1.19): in PlayMode the fixture's mouse press performs on
            // the next frame, while BeginTouch performs immediately. Asserting without this yield sees 0.
            yield return null;

            Assert.That(rig.Instances().Count, Is.EqualTo(1));
            AssertNear(rig.ExpectedLocalPoint(point), rig.Instances()[0].transform.localPosition);
        }

        [UnityTest]
        public IEnumerator TouchBegin_ShowsEffectAtTouchPosition()
        {
            InputSystem.AddDevice<Touchscreen>();
            var rig = new TapEffectTestRig(_gos, maxEffects: 2, withRipple: false);
            rig.Activate();
            yield return null;

            // Position and press arrive in one touch event — this proves the position is not one event stale.
            var point = new Vector2(120f, 80f);
            BeginTouch(1, point);

            Assert.That(rig.Instances().Count, Is.EqualTo(1));
            AssertNear(rig.ExpectedLocalPoint(point), rig.Instances()[0].transform.localPosition);
        }

        [UnityTest]
        public IEnumerator MousePressThenRelease_ShowsExactlyOneEffect()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            var rig = new TapEffectTestRig(_gos, maxEffects: 2, withRipple: false);
            rig.Activate();
            yield return null;

            Press(mouse.leftButton);
            yield return null; // mouse events are processed on the next frame (see above)
            Release(mouse.leftButton);
            yield return null;

            // A release-triggered or PassThrough action would have spawned a second instance.
            Assert.That(rig.Instances().Count, Is.EqualTo(1));
        }
    }
}
