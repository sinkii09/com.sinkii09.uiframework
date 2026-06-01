using TMPro;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public class Badge : UIControlBase
    {
        [SerializeField] private TextMeshProUGUI _label;

        private int _count;

        public int Count
        {
            get => _count;
            set { _count = value; Refresh(); }
        }

        protected override void OnInitialize() => Refresh();
        protected override void OnDispose() { }

        private void Refresh()
        {
            gameObject.SetActive(_count > 0);
            if (_label != null)
                _label.text = _count > 99 ? "99+" : _count.ToString();
        }
    }
}
