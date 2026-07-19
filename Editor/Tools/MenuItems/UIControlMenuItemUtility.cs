using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Editor
{
    // Shared helpers for the GameObject/UI/UIFramework/* quick-create menu items.
    public static class UIControlMenuItemUtility
    {
        public static GameObject GetOrCreateCanvas()
        {
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas != null)
                return canvas.gameObject;

            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
            }

            return canvasGO;
        }

        public static GameObject CreateRoot(string name, GameObject parent, Type mainComponent)
        {
            var go = new GameObject(name, typeof(RectTransform), mainComponent);
            GameObjectUtility.SetParentAndAlign(go, parent);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            return go;
        }

        public static GameObject CreateChild(string name, GameObject parent, bool stretch, params Type[] components)
        {
            var go = new GameObject(name, PrependRectTransform(components));
            GameObjectUtility.SetParentAndAlign(go, parent);

            if (stretch)
            {
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            return go;
        }

        // SerializeField refs on controls (icon/label/fill/button) are private with no editor-time
        // setter, so wiring them from a menu item has to go through SerializedObject.
        public static void SetPrivateField(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[UIFramework] Field '{fieldName}' not found on {target.GetType().Name} — " +
                    "the quick-create menu item is out of sync with the runtime script.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // UIControlBase.Awake() runs OnInitialize() immediately in the Editor. Badge's OnInitialize
        // deactivates its own GameObject when Count defaults to 0 — force it back on so newly
        // created controls aren't silently inactive in the Hierarchy.
        public static void Finish(GameObject root)
        {
            root.SetActive(true);
            Undo.RegisterCreatedObjectUndo(root, $"Create {root.name}");
            Selection.activeGameObject = root;
        }

        private static Type[] PrependRectTransform(Type[] components)
        {
            var result = new Type[components.Length + 1];
            result[0] = typeof(RectTransform);
            Array.Copy(components, 0, result, 1, components.Length);
            return result;
        }
    }
}
