using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Calls arriving outside the view's initialized lifetime: before its Awake (a list inside a panel
    /// that is still inactive when its data arrives), after it was destroyed (an awaited load finishing
    /// late), and a scroll asked for before there are items. They used to throw, or leave the content
    /// rect at its authored size for good.
    /// </summary>
    public class RecyclerViewLifetimeTests
    {
        private RecyclerViewHarness _harness;

        [TearDown]
        public void TearDown() => _harness?.Destroy();

        [UnityTest]
        public IEnumerator SetItemCountBeforeAwake_SizesTheContentOnActivation()
        {
            _harness = RecyclerViewHarness.Build(activate: false);
            _harness.View.SetItemCount(100);
            _harness.UseDefaultProvider();

            _harness.Activate();

            Assert.AreEqual(_harness.View.TotalSize, _harness.ContentSize, 0.5f,
                "the content kept its authored size, so the list cannot scroll to its end");

            yield return null;
            Assert.IsNotEmpty(_harness.View.ShownIndices);
        }

        [UnityTest]
        public IEnumerator ScrollToIndexBeforeAwake_IsAppliedOnceItCanBe()
        {
            _harness = RecyclerViewHarness.Build(activate: false);
            _harness.View.SetItemCount(1000);

            Assert.DoesNotThrow(() => _harness.View.ScrollToIndex(400));
            _harness.UseDefaultProvider();
            _harness.Activate();
            yield return null;

            CollectionAssert.Contains(_harness.View.ShownIndices, 400);
        }

        [Test]
        public void SetItemCountWithResetBeforeAwake_DoesNotThrow()
        {
            _harness = RecyclerViewHarness.Build(activate: false);

            Assert.DoesNotThrow(() => _harness.View.SetItemCount(100, resetPosition: true));
        }

        [UnityTest]
        public IEnumerator ALaterScrollToIndex_ReplacesTheParkedOne()
        {
            _harness = RecyclerViewHarness.Build(activate: false);
            _harness.View.SetItemCount(1000);
            _harness.View.ScrollToIndex(400);
            _harness.Activate();

            // Initialized, but no provider yet — so the parked request has not been applied.
            _harness.View.ScrollToIndex(700);
            _harness.UseDefaultProvider();
            yield return null;

            CollectionAssert.Contains(_harness.View.ShownIndices, 700);
            CollectionAssert.DoesNotContain(_harness.View.ShownIndices, 400,
                "a request parked before initialization overrode a newer one");
        }

        /// <summary>"Scroll, then set the count" — the order async code tends to produce.</summary>
        [Test]
        public void ScrollToIndexWhileEmpty_IsAppliedWhenItemsArrive()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();

            _harness.View.ScrollToIndex(400);
            _harness.View.SetItemCount(1000);

            CollectionAssert.Contains(_harness.View.ShownIndices, 400);
        }

        [Test]
        public void CallsAfterTheViewIsDestroyed_AreHarmless()
        {
            _harness = RecyclerViewHarness.Build();
            _harness.UseDefaultProvider();
            _harness.View.SetItemCount(100);
            RecyclerView view = _harness.View;

            Object.DestroyImmediate(_harness.Root);

            Assert.DoesNotThrow(() => view.SetCellProvider(_ => null), "a late provider woke a dead view");
            Assert.DoesNotThrow(() => view.SetItemCount(50));
            Assert.DoesNotThrow(() => view.RefreshAll());
            Assert.DoesNotThrow(() => view.ScrollToIndex(10));
        }

        [Test]
        public void AnEmptyPrefabSlot_IsRefusedAtInitialization()
        {
            _harness = RecyclerViewHarness.Build(activate: false);
            _harness.ReplacePrefabs(_harness.Prefab, null);

            LogAssert.Expect(LogType.Exception, new Regex("cell prefab slot 1 is empty"));
            _harness.Activate();
        }

        [Test]
        public void APrefabWithoutARectTransform_IsRefusedAtInitialization()
        {
            _harness = RecyclerViewHarness.Build(activate: false);
            var plain = new GameObject("PlainCellPrefab");
            plain.SetActive(false);
            _harness.ReplacePrefabs(plain.AddComponent<TestCell>());

            LogAssert.Expect(LogType.Exception, new Regex("has no RectTransform"));
            _harness.Activate();

            Object.DestroyImmediate(plain);
        }
    }
}
