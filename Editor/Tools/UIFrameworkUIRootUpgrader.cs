using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Editor
{
    // Brings an existing UIRoot up to date with the current UILayer set, and wires the layer
    // references the installer wizard never assigned.
    //
    // Why this exists as a separate command: the wizard's Step 7 early-returns when a UIRoot
    // prefab already exists, so editing its layer table only ever helps brand-new installs. Every
    // project installed before a layer was added would otherwise keep a null layer transform
    // forever — and UIRootLayerRefs.SetLayerInteractable returns *silently* on a null transform,
    // so that failure is invisible at runtime.
    //
    // Discovery is by UIFrameworkLifetimeScope, not by UIRootLayerRefs: the latter is a plain
    // [Serializable] class, not a component, so there is nothing to search prefabs for.
    public static class UIFrameworkUIRootUpgrader
    {
        // Single source of truth for the layer table. Keep in sync with UILayer's declaration
        // order; the sortingOrder gaps leave room to insert without renumbering.
        private static readonly (string Name, int Order)[] LayerSpecs =
        {
            ("HUD", 0), ("Screen", 100), ("Popup", 200),
            ("Tooltip", 250), ("Notification", 275), ("Overlay", 300), ("Debug", 400),
        };

        [MenuItem("Tools/UIFramework/Upgrade UIRoot Layers")]
        public static void UpgradeAll()
        {
            var report = new List<string>();
            UpgradePrefabs(report);
            UpgradeOpenScenes(report);

            Debug.Log(report.Count == 0
                ? "[UIFramework] Upgrade UIRoot Layers: no UIRoot found. Nothing changed."
                : "[UIFramework] Upgrade UIRoot Layers:\n  " + string.Join("\n  ", report));
        }

        private static void UpgradePrefabs(List<string> report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentInChildren<UIFrameworkLifetimeScope>(true) == null)
                    continue;

                // Variants inherit their base's children. Patching both would give the variant a
                // second Tooltip child of its own.
                if (PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant)
                {
                    report.Add($"{path}: skipped (prefab variant — upgrade its base instead)");
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var scope = root.GetComponentInChildren<UIFrameworkLifetimeScope>(true);
                    if (scope == null) continue;

                    string changes = Upgrade(scope);
                    if (changes == null)
                    {
                        report.Add($"{path}: already up to date");
                        continue;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.Add($"{path}: {changes}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        // Nothing requires the scope to live in a prefab — a scene-embedded UIRoot is legal and
        // would otherwise be missed entirely.
        private static void UpgradeOpenScenes(List<string> report)
        {
            // sceneCount, not loadedSceneCount: GetSceneAt indexes the full open-scene list, and
            // loadedSceneCount (which excludes unloaded scenes) would silently skip the tail of it.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var rootObject in scene.GetRootGameObjects())
                {
                    var scope = rootObject.GetComponentInChildren<UIFrameworkLifetimeScope>(true);
                    if (scope == null || PrefabUtility.IsPartOfPrefabInstance(scope)) continue;

                    string changes = Upgrade(scope);
                    if (changes == null)
                    {
                        report.Add($"{scene.name} (scene): already up to date");
                        continue;
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    report.Add($"{scene.name} (scene): {changes} — save the scene to keep it");
                }
            }
        }

        // Returns a description of what changed, or null when nothing did.
        private static string Upgrade(UIFrameworkLifetimeScope scope)
        {
            var created = new List<string>();
            var wired = new List<string>();

            var serialized = new SerializedObject(scope);
            var layers = serialized.FindProperty("_layers");
            if (layers == null)
            {
                Debug.LogError($"[UIFramework] '{scope.name}' has no serialized _layers field.", scope);
                return null;
            }

            // Nothing requires the scope to sit on the same GameObject as the layers. Adopt the
            // parent of whichever layer is already wired, so a newly created one lands beside its
            // siblings — under the same Canvas — instead of under the scope, which could be
            // somewhere else entirely.
            var root = ExistingLayerParent(layers) ?? scope.transform;

            foreach (var (name, order) in LayerSpecs)
            {
                var field = layers.FindPropertyRelative(name);
                if (field == null) continue;

                // An already-wired layer is left completely alone. Checking the reference before
                // searching by name is what keeps this safe on a UIRoot whose scope is not on the
                // same GameObject as the layers: root.Find only sees direct children, so a
                // name-first approach would not find the existing layers and would happily build
                // a second, duplicate set beside them.
                if (field.objectReferenceValue != null) continue;

                var child = root.Find(name);
                if (child == null)
                {
                    child = CreateLayer(root, name, order);
                    created.Add(name);
                }

                field.objectReferenceValue = child;
                wired.Add(name);
            }

            if (created.Count == 0 && wired.Count == 0) return null;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scope);

            var parts = new List<string>();
            if (created.Count > 0) parts.Add("created " + string.Join(", ", created));
            if (wired.Count > 0) parts.Add("wired " + string.Join(", ", wired));
            return string.Join("; ", parts);
        }

        // Parent of the first already-wired layer, or null when none are wired yet.
        private static Transform ExistingLayerParent(SerializedProperty layers)
        {
            foreach (var (name, _) in LayerSpecs)
            {
                var field = layers.FindPropertyRelative(name);
                if (field?.objectReferenceValue is Transform existing && existing != null)
                    return existing.parent;
            }
            return null;
        }

        private static Transform CreateLayer(Transform root, string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;

            // Required even on a layer nothing raycasts: SetLayerInteractable logs a warning on
            // every single call when the layer canvas has no GraphicRaycaster.
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<CanvasGroup>();

            if (name == "Debug") go.SetActive(false);
            return go.transform;
        }
    }
}
