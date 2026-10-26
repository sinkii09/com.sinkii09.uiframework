using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Puts every UI layer under one full-stretch "SafeArea" panel driven by UIRootSetup, so the
    // device safe area and the optional portrait aspect cap apply to the whole UI. Run by
    // UIFrameworkUIRootUpgrader ("Tools/UIFramework/Upgrade UIRoot Layers") after layer wiring.
    //
    // UIRoots created before v3.11.0 have their layers directly under the root canvas and no
    // UIRootSetup; this moves them. Idempotent, and it refuses rather than guesses whenever the
    // hierarchy is not one of the two shapes it understands.
    internal static class UIRootSafeAreaRestructurer
    {
        internal const string PanelName = "SafeArea";

        // Where the upgrader should look for (and create) layers when none is wired yet: inside the
        // installer's SafeArea panel if there is one, else the scope's own transform.
        internal static Transform FindLayerRoot(UIFrameworkLifetimeScope scope)
            => scope.transform.Find(PanelName) ?? scope.transform;

        // Returns a description of what changed, or null when nothing did. A refusal also returns
        // null — it changed nothing, so the prefab must not be re-saved — and logs an error, so a
        // batch or CI run can see it.
        internal static string Apply(UIFrameworkLifetimeScope scope, SerializedProperty layers)
        {
            var existing = scope.GetComponentInChildren<UIRootSetup>(true);
            if (existing != null && SerializedPanel(existing).objectReferenceValue != null)
                return null;

            var wired = WiredLayers(layers);
            if (wired.Count == 0) return Refuse(scope, "no layer is wired, so there is nothing to move");

            var parent = wired[0].parent;
            foreach (var layer in wired)
                if (layer.parent != parent)
                    return Refuse(scope, "layers have different parents; move them under one canvas first");

            // Reuse branch: the installer already made the panel. Create branch: layers sit
            // directly under the root canvas.
            var reuse = parent.name == PanelName && parent.GetComponent<Canvas>() == null;
            var rootCanvas = reuse ? parent.parent : parent;
            if (rootCanvas == null || !IsRootCanvas(rootCanvas))
                return Refuse(scope, $"'{rootCanvas?.name}' is not a root canvas");
            if (reuse && !IsFullStretch((RectTransform)parent))
                return Refuse(scope, $"existing '{PanelName}' panel is not full-stretch; fix or remove it first");
            if (existing != null && !reuse)
                return Refuse(scope, "UIRootSetup exists with no panel and the layers are not under a SafeArea panel");
            if (rootCanvas.GetComponentInParent<UIFrameworkLifetimeScope>(true) != scope)
                return Refuse(scope, "the root canvas is outside the scope's hierarchy, so the scope could not inject UIRootSetup");

            var changes = new List<string>();
            var panel = reuse ? (RectTransform)parent : CreatePanel(rootCanvas, wired);
            if (!reuse) changes.Add($"created {PanelName} panel and moved {wired.Count} layers into it");

            var setup = existing != null ? existing : rootCanvas.gameObject.AddComponent<UIRootSetup>();
            if (existing == null) changes.Add("added UIRootSetup");

            var serialized = new SerializedObject(setup);
            serialized.FindProperty("_safeAreaPanel").objectReferenceValue = panel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(setup);
            changes.Add("wired UIRootSetup._safeAreaPanel");

            return string.Join("; ", changes);
        }

        // Every wired layer moves, including any the upgrader created this run; non-layer children
        // (EventSystem, game objects) stay where they are. Sibling order is kept, and the serialized
        // layer references survive because they are object references, not paths.
        private static RectTransform CreatePanel(Transform rootCanvas, List<Transform> wired)
        {
            var firstIndex = int.MaxValue;
            foreach (var layer in wired) firstIndex = Mathf.Min(firstIndex, layer.GetSiblingIndex());

            var go = new GameObject(PanelName, typeof(RectTransform));
            var panel = (RectTransform)go.transform;
            panel.SetParent(rootCanvas, false);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.SetSiblingIndex(firstIndex);

            wired.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
            foreach (var layer in wired) layer.SetParent(panel, false);
            return panel;
        }

        private static List<Transform> WiredLayers(SerializedProperty layers)
        {
            var result = new List<Transform>();
            var end = layers.GetEndProperty();
            var child = layers.Copy();
            if (!child.NextVisible(true)) return result;
            while (!SerializedProperty.EqualContents(child, end))
            {
                if (child.propertyType == SerializedPropertyType.ObjectReference &&
                    child.objectReferenceValue is Transform t && t != null)
                    result.Add(t);
                if (!child.NextVisible(false)) break;
            }
            return result;
        }

        private static string Refuse(UIFrameworkLifetimeScope scope, string reason)
        {
            Debug.LogError($"[UIFramework] '{scope.name}': SafeArea restructure refused, nothing changed — {reason}.", scope);
            return null;
        }

        private static SerializedProperty SerializedPanel(UIRootSetup setup)
            => new SerializedObject(setup).FindProperty("_safeAreaPanel");

        private static bool IsRootCanvas(Transform t)
            => t.GetComponent<Canvas>() != null && t.GetComponentsInParent<Canvas>(true).Length == 1;

        private static bool IsFullStretch(RectTransform rect)
            => rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one &&
               rect.offsetMin == Vector2.zero && rect.offsetMax == Vector2.zero;
    }
}
