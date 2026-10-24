using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Sinkii09.UIFramework.Editor.UIControlMenuItemUtility;

namespace Sinkii09.UIFramework.Editor
{
    // GameObject/UI/UIFramework/Tap Effect — a working click/tap effect in one click:
    //   Tap Effect  (Canvas, sortingOrder 500, CanvasScaler, TapEffectSpawner)
    //   └─ Ripple   (inactive template: CanvasGroup, Image, TapRipple)
    public static class CreateTapEffectMenuItem
    {
        // Above every UIRoot layer (HUD 0 … Overlay 300, Debug 400 — see UIFrameworkUIRootUpgrader).
        private const int SortingOrder = 500;
        private const float RippleSize = 120f;
        // Unity's built-in circle (the same one uGUI's DefaultControls use) — ships in builds, no package art.
        private const string CircleSpritePath = "UI/Skin/Knob.psd";

        [MenuItem("GameObject/UI/UIFramework/Tap Effect", false, 30)]
        public static void CreateTapEffect()
        {
            // Created at scene root on purpose: under another Canvas it would lose its sorting order.
            // No GraphicRaycaster, so this canvas can never block a click.
            var root = new GameObject("Tap Effect", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TapEffectSpawner));

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // Keeps the ripple proportional on phones. Match these to your project's main CanvasScaler.
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var ripple = CreateChild("Ripple", root, false, typeof(CanvasGroup), typeof(Image), typeof(TapRipple));
            ((RectTransform)ripple.transform).sizeDelta = new Vector2(RippleSize, RippleSize);

            var image = ripple.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircleSpritePath);
            image.color = new Color(1f, 1f, 1f, 0.6f);
            image.raycastTarget = false;

            var group = ripple.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            ripple.SetActive(false); // template only — the spawner clones it
            SetPrivateField(root.GetComponent<TapEffectSpawner>(), "_effect", ripple);

            Undo.RegisterCreatedObjectUndo(root, "Create Tap Effect");
            Selection.activeGameObject = root;
        }
    }
}
