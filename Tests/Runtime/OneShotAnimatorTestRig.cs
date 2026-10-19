#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    // Builds OneShotAnimator subjects on an in-memory Mecanim controller. Editor-only: controllers and
    // clips can only be authored in code through UnityEditor.Animations.
    // States (all animate the subject's own localScale.x over ClipLength):
    //   "Play"  1 -> 2      "Other" 3 -> 4      "Loop" 1 -> 2, looping
    //   "Long"  5 -> 6 over LongClipLength — for tests that must not finish by running out of clip
    internal sealed class OneShotAnimatorTestRig
    {
        internal const float ClipLength = 0.1f;
        internal const float LongClipLength = 10f;

        private readonly GameObjectTracker _gos = new();
        private readonly List<Object> _assets = new();

        internal OneShotAnimator Create(
            bool active = true,
            bool withController = true,
            string state = null,
            bool? playOnEnable = null,
            bool? unscaledTime = null,
            AnimatorUpdateMode authoredUpdateMode = AnimatorUpdateMode.Normal)
        {
            var go = _gos.Track(new GameObject("OneShot"));
            go.SetActive(false);   // Awake waits for the field overrides below

            var animator = go.AddComponent<Animator>();
            animator.updateMode = authoredUpdateMode;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;   // Awake must override this
            if (withController) animator.runtimeAnimatorController = CreateController();

            var subject = go.AddComponent<OneShotAnimator>();
            var so = new SerializedObject(subject);
            if (state != null) so.FindProperty("_state").stringValue = state;
            if (playOnEnable.HasValue) so.FindProperty("_playOnEnable").boolValue = playOnEnable.Value;
            if (unscaledTime.HasValue) so.FindProperty("_unscaledTime").boolValue = unscaledTime.Value;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (active) go.SetActive(true);
            return subject;
        }

        internal void DestroyAll()
        {
            _gos.DestroyAll();
            foreach (var asset in _assets)
                if (asset != null) Object.Destroy(asset);
            _assets.Clear();
        }

        internal static float ScaleX(Component subject) => subject.transform.localScale.x;

        private AnimatorController CreateController()
        {
            var controller = Track(new AnimatorController { name = "OneShotTestController" });
            controller.AddLayer("Base Layer");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AddState(machine, "Play", 1f, 2f, false);
            AddState(machine, "Other", 3f, 4f, false);
            AddState(machine, "Loop", 1f, 2f, true);
            AddState(machine, "Long", 5f, 6f, false, LongClipLength);
            return controller;
        }

        private void AddState(AnimatorStateMachine machine, string name, float from, float to, bool loop,
            float length = ClipLength)
        {
            var clip = Track(new AnimationClip { name = name });
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), "m_LocalScale.x"),
                AnimationCurve.Linear(0f, from, length, to));
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            machine.AddState(name).motion = clip;
        }

        private T Track<T>(T asset) where T : Object
        {
            _assets.Add(asset);
            return asset;
        }
    }
}
#endif
