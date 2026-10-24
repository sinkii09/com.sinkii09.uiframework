using UnityEngine;
using UnityEngine.InputSystem;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Shows a short effect wherever the player clicks or taps. Put it on a UI object under a Canvas
    /// and assign an effect — a prefab, or an inactive child used as a template.
    /// Quickest setup: <c>GameObject &gt; UI &gt; UIFramework &gt; Tap Effect</c>.
    /// </summary>
    /// <remarks>
    /// The effect restarts by being switched off and on again, so anything that plays on enable works:
    /// <see cref="TapRipple"/>, a ParticleSystem with Play On Awake, an Animator. The effect should
    /// switch itself off when it ends (TapRipple does; for particles set Stop Action = Disable).
    /// Reads the primary pointer only (mouse left button, first finger, pen tip). Draws above any
    /// canvas with a lower sorting order; keep it a root canvas or tick Override Sorting.
    /// Supports Screen Space Overlay and Screen Space Camera canvases (not World Space).
    /// <see cref="Play"/> does nothing until the spawner has been active once.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TapEffectSpawner : MonoBehaviour
    {
        [Header("Effect")]
        [Tooltip("Prefab or inactive child shown at each tap. It restarts when enabled and should switch itself off when it ends (TapRipple does; particles: Stop Action = Disable).")]
        [SerializeField] private GameObject _effect;

        [Tooltip("Most effects on screen at once. A tap past this restarts the oldest one.")]
        [SerializeField, Min(1)] private int _maxEffects = 8;

        private InputAction _press;
        private GameObject[] _instances;
        private int _next;
        private RectTransform _area;
        private Canvas _canvas;

        private void Awake()
        {
            _area = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();

            // <Pointer>/press = mouse left button, primary touch, or pen tip: one binding, every platform.
            _press = new InputAction("TapEffect", InputActionType.Button, "<Pointer>/press");
            _press.performed += OnPress;
        }

        // Checked in Start, not Awake: fields assigned right after AddComponent (code, tests) are in
        // place by then. A tap effect that silently shows nothing is the failure we refuse.
        private void Start()
        {
            if (_area == null || _canvas == null)
                Debug.LogError("[TapEffect] Must be on a UI object under a Canvas.", this);
            if (_effect == null)
                Debug.LogError("[TapEffect] No effect assigned — taps will show nothing.", this);
            // An active scene template plays (and keeps) its own end pose, and every copy inherits it.
            else if (_effect.scene.IsValid() && _effect.activeSelf)
                Debug.LogError("[TapEffect] The effect template is an active scene object — deactivate it, or use a prefab.", this);
        }

        private void OnEnable() => _press.Enable();

        private void OnDisable() => _press.Disable();

        private void OnDestroy() => _press.Dispose();

        /// <summary>Shows the effect at a screen position. Taps call this; game code may too.</summary>
        public void Play(Vector2 screenPosition)
        {
            if (_effect == null || _canvas == null || _area == null) return;

            Canvas root = _canvas.rootCanvas;
            Camera eventCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenPosition, eventCamera, out Vector2 localPoint))
                return;

            GameObject instance = NextInstance();
            instance.SetActive(false); // off → on restarts an effect that is still playing
            // localPosition, not anchoredPosition: the point is pivot-relative, so the effect's anchors don't matter.
            instance.transform.localPosition = localPoint;
            instance.SetActive(true);
        }

        private void OnPress(InputAction.CallbackContext context)
        {
            // Read from the device that pressed, not Pointer.current, so mouse + touch can't mix positions.
            if (context.control.device is Pointer pointer)
                Play(pointer.position.ReadValue());
        }

        // A ring of slots used in order, so a burst past _maxEffects restarts the oldest effect.
        // Built on first use (not in Awake), so a _maxEffects set after AddComponent still counts.
        private GameObject NextInstance()
        {
            _instances ??= new GameObject[Mathf.Max(1, _maxEffects)];

            int slot = _next;
            _next = (_next + 1) % _instances.Length;

            // Unity's == (not ??=) so a destroyed instance is replaced.
            if (_instances[slot] == null)
                _instances[slot] = Instantiate(_effect, transform);
            return _instances[slot];
        }
    }
}
