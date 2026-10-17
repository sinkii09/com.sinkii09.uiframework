using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// A number label that rolls to its new value instead of jumping. Bigger changes roll longer,
    /// within a clamped window, and an optional <see cref="PunchFeedback"/> pulses on increases.
    /// </summary>
    /// <remarks>
    /// Must be the label's ONLY writer — do not also bind the same TMP_Text with BindToText.
    /// A value set while inactive, or the very first value, is applied instantly: R3 emits the
    /// current value on Subscribe, and rolling up from 0 on every show reads as a bug.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RollingNumberLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private string _format = "N0";
        [SerializeField] private float _minDuration = 0.15f;
        [SerializeField] private float _maxDuration = 0.8f;
        [SerializeField] private bool _useUnscaledTime = true;
        [SerializeField] private PunchFeedback _pulseOnIncrease;

        // Deltas of this magnitude or more use _maxDuration; smaller ones scale down on a log curve
        // so +1 is a quick tick and +10,000 is a satisfying roll.
        private const float LogDeltaForMaxDuration = 4f;

        private long _target;
        private long _shown;
        private bool _hasShown;

        public long Value => _target;
        public long DisplayedValue => _shown;
        public bool IsRolling => DOTween.IsTweening(this);

        // Resolved lazily too: SetValue can arrive before Awake when the label starts inactive in a
        // prefab, and a value "shown" with no label would be swallowed by the same-value early return.
        private void Awake() => ResolveLabel();

        private void ResolveLabel()
        {
            if (_label == null) _label = GetComponent<TMP_Text>();
        }

        public void SetValue(long target, bool animate = true)
        {
            _target = target;
            DOTween.Kill(this);

            if (!animate || !_hasShown || !isActiveAndEnabled)
            {
                Show(target);
                return;
            }
            if (target == _shown) return;

            // Roll from what is on screen, not from the previous target, so an interrupted roll
            // continues smoothly instead of jumping back.
            long from = _shown;
            float logDelta = (float)Math.Log10(Math.Abs((double)target - from) + 1d);
            float duration = Mathf.Lerp(_minDuration, _maxDuration, Mathf.Clamp01(logDelta / LogDeltaForMaxDuration));

            // Tween a 0..1 progress and interpolate in double: tweening the value itself as float
            // loses integer precision above 2^24 and would land on the wrong final number.
            // SetTarget(this) is what makes DOTween.Kill(this) reach a hand-rolled DOVirtual tween.
            DOVirtual.Float(0f, 1f, duration, t => Show(from + (long)Math.Round(((double)target - from) * t)))
                .SetEase(Ease.OutCubic)
                .SetUpdate(_useUnscaledTime)
                .SetTarget(this)
                .SetLink(gameObject)
                .OnComplete(() => Show(target));

            if (target > from && _pulseOnIncrease != null) _pulseOnIncrease.PunchScale();
        }

        // A roll interrupted by a hide must not resume mid-number on the next show.
        private void OnDisable()
        {
            DOTween.Kill(this);
            if (_hasShown) Show(_target);
        }

        // Reformats only when the integer actually changes — no per-frame string allocation.
        private void Show(long value)
        {
            if (_hasShown && value == _shown) return;

            ResolveLabel();
            _shown = value;
            _hasShown = true;
            if (_label != null) _label.SetText(value.ToString(_format));
        }
    }
}
