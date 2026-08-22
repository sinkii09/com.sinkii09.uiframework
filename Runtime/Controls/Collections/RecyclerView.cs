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
    /// <para>Cell sizes are <b>declared, never measured</b>: uniform from
    /// <see cref="RecyclerViewSettings.CellSize"/> by default, or per-index via
    /// <see cref="SetItemSizeProvider"/>. Because a size is known before its cell is realised, the
    /// content extent and every position are exact from the first frame — the list never shifts
    /// under the user to correct an estimate. The cost is that a cell may not size itself.</para>
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
        private Func<int, float> _sizeProvider;
        private int _itemCount;
        private int _tick;
        private int _pendingPrefabId = -1;
        private RecyclerCell _pendingCell;
        private bool _inPump;
        private bool _binding;
        private bool _rebuildingOffsets;

        /// <summary>
        /// Where every item sits. Initialized non-null so <see cref="TotalSize"/> answers before
        /// <c>OnInitialize</c> — it is public, and another component's <c>Awake</c> can reach it.
        /// </summary>
        private IItemOffsets _offsets = UniformOffsets.Empty;

        /// <summary>Total extent of the list along the scroll axis.</summary>
        public float TotalSize => _offsets.TotalSize;

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
            _sizeProvider = null;
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
            RebuildOffsets();
            ReleaseAllShown();
            ApplyContentSize();

            if (resetPosition) ScrollToIndex(0, 0f);
            Pump();
        }

        /// <summary>
        /// Declares each index's size along the scroll axis, ahead of any cell existing. Pass
        /// <c>null</c> to go back to the uniform <see cref="RecyclerViewSettings.CellSize"/>.
        ///
        /// <para>Sizes are <i>declared</i>, never measured: the view asks before it binds, so the
        /// content extent and every cell position are exact from the first frame and nothing shifts
        /// under the user later. A cell that self-sizes (a <c>ContentSizeFitter</c> on the scroll
        /// axis) breaks that and is reported by the editor-only measurement check.</para>
        /// </summary>
        public void SetItemSizeProvider(Func<int, float> sizeProvider)
        {
            if (RejectReentrant(nameof(SetItemSizeProvider))) return;

            // Build first, adopt second. Assigning the field up front and letting RebuildOffsets read
            // it means a provider that throws is still installed afterwards, so every later
            // SetItemCount re-invokes it and throws too — the view never recovers. Preserving
            // _offsets is not enough on its own.
            IItemOffsets rebuilt = BuildOffsets(sizeProvider);

            _sizeProvider = sizeProvider;
            _offsets = rebuilt;

            // Not just Pump(): the content rect and every live cell's cached offset are computed
            // from the old sizes, and Pump() re-reads neither.
            ReleaseAllShown();
            ApplyContentSize();
            Pump();
        }

        /// <summary>
        /// Re-declares one item's size and re-lays out everything after it.
        ///
        /// <para>O(n) in the item count — sized for a row that expands on tap, not for animating a
        /// size every frame.</para>
        ///
        /// <para><b>Discarded by the next <see cref="SetItemCount"/>.</b> A count change re-asks the
        /// size provider for every index, and this override lives outside it. That is deliberate —
        /// after a count change an index no longer refers to the same item — but it means a caller
        /// keeping expand/collapse state must re-apply it, or fold it into the provider instead.
        /// With no provider installed the rebuild returns to the uniform size and every override is
        /// dropped.</para>
        /// </summary>
        public void SetItemSize(int index, float size)
        {
            if (index < 0 || index >= _itemCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is outside [0, {_itemCount}).");
            if (!(size > 0f))
                throw new ArgumentOutOfRangeException(nameof(size), $"Size must be > 0 (was {size}).");
            if (RejectReentrant(nameof(SetItemSize))) return;

            // With no provider installed the table is uniform and immutable, so promote it. The
            // alternative — refusing until the caller installs a trivial provider — buys nothing.
            PrefixSumOffsets table = _offsets as PrefixSumOffsets ?? BuildUniformTable();
            _offsets = table.WithSize(index, size);

            ReleaseAllShown();
            ApplyContentSize();
            Pump();
        }

        /// <summary>
        /// Rebuilds the offset table from the current count and size provider.
        ///
        /// <para>The size provider is consumer code running outside the pump, so it needs its own
        /// reentrancy guard — and that guard must cover <b>only</b> this build. Holding it across the
        /// caller's follow-up <c>Pump()</c> would make the pump reject itself and leave the list
        /// silently empty.</para>
        /// </summary>
        private void RebuildOffsets() => _offsets = BuildOffsets(_sizeProvider);

        /// <summary>
        /// Produces the table for the current count under <paramref name="sizeProvider"/>, or throws
        /// having changed nothing. Callers assign the result — that is what keeps a throwing provider
        /// from leaving the view in a state it cannot be talked out of.
        /// </summary>
        private IItemOffsets BuildOffsets(Func<int, float> sizeProvider)
        {
            if (sizeProvider == null)
                return new UniformOffsets(_itemCount, _settings.CellSize, _settings.Spacing);

            _rebuildingOffsets = true;
            try
            {
                return new PrefixSumOffsets(_itemCount, sizeProvider, _settings.Spacing);
            }
            finally
            {
                _rebuildingOffsets = false;
            }
        }

        private PrefixSumOffsets BuildUniformTable()
        {
            float cellSize = _settings.CellSize;
            return new PrefixSumOffsets(_itemCount, _ => cellSize, _settings.Spacing);
        }

        /// <summary>Sizes the content rect to the current table. No-ops before initialization.</summary>
        private void ApplyContentSize()
        {
            if (_content == null) return;
            ContentLayout.SetContentSize(_content, TotalSize, _axis);
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

            // The alignment term needs THIS row's size: aligning index 400 to the viewport's trailing
            // edge lands short or long by however much that row differs from the declared default.
            float target = OffsetOf(index) - Mathf.Clamp01(alignment) * (viewportSize - _offsets.SizeOf(index));
            target = Mathf.Clamp(target, 0f, Mathf.Max(0f, TotalSize - viewportSize));

            Vector2 current = _content.anchoredPosition;
            float cross = _axis.Horizontal ? current.y : current.x;
            _content.anchoredPosition = _axis.Compose(-_axis.Sign * target, cross);

            // A programmatic move must not leave inertia behind, or the ScrollRect immediately
            // drifts away from the position we just set.
            _scrollRect.velocity = Vector2.zero;

            Pump();
        }

        /// <summary>Offset-space start of a data index.</summary>
        private float OffsetOf(int index) => _offsets.OffsetOf(index);

        /// <summary>
        /// Anchors, scale and rotation only — deliberately not size.
        ///
        /// <para>This runs once per <c>Instantiate</c>, not once per bind, so sizing here would give
        /// a pooled cell the size of whichever index first created it. Under a uniform size that was
        /// free; with per-item sizes it renders the wrong height after the first recycle and reports
        /// nothing. Size is applied per bind, in <c>CreateAt</c> and <c>Rebind</c>.</para>
        /// </summary>
        private RecyclerCell CreateCellInstance(int prefabId)
        {
            RecyclerCell instance = Instantiate(_cellPrefabs[prefabId], _content, false);
            ContentLayout.ConfigureCell((RectTransform)instance.transform, _axis);
            return instance;
        }
    }
}
