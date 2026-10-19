#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static Sinkii09.UIFramework.Tests.JuiceTestHelpers;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Editor-only: the wipe shader is found by name, which a player test build may have stripped.
    public class PatternWipeTransitionTests
    {
        private const string ShaderName = "UIFramework/UI/Pattern Wipe";
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

        private readonly GameObjectTracker _gos = new();
        private readonly List<UnityEngine.Object> _assets = new();

        [TearDown]
        public void TearDown()
        {
            _gos.DestroyAll();
            foreach (var asset in _assets)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            _assets.Clear();
        }

        [Test]
        public void CreateShowTween_Cover_WritesStartPoseBeforeFirstUpdate()
        {
            var view = CreateWipeView(out Image image, out _);
            Tween tween = CreateTransition(PatternWipeTransition.Mode.Cover, 5f).CreateShowTween(view);

            AssertPose(image, 0f, 0f);
            tween.Kill();
        }

        [Test]
        public void CreateHideTween_Reveal_WritesStartPoseBeforeFirstUpdate()
        {
            var view = CreateWipeView(out Image image, out _);
            Tween tween = CreateTransition(PatternWipeTransition.Mode.Reveal, 5f).CreateHideTween(view);

            AssertPose(image, 1f, 0f);
            tween.Kill();
        }

        // The animator snaps alpha back to 1 after a cancelled show OR hide, so the resting pose must be
        // the covered one in both modes; restoring Reveal's hidden end pose would leave an invisible curtain.
        [Test]
        public void RestoreOnCancel_BothModes_RestoresCoveredPose()
        {
            var view = CreateWipeView(out Image image, out _);

            CreateTransition(PatternWipeTransition.Mode.Cover, 1f).RestoreOnCancel(view);
            AssertPose(image, 1f, 0f);

            image.GetComponent<PatternWipeSurface>().SetPose(0.3f, 0.7f);
            CreateTransition(PatternWipeTransition.Mode.Reveal, 1f).RestoreOnCancel(view);
            AssertPose(image, 1f, 0f);
        }

        [UnityTest]
        public IEnumerator HideAsync_RevealCancelledMidTween_RestoresCoveredPose() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateWipeView(out Image image, out _);
            var animator = new DOTweenUIAnimator();
            using var cts = new CancellationTokenSource();

            var hideTask = animator.HideAsync(view, CreateTransition(PatternWipeTransition.Mode.Reveal, 5f), cts.Token);
            // Cancel only once the reveal has visibly moved: at reveal 0 the covered pose would match
            // without RestoreOnCancel ever running.
            await WaitUntil(() => image.material.GetFloat("_Reveal") > 0.02f);
            cts.Cancel();

            var error = await CaptureAsync(async () => await hideTask.Timeout(Bound));
            Assert.IsInstanceOf<OperationCanceledException>(error);
            AssertPose(image, 1f, 0f);
        });

        [UnityTest]
        public IEnumerator ShowThenHide_RunToEnd_ReachFinalPoses() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateWipeView(out Image image, out _);
            var animator = new DOTweenUIAnimator();

            await animator.ShowAsync(view, CreateTransition(PatternWipeTransition.Mode.Cover, 0.05f)).Timeout(Bound);
            AssertPose(image, 1f, 0f);

            await animator.HideAsync(view, CreateTransition(PatternWipeTransition.Mode.Reveal, 0.05f)).Timeout(Bound);
            AssertPose(image, 1f, 1f);
        });

        [Test]
        public void CreateTween_NoSurface_ReturnsEmptySequenceAndLogsError()
        {
            var view = _gos.Track(new GameObject("BareView", typeof(RectTransform)).AddComponent<TestView>());
            LogAssert.Expect(LogType.Error, new Regex("has no PatternWipeSurface"));

            Tween tween = CreateTransition(PatternWipeTransition.Mode.Cover, 1f).CreateShowTween(view);

            Assert.IsNotNull(tween, "a null tween would throw inside the animator's await");
            Assert.AreEqual(0f, tween.Duration());
            tween.Kill();
        }

        [Test]
        public void SetPose_WritesInstanceMaterial_SharedMaterialUntouched()
        {
            CreateWipeView(out Image image, out Material shared);

            Assert.AreNotSame(shared, image.material);
            Assert.AreEqual(0f, shared.GetFloat("_Cover"));
            AssertPose(image, 0.5f, 0.5f);
        }

        [Test]
        public void SetPose_ImageWithoutWipeMaterial_LogsErrorOnce()
        {
            var go = _gos.Track(new GameObject("PlainImage", typeof(RectTransform), typeof(Image)));
            LogAssert.Expect(LogType.Error, new Regex("needs a material using the UIFramework/UI/Pattern Wipe shader"));

            var surface = go.AddComponent<PatternWipeSurface>();   // Awake reports it
            surface.SetPose(1f, 0f);
            surface.SetPose(0f, 0f);

            LogAssert.NoUnexpectedReceived();
        }

        // TheEnd's PatternWipeReveal.asset stores `_mode: 1`; a renamed field would load it as Cover.
        [Test]
        public void SerializedModeField_KeepsItsName()
        {
            var transition = CreateTransition(PatternWipeTransition.Mode.Reveal, 1f);
            SerializedProperty mode = new SerializedObject(transition).FindProperty("_mode");

            Assert.IsNotNull(mode);
            Assert.AreEqual((int)PatternWipeTransition.Mode.Reveal, mode.enumValueIndex);
        }

        private TestView CreateWipeView(out Image image, out Material shared)
        {
            var view = _gos.Track(new GameObject("WipeView", typeof(RectTransform)).AddComponent<TestView>());
            var curtain = new GameObject("Curtain", typeof(RectTransform), typeof(Image));
            curtain.transform.SetParent(view.transform, false);
            image = curtain.GetComponent<Image>();

            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, $"shader '{ShaderName}' not found");
            shared = Track(new Material(shader));
            image.material = shared;   // before AddComponent: Awake runs inside it and copies this material

            curtain.AddComponent<PatternWipeSurface>().SetPose(0.5f, 0.5f);   // so a written start pose is observable
            return view;   // the surface destroys its own material copy
        }

        private PatternWipeTransition CreateTransition(PatternWipeTransition.Mode mode, float duration)
        {
            var transition = Track(ScriptableObject.CreateInstance<PatternWipeTransition>());
            transition.WipeMode = mode;
            transition.Duration = duration;
            return transition;
        }

        private T Track<T>(T asset) where T : UnityEngine.Object
        {
            _assets.Add(asset);
            return asset;
        }

        private static void AssertPose(Image image, float cover, float reveal)
        {
            Assert.AreEqual(cover, image.material.GetFloat("_Cover"), 1e-4f, "_Cover");
            Assert.AreEqual(reveal, image.material.GetFloat("_Reveal"), 1e-4f, "_Reveal");
        }
    }
}
#endif
