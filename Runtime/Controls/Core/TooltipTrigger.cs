using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Raises tooltips for the widget it sits on, from any of four sources: mouse hover, mouse
    // click, gamepad/keyboard focus, and touch long-press. All timing policy lives in
    // ITooltipService — this only translates input events into Show/Hide calls.
    //
    // Payload comes from an ITooltipSource on this GameObject or a parent; failing that, from the
    // serialized title/body below, so a static hint needs no extra component.
    public class TooltipTrigger : UIControlBase,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        [Header("Sources")]
        [SerializeField] private bool _onHover = true;
        [SerializeField] private bool _onClick;
        [SerializeField] private bool _onFocus = true;
        [SerializeField] private bool _onLongPress = true;

        [Header("Placement")]
        [SerializeField] private TooltipPlacement _placement = TooltipPlacement.Auto;
        [Tooltip("Rect the tooltip positions against. Defaults to this GameObject's own rect.")]
        [SerializeField] private RectTransform _anchorOverride;

        [Header("Inline content (used only when no ITooltipSource is present)")]
        [SerializeField] private string _title;
        [SerializeField, TextArea] private string _body;

        private static readonly NullTooltipService Fallback = new();

        private ITooltipService _tooltips;
        private ITooltipSource _source;
        private RectTransform _anchor;

        // Long-press tracking
        private bool _pressing;
        private float _pressStartedAt;
        private Vector2 _pressOrigin;

        // Frame this trigger became armed for focus events. EventSystem auto-selects
        // firstSelectedGameObject on its first frame, which would otherwise pop a tooltip at boot.
        private int _armedFrame;

        private ITooltipService Tooltips => _tooltips ??= ResolveService();

        [Inject]
        public void Construct(ITooltipService tooltips) => _tooltips = tooltips;

        protected override void OnInitialize()
        {
            _anchor = _anchorOverride != null ? _anchorOverride : (RectTransform)transform;
            _source = GetComponent<ITooltipSource>() ?? GetComponentInParent<ITooltipSource>();
        }

        protected override void OnDispose() { }

        private void OnEnable() => _armedFrame = Time.frameCount + 1;

        // UIControlBase's Awake/OnDestroy are private and non-virtual with no disable hook, so the
        // release lives here: a recycled or disabled cell must not strand the tooltip it owns.
        private void OnDisable()
        {
            _pressing = false;
            Release();
        }

        private void Update()
        {
            if (!_pressing) return;

            // Cancel on travel, or every scroll gesture that starts on a cell fires a tooltip.
            float moved = (CurrentPointerPosition() - _pressOrigin).magnitude;
            if (moved > Tooltips.LongPressMoveCancelPixels)
            {
                _pressing = false;
                return;
            }

            if (Time.unscaledTime - _pressStartedAt >= Tooltips.LongPressSeconds)
            {
                _pressing = false;
                Raise(TooltipSource.Touch);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Unity raises OnPointerEnter on touch PRESS too, so an ungated hover path
            // double-triggers on mobile. Mouse pointers are the negative ids.
            if (!_onHover || eventData.pointerId >= 0) return;
            Raise(TooltipSource.Hover);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData.pointerId >= 0) return;
            Release();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerId < 0)
            {
                if (_onClick) Raise(TooltipSource.Click);
                return;
            }

            if (!_onLongPress) return;
            _pressing = true;
            _pressStartedAt = Time.unscaledTime;
            _pressOrigin = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId < 0) return;
            _pressing = false;
            Release();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (!_onFocus || Time.frameCount <= _armedFrame) return;
            Raise(TooltipSource.Focus);
        }

        // Moving focus can fire OnDeselect on the old target AFTER OnSelect on the new one, which
        // would kill the tooltip just shown. Safe because ITooltipService.Hide ignores a source
        // that is not the current anchor.
        public void OnDeselect(BaseEventData eventData)
        {
            if (!_onFocus) return;
            Release();
        }

        // Call this after rebinding this widget's content in place. The service's anchor watchdog
        // catches a destroyed or deactivated anchor, but a pooled RecyclerView cell reused for a
        // different item without ever being deactivated (CellPool does this — it reuses live cells
        // in the same frame with no SetActive churn) passes every one of those checks while the
        // tooltip still shows the PREVIOUS item's payload. Nothing can detect that from outside;
        // the widget has to say so.
        public void NotifyContentChanged()
        {
            if (Tooltips.CurrentAnchor == _anchor) Release();
        }

        private void Raise(TooltipSource source)
        {
            var payload = BuildPayload();
            if (payload == null || _anchor == null) return;
            Tooltips.Show(new TooltipRequest(_anchor, payload, _placement, source));
        }

        private void Release() => Tooltips.Hide(_anchor);

        private ITooltipPayload BuildPayload()
        {
            // Queried at show time, not cached: a stat that changed since bind shows its new value.
            if (_source != null) return _source.GetTooltipPayload();

            if (string.IsNullOrEmpty(_title) && string.IsNullOrEmpty(_body)) return null;
            return new TooltipContent { Title = _title, Body = _body };
        }

        // New Input System, null-guarded the same way NewInputSystemBackButtonSource guards
        // Keyboard.current. Legacy Input.mousePosition would throw outright in a project set to
        // "Input System Package (New)" only. Pointer.current covers mouse and touchscreen alike.
        //
        // Polled rather than taken from IDragHandler on purpose: implementing IDragHandler here
        // would make this widget the drag target and stop any parent ScrollRect from scrolling.
        private static Vector2 CurrentPointerPosition()
            => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;

        // Triggers inside a view prefab are injected with the rest of the view's hierarchy
        // (UIViewFactory.cs:314). Anything instantiated later — a RecyclerView cell above all —
        // is not, so fall back to the root scope once and cache the result.
        private ITooltipService ResolveService()
        {
            var scope = LifetimeScope.Find<UIFrameworkLifetimeScope>();
            if (scope != null && scope.Container != null &&
                scope.Container.TryResolve<ITooltipService>(out var resolved))
                return resolved;

            Debug.LogWarning(
                $"[TooltipTrigger] No ITooltipService reachable from '{name}' — tooltips disabled " +
                "for this widget. Ensure a UIFrameworkLifetimeScope exists in the scene.", this);
            return Fallback;
        }
    }
}
