using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // TapEffectSpawner.Play + TapRipple. Input itself is covered by TapEffectInputTests.
    public class TapEffectTests
    {
        private const float Tolerance = 0.01f;
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            _gos.DestroyAll();
        }

        private TapEffectTestRig CreateActiveRig(int maxEffects, bool withRipple)
        {
            var rig = new TapEffectTestRig(_gos, maxEffects, withRipple);
            rig.Activate();
            return rig;
        }

        private static void AssertNear(Vector2 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator Play_ScreenPoints_PlacesEffectAtMatchingCanvasPoint() => UniTask.ToCoroutine(async () =>
        {
            var rig = CreateActiveRig(maxEffects: 2, withRipple: false);
            await UniTask.Yield(); // let the overlay canvas size its rect to the screen

            var centre = new Vector2(Screen.width, Screen.height) * 0.5f;
            rig.Spawner.Play(centre);
            AssertNear(Vector2.zero, rig.Instances()[0].transform.localPosition);

            var corner = new Vector2(10f, 20f);
            rig.Spawner.Play(corner);
            AssertNear(rig.ExpectedLocalPoint(corner), rig.Instances()[1].transform.localPosition);
        });

        [UnityTest]
        public IEnumerator Play_MoreTapsThanMaxEffects_ReusesOldestInstance() => UniTask.ToCoroutine(async () =>
        {
            var rig = CreateActiveRig(maxEffects: 2, withRipple: false);
            await UniTask.Yield();

            rig.Spawner.Play(new Vector2(10f, 10f));
            rig.Spawner.Play(new Vector2(20f, 20f));
            var third = new Vector2(30f, 30f);
            rig.Spawner.Play(third);

            var instances = rig.Instances();
            Assert.That(instances.Count, Is.EqualTo(2));
            AssertNear(rig.ExpectedLocalPoint(third), instances[0].transform.localPosition);
        });

        [UnityTest]
        public IEnumerator Play_EffectStillPlaying_RestartsFromStartPose() => UniTask.ToCoroutine(async () =>
        {
            var rig = new TapEffectTestRig(_gos, maxEffects: 1, withRipple: true);
            rig.SetRipple("_duration", 1f);
            rig.Activate();
            await UniTask.Yield();

            rig.Spawner.Play(Vector2.zero);
            var ripple = rig.Instances()[0];
            await WaitRealtime(0.4f);
            Assume.That(ripple.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f), "ripple should be mid-fade");

            rig.Spawner.Play(Vector2.zero);

            Assert.That(ripple.transform.localScale.x, Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(ripple.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
        });

        [UnityTest]
        public IEnumerator TapRipple_AfterDuration_SwitchesItselfOffWhilePaused() => UniTask.ToCoroutine(async () =>
        {
            var rig = new TapEffectTestRig(_gos, maxEffects: 1, withRipple: true);
            rig.SetRipple("_duration", 0.05f);
            rig.Activate();
            await UniTask.Yield();
            Time.timeScale = 0f; // proves the ripple runs in unscaled time

            rig.Spawner.Play(Vector2.zero);
            var ripple = rig.Instances()[0];
            Assert.That(ripple.activeSelf, Is.True);

            await WaitUntil(() => !ripple.activeSelf, timeoutSeconds: 2f);
        });

        [UnityTest]
        public IEnumerator TapRipple_ReplayedMidRipple_KeepsAuthoredBaseScale() => UniTask.ToCoroutine(async () =>
        {
            var rig = new TapEffectTestRig(_gos, maxEffects: 1, withRipple: true);
            rig.Template.transform.localScale = new Vector3(1.3f, 1.3f, 1f); // non-unit: a reset to one shows
            rig.SetRipple("_duration", 1f);
            rig.SetRipple("_endScale", 2f); // with end 1 a finished ripple sits at base and hides a re-capture
            rig.Activate();
            await UniTask.Yield();

            rig.Spawner.Play(Vector2.zero);
            await WaitRealtime(0.5f);
            rig.Spawner.Play(Vector2.zero);

            Assert.That(rig.Instances()[0].transform.localScale.x, Is.EqualTo(1.3f * 0.3f).Within(Tolerance));
        });

        [UnityTest]
        public IEnumerator Start_ActiveSceneTemplate_LogsError() => UniTask.ToCoroutine(async () =>
        {
            var rig = new TapEffectTestRig(_gos, maxEffects: 1, withRipple: false);
            rig.Template.SetActive(true);
            LogAssert.Expect(LogType.Error, new Regex(@"\[TapEffect\] The effect template is an active scene object"));
            rig.Activate();
            await UniTask.Yield();
        });

        [UnityTest]
        public IEnumerator Start_NoEffectAssigned_LogsError() => UniTask.ToCoroutine(async () =>
        {
            var rig = new TapEffectTestRig(_gos, maxEffects: 1, withRipple: false, assignEffect: false);
            LogAssert.Expect(LogType.Error, new Regex(@"\[TapEffect\] No effect assigned"));
            rig.Activate();
            await UniTask.Yield(); // Start runs before the next frame

            rig.Spawner.Play(Vector2.zero);
            Assert.That(rig.Instances(), Is.Empty);
        });
    }
}
