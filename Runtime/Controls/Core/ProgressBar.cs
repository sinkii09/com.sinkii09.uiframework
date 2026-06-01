using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    public class ProgressBar : UIControlBase
    {
        [SerializeField] private Image _fill;
        [SerializeField] private TextMeshProUGUI _label; // optional percentage label

        private float _value;

        // 0..1 normalised value
        public float Value
        {
            get => _value;
            set { _value = Mathf.Clamp01(value); Refresh(); }
        }

        protected override void OnInitialize() => Refresh();
        protected override void OnDispose() { }

        private void Refresh()
        {
            if (_fill != null) _fill.fillAmount = _value;
            if (_label != null) _label.text = $"{Mathf.RoundToInt(_value * 100)}%";
        }
    }
}
