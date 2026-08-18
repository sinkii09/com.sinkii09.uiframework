using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// A recycling list: keeps only the cells the viewport can see, whatever the item count.
    ///
    /// <para>Index-driven by design. The consumer owns its data, declares how many items exist via
    /// <see cref="SetItemCount"/>, and supplies a provider that binds a cell for a given index.
    /// The count is authoritative — a provider returning <c>null</c> is an error, not an
    /// end-of-list signal.</para>
    ///
    /// <para>Phase 1 assumes a uniform, declared cell size (see <see cref="RecyclerViewSettings"/>).
    /// Variable sizing arrives in phase 2.</para>
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public partial class RecyclerView : UIControlBase
    {
        [SerializeField] private ScrollDirection _direction = ScrollDirection.TopToBottom;
        [SerializeField] private RecyclerViewSettings _settings = new();

        [Tooltip("Cell prefabs. A prefab's index in this array is its prefab id — ids are stable " +
                 "under renaming, unlike the name-keyed lookup this replaces.")]
        [SerializeField] private RecyclerCell[] _cellPrefabs = Array.Empty<RecyclerCell>();

        private ScrollRect _scrollRect;
        private RectTransform _content;
        private RectTransform _viewport;
        private ScrollAxis _axis;
        private CellPool _pool;

        private readonly List<CellHandle> _shown = new();
        private readonly List<int> _shownIndices = new();

        private Func<int, RecyclerCell> _provider;
        private int _itemCount;
        private int _tick;
        private int _pendingPrefabId = -1;
        private RecyclerCell _pendingCell;
        private bool _inPump;
        private bool _binding;

        /// <summary>Total extent of the list along the scroll axis.</summary>
        public float TotalSize => _itemCount <= 0 ? 0f : _itemCount * _settings.Stride - _settings.Spacing;

        /// <summary>Number of items the list is currently displaying data for.</summary>
        public int ItemCount => _itemCount;

        /// <summary>Data indices currently realised as live cells, ascending.</summary>
        public IReadOnlyList<int> ShownIndices => _shownIndices;

        protected override void OnInitialize()
        {
            _settings.Validate();

            _scrollRect = GetComponent<ScrollRect>();
            _content = _scrollRect.content;
            _viewport = _scrollRect.viewport != null ? _scrollRect.viewport : (RectTransform)transform;

            if (_content == null)
                throw new InvalidOperationException($"{nameof(RecyclerView)} on '{name}' has no ScrollRect content assigned.");

            _axis = ScrollAxis.From(_direction);
            _scrollRect.horizontal = _axis.Horizontal;
            _scrollRect.vertical = !_axis.Horizontal;

            ContentLayout.ConfigureRect(_content, _axis);

            _pool = new CellPool(Mathf.Max(_cellPrefabs.Length, 1), CreateCellInstance);
            if (_cellPrefabs.Length > 0) _pool.Prewarm(0, _settings.PrewarmCount);
        }

        protected override void OnDispose()
        {
            ReleaseAllShown();
            _pool?.DestroyAll();
            _provider = null;
        }

        /// <summary>
        /// Sets the callback that binds a cell for a data index. The callback must obtain its cell
        /// from <see cref="RentCell{T}"/> and must never return <c>null</c>.
        /// </summary>
        public void SetCellProvider(Func<int, RecyclerCell> provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (RejectReentrant(nameof(SetCellProvider))) return;

            _provider = provider;

            // Pump here or the order of SetItemCount/SetCellProvider silently matters: setting the
            // count first leaves the list blank until something else happens to tick it.
            Pump();
        }

        /// <summary>
        /// Declares how many items exist. Recycles every live cell and rebuilds the window, so the
        /// provider is re-asked for everything currently on screen.
        /// </summary>
        public void SetItemCount(int count, bool resetPosition = false)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (RejectReentrant(nameof(SetItemCount))) return;

            _itemCount = count;
            ReleaseAllShown();
            ContentLayout.SetContentSize(_content, TotalSize, _axis);

            if (resetPosition) ScrollToIndex(0, 0f);
            Pump();
        }

        /// <summary>Takes a pooled cell. Only legal from inside the cell provider, once per call.</summary>
        public T RentCell<T>(int prefabId) where T : RecyclerCell
        {
            if (prefabId < 0 || prefabId >= _cellPrefabs.Length)
                throw new ArgumentOutOfRangeException(nameof(prefabId), $"No cell prefab with id {prefabId}.");

            // Both contract violations below leak a cell rather than fail visibly: the view only
            // tracks the cell the provider returns, so anything else rented is live forever with no
            // handle pointing at it. Cheaper to refuse than to hunt an ever-growing instance count.
            if (!_binding)
                throw new InvalidOperationException(
                    $"[RecyclerView] '{name}': {nameof(RentCell)} is only legal from inside the cell " +
                    "provider — a cell rented anywhere else is never recycled.");

            if (_pendingPrefabId >= 0)
                throw new InvalidOperationException(
                    $"[RecyclerView] '{name}': the cell provider rented more than one cell for a " +
                    "single index. Only the returned cell is tracked; the rest leak.");

            RecyclerCell cell = _pool.Rent(prefabId);
            if (cell is not T typed)
                throw new InvalidOperationException(
                    $"Cell prefab {prefabId} is a {cell.GetType().Name}, not a {typeof(T).Name}.");

            _pendingPrefabId = prefabId;
            _pendingCell = typed;
            return typed;
        }

        /// <summary>Re-binds every shown cell, keeping the scroll position.</summary>
        public void RefreshAll()
        {
            if (RejectReentrant(nameof(RefreshAll))) return;

            ReleaseAllShown();
            Pump();
        }

        /// <summary>Re-binds one index, if it is currently shown.</summary>
        public void RefreshIndex(int index)
        {
            if (RejectReentrant(nameof(RefreshIndex))) return;

            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i].Index != index) continue;

                Rebind(i);
                return;
            }
        }

        /// <summary>Visits every live cell. The action must not mutate the list.</summary>
        public void ForEachShownCell(Action<RecyclerCell> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // Borrow the pump's guard so a mutating call from inside the action is refused loudly
            // rather than corrupting _shown mid-walk.
            _inPump = true;
            try
            {
                for (int i = 0; i < _shown.Count; i++) action(_shown[i].Cell);
            }
            finally
            {
                _inPump = false;
            }
        }

        /// <summary>
        /// Scrolls so <paramref name="index"/> sits at <paramref name="alignment"/> within the
        /// viewport (0 = leading edge, 1 = trailing edge), clamped to the list's bounds.
        /// </summary>
        public void ScrollToIndex(int index, float alignment = 0f)
        {
            if (_itemCount <= 0) return;
            if (RejectReentrant(nameof(ScrollToIndex))) return;

            index = Mathf.Clamp(index, 0, _itemCount - 1);
            float viewportSize = _axis.SizeOf(_viewport.rect);
            float target = OffsetOf(index) - Mathf.Clamp01(alignment) * (viewportSize - _settings.CellSize);
            target = Mathf.Clamp(target, 0f, Mathf.Max(0f, TotalSize - viewportSize));

            Vector2 current = _content.anchoredPosition;
            float cross = _axis.Horizontal ? current.y : current.x;
            _content.anchoredPosition = _axis.Compose(-_axis.Sign * target, cross);

            // A programmatic move must not leave inertia behind, or the ScrollRect immediately
            // drifts away from the position we just set.
            _scrollRect.velocity = Vector2.zero;

            Pump();
        }

        /// <summary>Offset-space start of a data index. Uniform stride in phase 1.</summary>
        private float OffsetOf(int index) => index * _settings.Stride;

        private RecyclerCell CreateCellInstance(int prefabId)
        {
            RecyclerCell instance = Instantiate(_cellPrefabs[prefabId], _content, false);
            ContentLayout.ConfigureCell((RectTransform)instance.transform, _settings.CellSize, _axis);
            return instance;
        }
    }
}
