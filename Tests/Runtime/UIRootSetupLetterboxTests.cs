using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // UIRootSetup's wiring: anchors, the CanvasScaler match while letterboxed, and the injection
    // guards. The aspect maths itself is covered by the EditMode CapAspect table.
    public class UIRootSetupLetterboxTests
    {
        private const float NineBySixteen = 0.5625f;

        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private sealed class StubSafeArea : ISafeAreaProvider
        {
            private readonly ReactiveProperty<Rect> _area = new(new Rect(0, 0, Screen.width, Screen.height));
            public Rect SafeArea => _area.Value;
            public Observable<Rect> OnChanged => _area;
        }

        private static void Set(object target, string field, object value)
            => typeof(UIRootSetup).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private UIRootSetup Create(float maxAspect, out RectTransform panel, out CanvasScaler scaler, bool inject = true)
        {
            var root = _gos.Track(new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas)));
            root.SetActive(false);   // so Start waits until the fields are set
            scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.matchWidthOrHeight = 0.5f;
            panel = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);

            var setup = root.AddComponent<UIRootSetup>();
            Set(setup, "_safeAreaPanel", panel);
            Set(setup, "_maxAspect", maxAspect);
            if (inject) Set(setup, "_safeArea", new StubSafeArea());
            root.SetActive(true);
            return setup;
        }

        [UnityTest]
        public IEnumerator Apply_WideScreenWithCap_CentresColumnAndMatchesHeight()
        {
            var setup = Create(NineBySixteen, out var panel, out var scaler);
            yield return null;   // Start

            setup.Apply(new Rect(0, 0, 1920, 1080), new Vector2(1920, 1080));

            Assert.IsTrue(setup.IsLetterboxed);
            Assert.AreEqual(1f, scaler.matchWidthOrHeight);
            Assert.AreEqual((1920 - 607.5f) / 2f / 1920f, panel.anchorMin.x, 1e-4f);
            Assert.AreEqual(1f - (1920 - 607.5f) / 2f / 1920f, panel.anchorMax.x, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Apply_BackToPortrait_RestoresTheOriginalMatch()
        {
            var setup = Create(NineBySixteen, out _, out var scaler);
            yield return null;
            setup.Apply(new Rect(0, 0, 1920, 1080), new Vector2(1920, 1080));

            setup.Apply(new Rect(0, 0, 1080, 1920), new Vector2(1080, 1920));

            Assert.IsFalse(setup.IsLetterboxed);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, "the value from before the first apply");
        }

        [UnityTest]
        public IEnumerator Apply_CapOff_AnchorsToTheSafeAreaAndLeavesMatchAlone()
        {
            var setup = Create(0f, out var panel, out var scaler);
            yield return null;

            setup.Apply(new Rect(100, 50, 1720, 980), new Vector2(1920, 1080));

            Assert.IsFalse(setup.IsLetterboxed);
            Assert.AreEqual(0.5f, scaler.matchWidthOrHeight);
            Assert.AreEqual(100f / 1920f, panel.anchorMin.x, 1e-4f);
            Assert.AreEqual(1820f / 1920f, panel.anchorMax.x, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Apply_ZeroSizeScreen_IsSkipped()
        {
            var setup = Create(NineBySixteen, out var panel, out _);
            yield return null;
            var before = (panel.anchorMin, panel.anchorMax);

            setup.Apply(new Rect(0, 0, 0, 0), Vector2.zero);

            Assert.AreEqual(before, (panel.anchorMin, panel.anchorMax));
        }

        [UnityTest]
        public IEnumerator Start_NotInjected_LogsInsteadOfThrowing()
        {
            // A duplicate UIRoot is destroyed before its container builds, but its Start still runs.
            LogAssert.Expect(LogType.Error, new Regex(@"\[UIRootSetup\] ISafeAreaProvider was not injected"));

            Create(NineBySixteen, out _, out _, inject: false);
            yield return null;
        }
    }
}
