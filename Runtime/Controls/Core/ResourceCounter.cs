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

        private int _current;
        private int _max;

        public int Current
        {
            get => _current;
            set { _current = Mathf.Max(0, value); Refresh(); }
        }

        public int Max
        {
            get => _max;
            set { _max = Mathf.Max(0, value); Refresh(); }
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
            if (_label != null)
                _label.text = $"{Mathf.Min(_current, _max)}/{_max}";
        }
    }
}
