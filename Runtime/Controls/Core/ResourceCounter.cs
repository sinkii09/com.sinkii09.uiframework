using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    [RequireComponent(typeof(Button))]
    public class ResourceCounter : UIControlBase
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private Button _button;

        private readonly Subject<Unit> _onClicked = new();
        public Observable<Unit> OnClickedAsObservable => _onClicked;

        // IntFraction is the original "current/max" readout and stays the default, byte for byte.
        // The idle modes are reached only through SetAmount/SetValues; setting the int Current or
        // Max switches back.
        private enum DisplayMode { IntFraction, IdleAmount, IdleFraction }

        private DisplayMode _mode;
        private int _current;
        private int _max;
        private double _idleCurrent;
        private double _idleMax;

        public int Current
        {
            get => _current;
            set { _current = Mathf.Max(0, value); _mode = DisplayMode.IntFraction; Refresh(); }
        }

        public int Max
        {
            get => _max;
            set { _max = Mathf.Max(0, value); _mode = DisplayMode.IntFraction; Refresh(); }
        }

        /// <summary>Shows a single idle-formatted amount ("1.23M"), with no max.</summary>
        /// <remarks>Does not change the int <see cref="Current"/>/<see cref="Max"/> values.</remarks>
        public void SetAmount(double amount)
        {
            _idleCurrent = ClampNonNegative(amount);
            _mode = DisplayMode.IdleAmount;
            Refresh();
        }

        /// <summary>Shows an idle-formatted "current/max" pair ("1.20K/5.00K").</summary>
        /// <remarks>
        /// Each value is clamped to >= 0 on its own; current is capped by max only when displayed,
        /// for the same call-order reason as the int properties.
        /// </remarks>
        public void SetValues(double current, double max)
        {
            _idleCurrent = ClampNonNegative(current);
            _idleMax = ClampNonNegative(max);
            _mode = DisplayMode.IdleFraction;
            Refresh();
        }

        public void SetIcon(Sprite icon)
        {
            if (_icon == null) return;
            _icon.sprite = icon;
            _icon.gameObject.SetActive(icon != null);
        }

        protected override void OnInitialize()
        {
            if (_button == null)
                _button = GetComponent<Button>();
            _button.onClick.AddListener(OnButtonClick);
            Refresh();
        }

        protected override void OnDispose()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnButtonClick);
            _onClicked.Dispose();
        }

        private void OnButtonClick() => _onClicked.OnNext(Unit.Default);

        private void Refresh()
        {
            if (_label == null) return;

            switch (_mode)
            {
                case DisplayMode.IdleAmount:
                    _label.text = UIFormatUtils.FormatIdle(_idleCurrent);
                    break;
                case DisplayMode.IdleFraction:
                    _label.text = UIFormatUtils.FormatIdle(Math.Min(_idleCurrent, _idleMax)) + "/" +
                                  UIFormatUtils.FormatIdle(_idleMax);
                    break;
                default:
                    _label.text = $"{Mathf.Min(_current, _max)}/{_max}";
                    break;
            }
        }

        // Math.Max(0d, NaN) is NaN, so NaN is checked explicitly and shown as 0.
        private static double ClampNonNegative(double value) => double.IsNaN(value) ? 0d : Math.Max(0d, value);
    }
}
