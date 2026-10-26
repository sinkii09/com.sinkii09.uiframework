using R3;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Sits on the UIRoot's root canvas and drives the SafeArea panel that holds every UI layer:
    // anchors it to the device safe area (notches, rounded corners) and, optionally, caps its aspect
    // so a wide screen (desktop browser, landscape tablet) shows a centred portrait column.
    //
    // Injected by UIFrameworkLifetimeScope itself (a build callback in Configure), so it needs no
    // autoInjectGameObjects entry. That requires the scope to autoRun and any Configure override to
    // call base — otherwise _safeArea stays null and Start says so. Only the first UIRootSetup in
    // the scope's hierarchy is injected. Following a resize needs a SafeAreaProvider on the UIRoot;
    // the NullSafeAreaProvider fallback reports the screen once and never again.
    //
    // The framework draws nothing outside the column: whatever renders behind the canvas (camera
    // clear colour, a background image) shows through as the side bars. World/camera content is
    // not capped.
    [AddComponentMenu("UIFramework/UIRootSetup")]
    public class UIRootSetup : MonoBehaviour
    {
        [Inject] private ISafeAreaProvider _safeArea;
        [SerializeField] private RectTransform _safeAreaPanel;

        [Tooltip("Widest width/height the UI may use. Wider screens get a centred column of this " +
                 "aspect, inside the safe area. 0.5625 = 9:16. 0 = off.")]
        [SerializeField, Min(0f)] private float _maxAspect;

        [Tooltip("Optional. Defaults to the CanvasScaler on this canvas.")]
        [SerializeField] private CanvasScaler _canvasScaler;

        private float _originalMatch;

        /// <summary>True while the screen is wider than the aspect cap and the UI is a centred column.</summary>
        public bool IsLetterboxed { get; private set; }

        private void Start()
        {
            if (_safeAreaPanel == null)
            {
                Debug.LogError("[UIRootSetup] _safeAreaPanel not assigned in Inspector.", this);
                return;
            }

            // Reachable: a duplicate UIRoot is destroyed in its scope's Awake before the container
            // builds, so its UIRootSetup is never injected, yet Destroy is deferred and this Start
            // can still run. That case is expected and stays quiet. Anything else (scope without
            // autoRun, a Configure override skipping base) is a real misconfiguration.
            if (_safeArea == null)
            {
                var scope = GetComponentInParent<UIFrameworkLifetimeScope>(true);
                if (scope != null && scope.IsDuplicate) return;

                Debug.LogError("[UIRootSetup] ISafeAreaProvider was not injected. Expected on a " +
                               "duplicate UIRoot being destroyed; otherwise check the scope autoRuns " +
                               "and any Configure override calls base.", this);
                return;
            }

            if (_canvasScaler == null) _canvasScaler = GetComponentInParent<CanvasScaler>();
            // Captured BEFORE Subscribe: the ReactiveProperty emits its current value on Subscribe,
            // so the first apply — which may already force the match to 1 — happens inside it.
            if (_canvasScaler != null) _originalMatch = _canvasScaler.matchWidthOrHeight;
            else if (_maxAspect > 0f)
                Debug.LogWarning("[UIRootSetup] No CanvasScaler found; the aspect cap will not adjust UI scale.", this);

            // A match change resizes the root rect, which re-fires SafeAreaProvider's
            // OnRectTransformDimensionsChange — but with an unchanged safe area, which the
            // ReactiveProperty drops. No feedback loop.
            _safeArea.OnChanged
                .Subscribe(area => Apply(area, new Vector2(Screen.width, Screen.height)))
                .AddTo(this);
        }

        // Seam for tests: the PlayMode screen size cannot be controlled.
        internal void Apply(Rect area, Vector2 screen)
        {
            // Minimised window, or the first frame on some browsers: nothing sensible to divide by.
            if (screen.x <= 0f || screen.y <= 0f || area.width <= 0f || area.height <= 0f) return;

            var capped = CapAspect(area, _maxAspect);
            var wasLetterboxed = IsLetterboxed;
            IsLetterboxed = capped.width < area.width;

            _safeAreaPanel.anchorMin = capped.position / screen;
            _safeAreaPanel.anchorMax = (capped.position + capped.size) / screen;
            _safeAreaPanel.offsetMin = _safeAreaPanel.offsetMax = Vector2.zero;

            if (IsLetterboxed != wasLetterboxed) ApplyScalerMatch();
        }

        // A portrait reference resolution matched on width would size a narrow column as if the
        // whole wide screen were the reference width — everything huge and overflowing vertically.
        // So match height while capped. Written only when the letterbox state CHANGES, so a cap of 0
        // never touches the match and a game's own runtime match is left alone; ending a letterbox
        // does restore the value from Start, overwriting any change made while capped. The match
        // affects every child of this canvas, and it scales by the full screen height while the
        // column is the safe-area height, so with a notch the column is a little under reference
        // width — exact only when the reference resolution's aspect equals _maxAspect.
        private void ApplyScalerMatch()
        {
            if (_canvasScaler == null || _canvasScaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                return;

            _canvasScaler.matchWidthOrHeight = IsLetterboxed ? 1f : _originalMatch;
        }

        // Narrows a too-wide area to maxAspect (width/height), centred. Applied inside the safe area,
        // so a notch is never covered. maxAspect <= 0 means no cap.
        internal static Rect CapAspect(Rect area, float maxAspect)
        {
            if (maxAspect <= 0f || area.height <= 0f || area.width / area.height <= maxAspect)
                return area;

            var width = area.height * maxAspect;
            return new Rect(area.x + (area.width - width) * 0.5f, area.y, width, area.height);
        }
    }
}
