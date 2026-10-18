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
    ///
    /// <para>Every public method may be called before the view's <c>Awake</c> (a list inside an
    /// inactive panel): the count, providers and a requested scroll are kept and take effect once
    /// it initializes.</para>
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
        private bool _binding;
        private int _bindingIndex = -1;

        /// <summary>
        /// Where every item sits. Initialized non-null so <see cref="TotalSize"/> answers before
        /// <c>OnInitialize</c> — it is public, and another component's <c>Awake</c> can reach it.
        /// </summary>
        private IItemOffsets _offsets = UniformOffsets.Empty;

        /// <summary>
        /// The pool is created in <c>OnInitialize</c> and dropped in <c>OnDispose</c>, so it doubles
        /// as the flag — a call arriving after the view was destroyed (an awaited load finishing late)
        /// takes the same harmless path as one arriving before it initialized.
        /// </summary>
        private bool IsInitialized => _pool != null;

        /// <summary>Total extent of the list along the scroll axis.</summary>
        public float TotalSize => _offsets.TotalSize;

        /// <summary>Number of items the list is currently displaying data for.</summary>
        public int ItemCount => _itemCount;

        /// <summary>Data indices currently realised as live cells, ascending.</summary>
        public IReadOnlyList<int> ShownIndices => _shownIndices;

        /// <summary>
        /// Cells per row across the scroll axis. <c>1</c> is a plain list and is the default.
        ///
        /// <para>Items fill a row before starting the next, so item <c>i</c> sits at row
        /// <c>i / CrossAxisCount</c>, column <c>i % CrossAxisCount</c>. Indices, the provider
        /// contract and <see cref="ShownIndices"/> are unchanged by the column count — a grid is
        /// still one list of items, only folded.</para>
        /// </summary>
        public int CrossAxisCount => _settings.CrossAxisCount;

        protected override void OnInitialize()
        {
            _settings.Validate();
            ValidateCellPrefabs();

            _scrollRect = GetComponent<ScrollRect>();
            _content = _scrollRect.content;
            _viewport = _scrollRect.viewport != null ? _scrollRect.viewport : (RectTransform)transform;

            if (_content == null)
                throw new InvalidOperationException($"{nameof(RecyclerView)} on '{name}' has no ScrollRect content assigned.");

            _axis = ScrollAxis.From(_direction);
            _scrollRect.horizontal = _axis.Horizontal;
            _scrollRect.vertical = !_axis.Horizontal;
            _scrollRect.onValueChanged.AddListener(OnScrollMoved);

            ContentLayout.ConfigureRect(_content, _axis);

            _pool = new CellPool(Mathf.Max(_cellPrefabs.Length, 1), CreateCellInstance);
            if (_cellPrefabs.Length > 0) _pool.Prewarm(0, _settings.PrewarmCount);

            // Last, once the pool exists, so a size provider throwing here still leaves a view the
            // pump can run against. A count set before Awake built its table against settings
            // Validate had not checked yet, and could not size a content rect that was not known.
            _offsets = BuildOffsets(_sizeProvider, _settings.CrossAxisCount, _itemCount);
            ApplyContentSize();
        }

        protected override void OnDispose()
        {
            // OnRecycled runs below — consumer code, refused like anywhere else if it mutates.
            string previous = EnterBusy(CellCallbacks);
            try
            {
                ReleaseAllShown();
            }
            finally
            {
                ExitBusy(previous);
            }

            if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScrollMoved);
            _pool?.DestroyAll();
            _pool = null;
            _provider = null;
            _sizeProvider = null;
        }

        /// <summary>
        /// Authoring errors, refused at initialization rather than at the first bind that happens to
        /// reach the slot. Unity's <c>==</c>, so an unassigned or destroyed prefab counts as empty.
        /// </summary>
        private void ValidateCellPrefabs()
        {
            for (int id = 0; id < _cellPrefabs.Length; id++)
            {
                RecyclerCell prefab = _cellPrefabs[id];

                if (prefab == null)
                    throw new InvalidOperationException(
                        $"[RecyclerView] '{name}': cell prefab slot {id} is empty.");

                if (!(prefab.transform is RectTransform))
                    throw new InvalidOperationException(
                        $"[RecyclerView] '{name}': cell prefab '{prefab.name}' (id {id}) has no " +
                        "RectTransform. Cells are placed as UI and must be built on one.");
            }
        }

        /// <summary>
        /// Sets the callback that binds a cell for a data index. The callback must obtain its cell
        /// from <see cref="RentCell{T}"/> and must never return <c>null</c>.
        ///
        /// <para>Cells already on screen are re-bound through the new provider — they were bound by
        /// the old one and would otherwise keep showing its data until each scrolled out.</para>
        /// </summary>
        public void SetCellProvider(Func<int, RecyclerCell> provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (RejectReentrant(nameof(SetCellProvider))) return;

            string previous = EnterBusy(CellCallbacks);
            try
            {
                ReleaseAllShown();
                _provider = provider;

                // Pump here or the order of SetItemCount/SetCellProvider silently matters: setting
                // the count first leaves the list blank until something else happens to tick it.
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>
        /// Declares how many items exist. Recycles every live cell and rebuilds the window, so the
        /// provider is re-asked for everything currently on screen.
        ///
        /// <para>The size provider is asked for the new count before anything changes: if it throws,
        /// <see cref="ItemCount"/> and the layout are left exactly as they were. It runs while
        /// <see cref="ItemCount"/> still reports the <b>old</b> count, so it must take its range
        /// from its own data rather than from the view.</para>
        /// </summary>
        public void SetItemCount(int count, bool resetPosition = false)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (RejectReentrant(nameof(SetItemCount))) return;

            if (!IsInitialized)
            {
                // Built now so TotalSize answers, rebuilt in OnInitialize against validated settings.
                IItemOffsets early = BuildOffsets(_sizeProvider, _settings.CrossAxisCount, count);
                _itemCount = count;
                _offsets = early;
                if (resetPosition) RequestScroll(0, 0f);
                return;
            }

            string previous = EnterBusy(CellCallbacks);
            try
            {
                // Build first, adopt second — a throwing size provider used to leave the count
                // changed and the table not, so every later pump indexed past the table's end.
                IItemOffsets rebuilt = BuildOffsets(_sizeProvider, _settings.CrossAxisCount, count);
                _itemCount = count;
                _offsets = rebuilt;

                ReleaseAllShown();
                ApplyContentSize();

                if (resetPosition) ScrollToIndexCore(0, 0f);
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
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

            string previous = EnterBusy(CellCallbacks);
            try
            {
                // Build first, adopt second. Assigning the field up front means a provider that
                // throws is still installed afterwards, so every later SetItemCount re-invokes it and
                // throws too — the view never recovers. Preserving _offsets is not enough on its own.
                IItemOffsets rebuilt = BuildOffsets(sizeProvider, _settings.CrossAxisCount, _itemCount);

                _sizeProvider = sizeProvider;
                _offsets = rebuilt;

                // Not just a pump: the content rect and every live cell's cached offset are computed
                // from the old sizes, and the pump re-reads neither.
                ReleaseAllShown();
                ApplyContentSize();
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>
        /// Changes how many cells sit in a row, at runtime. <c>1</c> turns the view back into a list.
        ///
        /// <para>Every live cell is released and rebuilt: a handle caches the offset and the column
        /// anchors it was laid out with, and both are wrong the moment the row width changes.
        /// Keeping the scroll position would be meaningless anyway — the same offset lands on a
        /// different item once items repack into rows, so the view stays where it is numerically and
        /// the caller re-aims with <see cref="ScrollToIndex"/> if it cares.</para>
        /// </summary>
        public void SetCrossAxisCount(int count)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(count), $"{nameof(CrossAxisCount)} must be >= 1 (was {count}). Use 1 for a list.");

            if (RejectReentrant(nameof(SetCrossAxisCount))) return;
            if (count == _settings.CrossAxisCount) return;

            string previous = EnterBusy(CellCallbacks);
            try
            {
                // Build first, adopt second — the same rule as SetItemSizeProvider, and it applies
                // here for a sharper reason: a size provider that throws partway would otherwise leave
                // the column count already changed while the offset table is still the old one, so
                // the next layout would place columns against list offsets and overlap them.
                IItemOffsets rebuilt = BuildOffsets(_sizeProvider, count, _itemCount);

                _settings.OverrideCrossAxisCount(count);
                _offsets = rebuilt;

                ReleaseAllShown();
                ApplyContentSize();
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>
        /// Re-asks the size provider for every index and re-lays the list out, keeping the scroll
        /// position. Call after changing whatever state the provider reads — an expanded row, a
        /// re-measured message.
        ///
        /// <para>O(n) in the item count. No-op when no provider is installed: uniform sizes have
        /// nothing to re-ask.</para>
        ///
        /// <para>There is deliberately no per-index setter. The provider is the single source of
        /// truth for size, so an override stored beside it would be discarded by the next
        /// <see cref="SetItemCount"/> — and worse, an override keyed by index outlives the item it
        /// was meant for as soon as the list's contents shift. Keeping the size in the consumer's own
        /// data and re-asking is both smaller and always correct.</para>
        /// </summary>
        public void RefreshSizes()
        {
            if (_sizeProvider == null) return;
            if (RejectReentrant(nameof(RefreshSizes))) return;

            string previous = EnterBusy(CellCallbacks);
            try
            {
                _offsets = BuildOffsets(_sizeProvider, _settings.CrossAxisCount, _itemCount);

                ReleaseAllShown();
                ApplyContentSize();
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>
        /// Produces the table for <paramref name="itemCount"/> items under
        /// <paramref name="sizeProvider"/>, or throws having changed nothing. Callers assign the
        /// result — that is what keeps a throwing provider from leaving the view in a state it cannot
        /// be talked out of.
        ///
        /// <para>The count is a parameter, not the field, so a caller can build for a count it has not
        /// adopted yet.</para>
        /// </summary>
        private IItemOffsets BuildOffsets(Func<int, float> sizeProvider, int crossAxisCount, int itemCount)
        {
            // The inner table is built over ROWS. Handing it the item count instead makes the content
            // rect that many times too tall, with nothing on screen to show for it.
            int strideCount = crossAxisCount <= 1
                ? itemCount
                : (itemCount + crossAxisCount - 1) / crossAxisCount;

            IItemOffsets strides;

            if (sizeProvider == null)
            {
                strides = new UniformOffsets(strideCount, _settings.CellSize, _settings.Spacing);
            }
            else
            {
                // The size provider is consumer code running outside the pump; this scope is what
                // refuses a provider that mutates the list it is being asked about.
                string previous = EnterBusy(SizeCallbacks);
                try
                {
                    strides = new PrefixSumOffsets(
                        strideCount, RowSizeFrom(sizeProvider, crossAxisCount, itemCount), _settings.Spacing);
                }
                finally
                {
                    ExitBusy(previous);
                }
            }

            return crossAxisCount <= 1 ? strides : new GridOffsets(strides, crossAxisCount, itemCount);
        }

        /// <summary>
        /// Turns the consumer's per-ITEM size provider into the per-ROW one the table needs, by
        /// taking the tallest item in each row.
        ///
        /// <para>The public contract stays "declare the size of item <c>i</c>" in both modes, which
        /// is the point: a caller does not have to know whether the view is currently a grid. Every
        /// cell in a row is then laid out at the row's height — the same rule a uniform-span grid
        /// uses everywhere else, and the reason a short cell in a tall row leaves space rather than
        /// overlapping its neighbour below.</para>
        ///
        /// <para>At one column this returns the caller's own delegate untouched, so the single-column
        /// path is not merely equivalent — it is the same object making the same calls.</para>
        /// </summary>
        private Func<int, float> RowSizeFrom(Func<int, float> itemSize, int crossAxisCount, int itemCount)
        {
            if (crossAxisCount <= 1) return itemSize;

            return row =>
            {
                int first = row * crossAxisCount;
                int end = Math.Min(first + crossAxisCount, itemCount);

                float tallest = 0f;
                for (int i = first; i < end; i++)
                {
                    float size = itemSize(i);

                    // Checked per ITEM, not left to PrefixSumOffsets to catch per row. Two reasons:
                    // a row keeps its tallest, so one negative item beside a positive one would pass
                    // unnoticed here while the list path rejects it outright; and the error
                    // PrefixSumOffsets raises names the index it was given, which in grid mode is a
                    // ROW — sending whoever reads it to the wrong item entirely.
                    if (!(size > 0f))
                        throw new ArgumentOutOfRangeException(
                            nameof(itemSize),
                            $"[RecyclerView] '{name}': declared size for index {i} was {size}. " +
                            "Sizes must be positive.");

                    if (size > tallest) tallest = size;
                }

                return tallest;
            };
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

            // Checked against the PREFAB, before renting: the same check made on the rented instance
            // fails with that instance already active and tracked by nothing.
            if (_cellPrefabs[prefabId] is not T)
            {
                RecyclerCell prefab = _cellPrefabs[prefabId];
                throw new InvalidOperationException(prefab == null
                    ? $"[RecyclerView] '{name}': cell prefab slot {prefabId} is empty."
                    : $"Cell prefab {prefabId} is a {prefab.GetType().Name}, not a {typeof(T).Name}.");
            }

            var typed = (T)_pool.Rent(prefabId);

            // Index and a fresh bind token from here on, so the cell's own Bind can use both.
            typed.BeginBind(_bindingIndex);

            _pendingPrefabId = prefabId;
            _pendingCell = typed;
            return typed;
        }

        /// <summary>Re-binds every shown cell, keeping the scroll position.</summary>
        public void RefreshAll()
        {
            if (RejectReentrant(nameof(RefreshAll))) return;

            string previous = EnterBusy(CellCallbacks);
            try
            {
                ReleaseAllShown();
                PumpCore();
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>Re-binds one index, if it is currently shown.</summary>
        public void RefreshIndex(int index)
        {
            if (RejectReentrant(nameof(RefreshIndex))) return;

            string previous = EnterBusy(CellCallbacks);
            try
            {
                for (int i = 0; i < _shown.Count; i++)
                {
                    if (_shown[i].Index != index) continue;

                    Rebind(i);
                    return;
                }
            }
            finally
            {
                ExitBusy(previous);
            }
        }

        /// <summary>Visits every live cell. The action must not mutate the list.</summary>
        public void ForEachShownCell(Action<RecyclerCell> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // A scope of its own, so a mutating call from inside the action is refused loudly rather
            // than corrupting _shown mid-walk — and so that, called from inside the pump, leaving it
            // restores the pump's guard instead of clearing it.
            string previous = EnterBusy(VisitCallbacks);
            try
            {
                for (int i = 0; i < _shown.Count; i++) action(_shown[i].Cell);
            }
            finally
            {
                ExitBusy(previous);
            }
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
