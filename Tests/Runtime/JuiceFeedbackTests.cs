using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // PressScaleFeedback + PunchFeedback. Every target is authored at a NON-unit scale so a restore
    // to a hardcoded Vector3.one (instead of the captured base) fails visibly.
    public class JuiceFeedbackTests
    {
        private static readonly Vector3 Authored = new Vector3(1.3f, 1.3f, 1f);
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            _gos.DestroyAll();
        }

        private PressScaleFeedback CreatePress(bool interactable = true)
        {
            var go = _gos.Track(new GameObject("Press", typeof(RectTransform)));
            go.transform.localScale = Authored;
            go.AddComponent<Image>();
            go.AddComponent<Button>().interactable = interactable;
            return go.AddComponent<PressScaleFeedback>();
        }

        private PunchFeedback CreatePunch(out Image image)
        {
            var go = _gos.Track(new GameObject("Punch", typeof(RectTransform)));
            go.transform.localScale = Authored;
            image = go.AddComponent<Image>();
            image.color = Color.white;
            return go.AddComponent<PunchFeedback>();
        }

        private static PointerEventData Pointer(int id) => new PointerEventData(null) { pointerId = id };

        [UnityTest]
        public IEnumerator Press_DisabledMidSquash_RestoresAuthoredScale() => UniTask.ToCoroutine(async () =>
        {
            var press = CreatePress();
            press.OnPointerDown(Pointer(0));
            await WaitRealtime(0.03f);
            Assert.AreNotEqual(Authored, press.transform.localScale, "precondition: squash in flight");

            press.gameObject.SetActive(false);

            Assert.AreEqual(Authored, press.transform.localScale);
            Assert.IsFalse(press.IsPressed);
        });

        [UnityTest]
        public IEnumerator Press_ReleasedMidSquash_SettlesAtAuthoredScale() => UniTask.ToCoroutine(async () =>
        {
            var press = CreatePress();
            press.OnPointerDown(Pointer(0));
            await WaitRealtime(0.03f);
            press.OnPointerUp(Pointer(0));

            await WaitUntil(() => !press.IsAnimating);
            Assert.AreEqual(Authored, press.transform.localScale);
        });

        [Test]
        public void Press_HoverExitWithoutPress_DoesNothing()
        {
            var press = CreatePress();
            press.OnPointerExit(Pointer(0));

            Assert.IsFalse(press.IsAnimating);
            Assert.AreEqual(Authored, press.transform.localScale);
        }

        [Test]
        public void Press_Multitouch_StaysPressedUntilLastPointerLifts()
        {
            var press = CreatePress();
            press.OnPointerDown(Pointer(1));
            press.OnPointerDown(Pointer(2));

            press.OnPointerUp(Pointer(1));
            Assert.IsTrue(press.IsPressed);

            press.OnPointerUp(Pointer(2));
            Assert.IsFalse(press.IsPressed);
        }

        [Test]
        public void Press_NotInteractable_DoesNotAnimate()
        {
            var press = CreatePress(interactable: false);
            press.OnPointerDown(Pointer(0));

            Assert.IsFalse(press.IsPressed);
            Assert.IsFalse(press.IsAnimating);
        }

        [UnityTest]
        public IEnumerator Press_WhileTimeScaleZero_StillAnimates() => UniTask.ToCoroutine(async () =>
        {
            var press = CreatePress();
            Time.timeScale = 0f;

            press.OnPointerDown(Pointer(0));
            await WaitRealtime(0.04f);

            // A scaled-time tween would still sit exactly at the authored scale here.
            Assert.Less(press.transform.localScale.x, Authored.x);
        });

        [UnityTest]
        public IEnumerator Punch_TwoRapidPunches_EndAtCapturedBase() => UniTask.ToCoroutine(async () =>
        {
            var punch = CreatePunch(out _);
            punch.PunchScale();
            await WaitRealtime(0.05f);
            punch.PunchScale();

            await WaitUntil(() => !punch.IsAnimating);
            Assert.AreEqual(Authored, punch.transform.localScale);
        });

        [UnityTest]
        public IEnumerator Punch_FlashDuringPunch_DoesNotCutThePunch() => UniTask.ToCoroutine(async () =>
        {
            var punch = CreatePunch(out var image);
            punch.PunchScale();
            await WaitRealtime(0.05f);
            Assert.AreNotEqual(Authored, punch.transform.localScale, "precondition: punch in flight");

            punch.Flash(Color.red);

            // A shared kill-and-restore would have snapped the punch back to base right here.
            Assert.AreNotEqual(Authored, punch.transform.localScale);
            await WaitUntil(() => !punch.IsAnimating);
            Assert.AreEqual(Authored, punch.transform.localScale);
            Assert.AreEqual(Color.white, image.color);
        });

        [UnityTest]
        public IEnumerator Punch_FlashDuringFlash_KeepsOriginalBase() => UniTask.ToCoroutine(async () =>
        {
            var punch = CreatePunch(out var image);
            punch.Flash(Color.red);
            await WaitRealtime(0.03f);
            Assert.AreNotEqual(Color.white, image.color, "precondition: first flash in flight");

            punch.Flash(Color.red); // must not capture the blended mid-flash colour as the new base

            await WaitUntil(() => !punch.IsAnimating);
            Assert.AreEqual(Color.white, image.color);
        });

        [Test]
        public void Punch_CalledBeforeAwake_StillPlays()
        {
            var go = _gos.Track(new GameObject("Punch", typeof(RectTransform)));
            go.SetActive(false);
            var punch = go.AddComponent<PunchFeedback>();

            punch.PunchScale();

            Assert.IsTrue(punch.IsAnimating);
        }

        [UnityTest]
        public IEnumerator Punch_RecolouredBetweenFlashes_RestoresNewColour() => UniTask.ToCoroutine(async () =>
        {
            var punch = CreatePunch(out var image);
            punch.Flash(Color.red);
            await WaitUntil(() => !punch.IsAnimating);

            image.color = Color.blue;
            punch.Flash(Color.red);
            await WaitUntil(() => !punch.IsAnimating);

            Assert.AreEqual(Color.blue, image.color);
        });
    }
}
