using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Sinkii09.UIFramework.Editor;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Drives UIFrameworkUIRootUpgrader.Upgrade end to end on in-memory UIRoots shaped like the two
    // real cases: a pre-v3.11.0 root (layers directly under the canvas, already wired) and a fresh
    // install (layers under the installer's SafeArea panel, nothing wired yet).
    public class UIRootSafeAreaRestructurerTests
    {
        private static readonly string[] LayerNames = { "HUD", "Screen", "Popup", "Tooltip", "Notification", "Overlay", "Debug" };

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private GameObject NewRoot(out UIFrameworkLifetimeScope scope)
        {
            // Inactive while building: edit mode never runs Awake anyway, but this also keeps the
            // scope's singleton guard out of the picture.
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            _created.Add(root);
            scope = root.AddComponent<UIFrameworkLifetimeScope>();
            return root;
        }

        private static Transform AddLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            return go.transform;
        }

        private static void Wire(UIFrameworkLifetimeScope scope, Dictionary<string, Transform> layers)
        {
            var so = new SerializedObject(scope);
            foreach (var pair in layers)
                so.FindProperty("_layers").FindPropertyRelative(pair.Key).objectReferenceValue = pair.Value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform WiredLayer(UIFrameworkLifetimeScope scope, string name)
            => (Transform)new SerializedObject(scope).FindProperty("_layers").FindPropertyRelative(name).objectReferenceValue;

        private Dictionary<string, Transform> LegacyRoot(out UIFrameworkLifetimeScope scope, out GameObject eventSystem)
        {
            var root = NewRoot(out scope);
            eventSystem = new GameObject("EventSystem");
            eventSystem.transform.SetParent(root.transform, false);
            var layers = new Dictionary<string, Transform>();
            foreach (var name in LayerNames) layers[name] = AddLayer(root.transform, name);
            Wire(scope, layers);
            return layers;
        }

        [Test]
        public void Upgrade_FullyWiredLegacyRoot_MovesLayersIntoSafeAreaAndReportsAChange()
        {
            // The early-return trap: nothing to create or wire, so only the restructure changes —
            // it must still be reported, or the prefab is never saved.
            var layers = LegacyRoot(out var scope, out var eventSystem);

            var report = UIFrameworkUIRootUpgrader.Upgrade(scope);

            Assert.IsNotNull(report);
            StringAssert.Contains("created SafeArea", report);
            var panel = scope.transform.Find("SafeArea");
            Assert.IsNotNull(panel);
            for (var i = 0; i < LayerNames.Length; i++)
            {
                Assert.AreSame(panel, layers[LayerNames[i]].parent, LayerNames[i]);
                Assert.AreEqual(i, layers[LayerNames[i]].GetSiblingIndex(), "sibling order kept");
                Assert.AreSame(layers[LayerNames[i]], WiredLayer(scope, LayerNames[i]), "reference kept");
            }
            Assert.AreSame(scope.transform, eventSystem.transform.parent, "non-layer children stay put");
        }

        [Test]
        public void Upgrade_LegacyRoot_AddsAndWiresUIRootSetup()
        {
            LegacyRoot(out var scope, out _);

            UIFrameworkUIRootUpgrader.Upgrade(scope);

            var setup = scope.GetComponent<UIRootSetup>();
            Assert.IsNotNull(setup);
            Assert.AreSame(scope.transform.Find("SafeArea"),
                new SerializedObject(setup).FindProperty("_safeAreaPanel").objectReferenceValue);
        }

        [Test]
        public void Upgrade_SecondRun_ChangesNothing()
        {
            LegacyRoot(out var scope, out _);
            UIFrameworkUIRootUpgrader.Upgrade(scope);

            Assert.IsNull(UIFrameworkUIRootUpgrader.Upgrade(scope));
            Assert.AreEqual(1, scope.GetComponents<UIRootSetup>().Length);
        }

        [Test]
        public void Upgrade_InstallerShapedRoot_WiresTheExistingLayersWithoutDuplicates()
        {
            // Fresh install: layers under SafeArea, none wired, UIRootSetup already pointing at the
            // panel. A root.Find on the canvas alone would miss all seven and build a second set.
            var root = NewRoot(out var scope);
            var panel = new GameObject("SafeArea", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            foreach (var name in LayerNames) AddLayer(panel.transform, name);
            var setup = new SerializedObject(root.AddComponent<UIRootSetup>());
            setup.FindProperty("_safeAreaPanel").objectReferenceValue = panelRect;
            setup.ApplyModifiedPropertiesWithoutUndo();

            UIFrameworkUIRootUpgrader.Upgrade(scope);

            Assert.AreEqual(LayerNames.Length, panel.transform.childCount, "no duplicate layers");
            Assert.AreEqual(1, root.transform.childCount, "nothing created beside the panel");
            foreach (var name in LayerNames)
                Assert.AreSame(panel.transform, WiredLayer(scope, name).parent, name);
        }

        [Test]
        public void Restructure_LayersUnderDifferentParents_RefusesWithoutMovingAnything()
        {
            var root = NewRoot(out var scope);
            var other = new GameObject("Other", typeof(RectTransform));
            other.transform.SetParent(root.transform, false);
            var layers = new Dictionary<string, Transform>
            {
                ["HUD"] = AddLayer(root.transform, "HUD"),
                ["Screen"] = AddLayer(other.transform, "Screen"),
            };
            Wire(scope, layers);

            LogAssert.Expect(LogType.Error, new Regex("restructure refused.*different parents"));

            var result = Apply(scope);

            Assert.IsNull(result, "a refusal is not a change, so the prefab is not re-saved");
            Assert.IsNull(root.transform.Find("SafeArea"));
            Assert.AreSame(root.transform, layers["HUD"].parent);
        }

        [Test]
        public void Restructure_NothingWired_Refuses()
        {
            NewRoot(out var scope);
            LogAssert.Expect(LogType.Error, new Regex("restructure refused.*no layer is wired"));

            Assert.IsNull(Apply(scope));
        }

        [Test]
        public void Restructure_LayersUnderUnassignedSafeAreaPanel_ReusesItAndAddsUIRootSetup()
        {
            // Installer ran before UIRootSetup compiled: panel exists, nothing points at it.
            var root = NewRoot(out var scope);
            var panel = NewPanel(root.transform, fullStretch: true);
            Wire(scope, new Dictionary<string, Transform> { ["HUD"] = AddLayer(panel, "HUD") });

            var result = Apply(scope);

            StringAssert.DoesNotContain("created", result, "the existing panel is reused");
            Assert.AreEqual(1, root.transform.childCount);
            Assert.AreSame(panel, new SerializedObject(root.GetComponent<UIRootSetup>())
                .FindProperty("_safeAreaPanel").objectReferenceValue);
        }

        [Test]
        public void Restructure_SafeAreaPanelNotFullStretch_Refuses()
        {
            var root = NewRoot(out var scope);
            var panel = NewPanel(root.transform, fullStretch: false);
            Wire(scope, new Dictionary<string, Transform> { ["HUD"] = AddLayer(panel, "HUD") });
            LogAssert.Expect(LogType.Error, new Regex("restructure refused.*not full-stretch"));

            Assert.IsNull(Apply(scope));
            Assert.IsNull(root.GetComponent<UIRootSetup>());
        }

        private static string Apply(UIFrameworkLifetimeScope scope)
            => UIRootSafeAreaRestructurer.Apply(scope, new SerializedObject(scope).FindProperty("_layers"));

        private static RectTransform NewPanel(Transform parent, bool fullStretch)
        {
            var panel = (RectTransform)new GameObject("SafeArea", typeof(RectTransform)).transform;
            panel.SetParent(parent, false);
            panel.anchorMin = fullStretch ? Vector2.zero : new Vector2(0.1f, 0.1f);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            return panel;
        }
    }
}
