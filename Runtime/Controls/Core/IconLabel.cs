using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    public class IconLabel : UIControlBase
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _label;

        public void Set(Sprite icon, string text)
        {
            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.gameObject.SetActive(icon != null);
            }
            if (_label != null)
                _label.text = text;
        }

        protected override void OnInitialize() { }
        protected override void OnDispose() { }
    }
}
