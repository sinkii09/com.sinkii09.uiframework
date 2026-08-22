using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Builds a live <see cref="RecyclerView"/> over a real <see cref="ScrollRect"/>.
    ///
    /// <para><b>Why the GameObject is built inactive.</b> <c>UIControlBase.Awake</c> calls
    /// <c>OnInitialize</c>, and <c>AddComponent</c> runs <c>Awake</c> immediately — so a view whose
    /// serialized fields were set afterwards would already have initialized against the defaults,
    /// and one initialized before its ScrollRect had content would throw. Building inactive defers
    /// Awake until <see cref="Build"/> activates the object, which is the only window in which the
    /// serialized fields can be planted.</para>
    ///
    /// <para><c>movementType</c> is Unrestricted so the ScrollRect never applies elasticity of its
    /// own between frames — otherwise content drifts away from the position a test just set and the
    /// assertions turn flaky.</para>
    /// </summary>
    internal class RecyclerViewHarness
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        public RecyclerView View;
        public RectTransform Content;
        public RectTransform Viewport;
        public GameObject Root;
        public TestCell Prefab;

        /// <summary>Every cell template this harness created, prefab id order.</summary>
        public readonly List<TestCell> Prefabs = new();

        /// <summary>Data indices the provider was asked to bind, in order, across the whole test.</summary>
        public readonly List<int> BindCalls = new();

        /// <summary>Live cells that exist under the content root, however they were obtained.</summary>
        public int InstantiatedCells => Content.GetComponentsInChildren<TestCell>(true).Length;

        public static RecyclerViewHarness Build(
            float viewportSize = 500f,
            float cellSize = 100f,
            float spacing = 0f,
            ScrollDirection direction = ScrollDirection.TopToBottom,
            int prewarmCount = 0,
            int prefabCount = 1)
        {
            var harness = new RecyclerViewHarness();

            harness.Root = new GameObject("RecyclerViewRoot", typeof(RectTransform));
            harness.Root.SetActive(false);

            harness.Viewport = NewRect("Viewport", harness.Root.transform, viewportSize);
            harness.Content = NewRect("Content", harness.Viewport, viewportSize);

            var scroll = harness.Root.AddComponent<ScrollRect>();
            scroll.viewport = harness.Viewport;
            scroll.content = harness.Content;
            scroll.movementType = ScrollRect.MovementType.Unrestricted;
            scroll.inertia = false;

            // The "prefab" is just an inactive template Instantiate() can copy. More than one gives
            // the pool separate per-id tiers, which is the only way a recycled cell can arrive
            // carrying another index's size.
            var prefabs = new RecyclerCell[Mathf.Max(1, prefabCount)];
            for (int id = 0; id < prefabs.Length; id++)
            {
                var prefabGo = new GameObject($"CellPrefab{id}", typeof(RectTransform));
                prefabGo.SetActive(false);
                var cell = prefabGo.AddComponent<TestCell>();
                prefabs[id] = cell;
                harness.Prefabs.Add(cell);
            }
            harness.Prefab = harness.Prefabs[0];

            harness.View = harness.Root.AddComponent<RecyclerView>();
            SetField(harness.View, "_direction", direction);
            SetField(harness.View, "_cellPrefabs", prefabs);
            SetField(harness.View, "_settings", NewSettings(cellSize, spacing, prewarmCount));

            harness.Root.SetActive(true); // Awake -> OnInitialize
            return harness;
        }

        /// <summary>
        /// Real rendered size of the cell bound to a data index, along the scroll axis. Reads the
        /// rect rather than the view's bookkeeping, so it can catch a size the view believes it
        /// applied but never wrote.
        /// </summary>
        public float CellSizeOf(int index)
        {
            ScrollAxis axis = ScrollAxis.From(Direction);

            foreach (TestCell cell in Content.GetComponentsInChildren<TestCell>())
            {
                if (cell.Index != index) continue;
                return axis.SizeOf(((RectTransform)cell.transform).rect);
            }
            return float.NaN;
        }

        /// <summary>Content rect's own extent along the scroll axis.</summary>
        public float ContentSize => ScrollAxis.From(Direction).SizeOf(Content.rect);

        /// <summary>Installs the standard provider: rent prefab 0, record the index, hand it back.</summary>
        public void UseDefaultProvider()
        {
            View.SetCellProvider(index =>
            {
                BindCalls.Add(index);
                return View.RentCell<TestCell>(0);
            });
        }

        public void UseProvider(Func<int, RecyclerCell> provider) => View.SetCellProvider(provider);

        /// <summary>Parks the viewport at an offset-space position, the way a user's drag would.</summary>
        public void ScrollTo(float offset)
        {
            ScrollAxis axis = ScrollAxis.From(Direction);
            float cross = axis.Horizontal ? Content.anchoredPosition.y : Content.anchoredPosition.x;
            Content.anchoredPosition = axis.Compose(-axis.Sign * offset, cross);
        }

        public ScrollDirection Direction => (ScrollDirection)GetField(View, "_direction");

        /// <summary>Along-axis anchored position of the cell currently bound to a data index.</summary>
        public float CellOffsetOf(int index)
        {
            ScrollAxis axis = ScrollAxis.From(Direction);

            foreach (TestCell cell in Content.GetComponentsInChildren<TestCell>())
            {
                if (cell.Index != index) continue;

                float along = axis.Along(((RectTransform)cell.transform).anchoredPosition);
                return along / axis.Sign; // back into offset space
            }
            return float.NaN;
        }

        /// <summary>Stops Update() pumping — used after a deliberately broken provider has thrown.</summary>
        public void Freeze() => Root.SetActive(false);

        public void Destroy()
        {
            foreach (TestCell prefab in Prefabs)
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab.gameObject);

            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
        }

        private static RectTransform NewRect(string name, Transform parent, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }

        private static RecyclerViewSettings NewSettings(float cellSize, float spacing, int prewarmCount)
        {
            var settings = new RecyclerViewSettings();
            SetField(settings, "_cellSize", cellSize);
            SetField(settings, "_spacing", spacing);
            SetField(settings, "_prewarmCount", prewarmCount);
            return settings;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, Instance).SetValue(target, value);

        private static object GetField(object target, string name)
            => target.GetType().GetField(name, Instance).GetValue(target);
    }
}
