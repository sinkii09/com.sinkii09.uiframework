using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// A ready-made tap effect: grows and fades out, then switches itself off. It plays every time
    /// it is enabled, which is how <see cref="TapEffectSpawner"/> restarts it.
    /// </summary>
    /// <remarks>
    /// Fades a CanvasGroup, so every child fades together and authored colours stay untouched.
    /// Known limitation: if a parent is disabled mid-ripple, the ripple stays active and replays once
    /// when the parent is enabled again (Unity forbids SetActive during a hierarchy (de)activation).
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public sealed class TapRipple : MonoBehaviour
    {
        [Header("Ripple — seconds in unscaled time; scales multiply the authored scale")]
        [SerializeField, Min(0.01f)] private float _duration = 0.35f;
        [Tooltip("Scale at the moment of the tap.")]
        [SerializeField] private float _startScale = 0.3f;
        [Tooltip("Scale when fully faded.")]
        [SerializeField] private float _endScale = 1f;
        [SerializeField] private Ease _ease = Ease.OutQuad;

        private CanvasGroup _group;
        private Vector3 _baseScale;
        private Sequence _sequence;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _baseScale = transform.localScale; // captured once, so restarts never compound
        }

        private void OnEnable()
        {
            // Start pose first, so a restart mid-ripple never begins from a half-faded frame.
            transform.localScale = _baseScale * _startScale;
            _group.alpha = 1f;

            _sequence = DOTween.Sequence()
                .Join(transform.DOScale(_baseScale * _endScale, _duration).SetEase(_ease))
                .Join(_group.DOFade(0f, _duration).SetEase(Ease.InQuad)) // stays visible early, drops late
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => gameObject.SetActive(false));
        }

        // SetLink kills on destroy, not on disable. A bare kill is safe here: OnEnable always rewrites
        // the start pose, and the base scale was captured once in Awake.
        private void OnDisable()
        {
            _sequence?.Kill();
            _sequence = null; // a stale handle could kill a recycled tween if a project enables recycling
        }
    }
}
