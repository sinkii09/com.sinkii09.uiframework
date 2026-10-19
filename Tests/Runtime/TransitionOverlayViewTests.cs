using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Pins the overlay's pre-hide against both ways it can wake: with its scene (saved active) and
    // inside its own first ShowAsync (saved inactive). No container, so the animator is null and
    // every show is instant.
    public class TransitionOverlayViewTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [UnityTest]
        public IEnumerator ShowAsync_SavedInactive_StaysActiveAndShown() => UniTask.ToCoroutine(async () =>
        {
            // Regression: saved inactive, Awake ran inside ShowAsync's own SetActive(true) and its
            // pre-hide switched the overlay straight back off, so the first transition of every
            // session showed no curtain. Nothing was logged.
            _go = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasGroup));
            _go.SetActive(false);
            var overlay = _go.AddComponent<TransitionOverlayView>();

            await overlay.ShowAsync();

            Assert.IsTrue(_go.activeSelf, "The first show must leave the overlay active.");
            Assert.IsTrue(overlay.IsShown);
        });

        [Test]
        public void Awake_SavedActive_PreHides()
        {
            // The scene-load path: an overlay saved active must be hidden before its first frame.
            _go = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasGroup));
            var overlay = _go.AddComponent<TransitionOverlayView>();

            Assert.IsFalse(_go.activeSelf);
            Assert.AreEqual(0f, overlay.CanvasGroup.alpha);
            Assert.IsFalse(overlay.CanvasGroup.blocksRaycasts);
            Assert.IsFalse(overlay.IsShown);
        }
    }
}
