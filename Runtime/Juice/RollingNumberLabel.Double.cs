using System;
using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The double path: idle-game values formatted with UIFormatUtils.FormatIdle ("1.23M"). Kept apart
    // from the long path so neither changes the other's behaviour — the long path still formats with
    // _format and stays exact up to long.MaxValue.
    public sealed partial class RollingNumberLabel
    {
        // A roll of this many decades (x10) or more uses _maxDuration. Idle values grow by ratios, so
        // the double path scales by relative change: +10% is a quick tick, x10 a full roll. The long
        // path's absolute delta would run every tick past 10,000 at max duration.
        private const float DecadesForMaxDuration = 1f;

        private double _targetDouble;
        private double _shownDouble;
        private long _shownIdleKey;

        public double ValueDouble => _doubleMode ? _targetDouble : _target;
        public double DisplayedValueDouble => _doubleMode ? _shownDouble : _shown;

        /// <summary>Rolls to <paramref name="target"/>, shown in idle notation ("1.23M").</summary>
        /// <remarks>
        /// int and long arguments bind to the long overload; ulong, float and double bind here.
        /// Switching between the two overloads shows the new value instantly instead of rolling
        /// across formats. Infinity and NaN are shown instantly too.
        /// </remarks>
        public void SetValue(double target, bool animate = true)
        {
            _targetDouble = target;
            var switchingMode = !_doubleMode;
            _doubleMode = true;
            DOTween.Kill(this);

            if (!animate || !_hasShown || switchingMode || !isActiveAndEnabled ||
                !IsFinite(target) || !IsFinite(_shownDouble))
            {
                ShowDouble(target);
                return;
            }
            if (target == _shownDouble) return;

            var from = _shownDouble;
            var duration = Mathf.Lerp(_minDuration, _maxDuration, RollProgressForDuration(from, target));

            // Weighted form, not from + (target - from) * t: the difference overflows to infinity for
            // ends near opposite extremes, and infinity * 0 is NaN on the first frame.
            DOVirtual.Float(0f, 1f, duration, t => ShowDouble(from * (1d - t) + target * t))
                .SetEase(Ease.OutCubic)
                .SetUpdate(_useUnscaledTime)
                .SetTarget(this)
                .SetLink(gameObject)
                .OnComplete(() => ShowDouble(target));

            if (target > from && _pulseOnIncrease != null) _pulseOnIncrease.PunchScale();
        }

        // 0..1. Relative change when both ends are positive; otherwise (rolling to or from zero, or
        // across the sign) the long path's absolute formula — the log of a ratio there is NaN or
        // infinite, and Mathf.Clamp01(NaN) is NaN, which would reach DOTween as a duration.
        private static float RollProgressForDuration(double from, double target)
        {
            if (from > 0d && target > 0d)
                return Mathf.Clamp01((float)(Math.Abs(Math.Log10(target / from)) / DecadesForMaxDuration));

            var logDelta = Math.Log10(Math.Abs(target - from) + 1d);
            return Mathf.Clamp01((float)(logDelta / LogDeltaForMaxDuration));
        }

        // Reformats only when the visible text would change; the idle key is a cheap stand-in for
        // comparing strings.
        private void ShowDouble(double value)
        {
            _shownDouble = value;
            var key = UIFormatUtils.GetIdleKey(value);
            if (_hasShown && _renderedDouble && key == _shownIdleKey) return;

            ResolveLabel();
            _shownIdleKey = key;
            _hasShown = true;
            _renderedDouble = true;
            if (_label != null) _label.SetText(UIFormatUtils.FormatIdle(value));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        // (long)double is undefined outside the long range, so clamp first.
        private static long SaturateToLong(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value >= long.MaxValue) return long.MaxValue;
            if (value <= long.MinValue) return long.MinValue;
            return (long)value;
        }
    }
}
