using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // The one-line fix behind UIRootSetup's old NullReferenceException trap: the scope injects its own
    // UIRootSetup through a build callback, so no autoInjectGameObjects entry is needed. Built on
    // a REAL UIFrameworkLifetimeScope, because a reflection-set field would prove nothing about
    // the callback.
    public class UIRootSetupInjectionTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [UnityTest]
        public IEnumerator Scope_WithEmptyAutoInjectList_InjectsItsUIRootSetup()
        {
            _root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas));
            _root.SetActive(false);
            var panel = new GameObject("SafeArea", typeof(RectTransform)).transform;
            panel.SetParent(_root.transform, false);
            var setup = _root.AddComponent<UIRootSetup>();
            typeof(UIRootSetup).GetField("_safeAreaPanel", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(setup, panel);
            var scope = _root.AddComponent<UIFrameworkLifetimeScope>();
            typeof(UIFrameworkLifetimeScope).GetField("_layers", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(scope, Layers(panel));

            // No SafeAreaProvider on the test root: the scope warns and falls back to the null
            // provider, which is still a real injection.
            _root.SetActive(true);   // scope Awake builds the container
            yield return null;       // UIRootSetup.Start

            var injected = typeof(UIRootSetup).GetField("_safeArea", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(setup);
            Assert.IsNotNull(injected, "the scope's build callback must inject UIRootSetup");
        }

        // The scope's entry points (navigator, view factory) cannot resolve without layer refs.
        private static UIRootLayerRefs Layers(Transform panel)
        {
            Transform Layer(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
                go.transform.SetParent(panel, false);
                return go.transform;
            }

            return new UIRootLayerRefs
            {
                HUD = Layer("HUD"), Screen = Layer("Screen"), Popup = Layer("Popup"), Tooltip = Layer("Tooltip"),
                Notification = Layer("Notification"), Overlay = Layer("Overlay"), Debug = Layer("Debug"),
            };
        }
    }
}
