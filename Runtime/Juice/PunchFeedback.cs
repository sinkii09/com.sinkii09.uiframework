using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Code-triggered "something happened here" feedback: a scale punch (overshoot), a squash
    /// (x stretches while y compresses) and a colour flash. Call it from a view when a value changes,
    /// a reward lands or an action is rejected.
    /// </summary>
    /// <remarks>
    /// Scale and colour are independent channels with their own kill-and-restore, so a flash never
    /// snaps an in-flight punch and vice versa. One scale driver per transform — see
    /// <see cref="PressScaleFeedback"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PunchFeedback : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Graphic _graphic;
        [SerializeField] private float _punchAmount = 0.12f;
        [SerializeField] private float _punchDuration = 0.2f;
        [SerializeField] private int _vibrato = 6;
        [SerializeField, Range(0f, 1f)] private float _elasticity = 0.6f;
        [SerializeField] private float _flashDuration = 0.12f;
        [SerializeField] private bool _useUnscaledTime = true;

        private Vector3 _baseScale = Vector3.one;
        private bool _initialized;

        // Captured when a flash starts from idle, not at Awake: game code may legitimately recolour
        // the graphic between flashes, and a stale Awake colour would be written back over it.
        private Color _baseColor;
        private bool _hasBaseColor;

        public bool IsAnimating =>
            (_target != null && DOTween.IsTweening(_target)) ||
            (_graphic != null && DOTween.IsTweening(_graphic));

        private void Awake() => EnsureInitialized();

        // Also called from every entry point: a view may punch an object that has never been
        // active (Awake not yet run), and without this the call would be silently dropped.
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (_target == null) _target = transform;
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            _baseScale = JuiceScale.CaptureBase(_target, this);
        }

        /// <summary>Uniform scale punch. <paramref name="strength"/> 1 = the authored amount.</summary>
        public void PunchScale(float strength = 1f)
        {
            float amount = _punchAmount * strength;
            PlayScalePunch(new Vector3(amount, amount, 0f));
        }

        /// <summary>Volume-preserving squash: wider and shorter, then wobble back.</summary>
        public void Squash(float strength = 1f)
        {
            float amount = _punchAmount * strength;
            PlayScalePunch(new Vector3(amount, -amount, 0f));
        }

        /// <summary>Tints the graphic to <paramref name="color"/> and back.</summary>
        public void Flash(Color color)
        {
            EnsureInitialized();
            if (_graphic == null) return;

            // Capture BEFORE the kill, and only from idle — mid-flash the graphic holds a blended
            // colour, and capturing that would make the flash permanent.
            if (!DOTween.IsTweening(_graphic))
            {
                _baseColor = _graphic.color;
                _hasBaseColor = true;
            }

            KillAndRestoreColor();
            // Two Yoyo loops = there and back (SetLoops counts legs, not round trips).
            _graphic.DOColor(color, _flashDuration)
                .SetLoops(2, LoopType.Yoyo)
                .SetUpdate(_useUnscaledTime)
                .SetLink(gameObject);
        }

        private void PlayScalePunch(Vector3 punch)
        {
            EnsureInitialized();

            KillAndRestoreScale();
            _target.DOPunchScale(punch, _punchDuration, _vibrato, _elasticity)
                .SetUpdate(_useUnscaledTime)
                .SetLink(gameObject);
        }

        private void OnDisable()
        {
            KillAndRestoreScale();
            KillAndRestoreColor();
        }

        private void KillAndRestoreScale()
        {
            if (_target == null) return;

            _target.DOKill();
            _target.localScale = _baseScale;
        }

        private void KillAndRestoreColor()
        {
            if (_graphic == null || !_hasBaseColor) return;

            _graphic.DOKill();
            _graphic.color = _baseColor;
        }
    }
}
