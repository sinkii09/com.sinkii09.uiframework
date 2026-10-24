using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    // Builds an overlay canvas + TapEffectSpawner + inactive effect template, shared by the tap effect
    // tests. The canvas starts INACTIVE so private fields are set before Awake runs; call Activate().
    internal sealed class TapEffectTestRig
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        public readonly Canvas Canvas;
        public readonly TapEffectSpawner Spawner;
        public readonly GameObject Template;

        public TapEffectTestRig(GameObjectTracker gos, int maxEffects, bool withRipple, bool assignEffect = true)
        {
            var root = gos.Track(new GameObject("TapCanvas", typeof(RectTransform)));
            root.SetActive(false);
            Canvas = root.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Corner anchors on purpose: with centred anchors anchoredPosition == localPosition,
            // so a spawner that wrote the wrong one would still pass the position tests.
            Template = new GameObject("Effect", typeof(RectTransform));
            Template.transform.SetParent(root.transform, false);
            var rect = (RectTransform)Template.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            Template.SetActive(false);
            if (withRipple)
            {
                Template.AddComponent<CanvasGroup>();
                Template.AddComponent<TapRipple>();
            }

            Spawner = root.AddComponent<TapEffectSpawner>();
            if (assignEffect) SetField(Spawner, "_effect", Template);
            SetField(Spawner, "_maxEffects", maxEffects);
        }

        public void Activate() => Canvas.gameObject.SetActive(true);

        public void SetRipple(string field, object value) => SetField(Template.GetComponent<TapRipple>(), field, value);

        // Spawned copies, in creation order (the template is child 0).
        public List<GameObject> Instances()
        {
            var result = new List<GameObject>();
            foreach (Transform child in Canvas.transform)
                if (child.gameObject != Template) result.Add(child.gameObject);
            return result;
        }

        // What the spawner should compute, derived independently: overlay canvas, centre pivot.
        public Vector2 ExpectedLocalPoint(Vector2 screenPoint)
            => (screenPoint - new Vector2(Screen.width, Screen.height) * 0.5f) / Canvas.scaleFactor;

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, Instance).SetValue(target, value);
    }
}
