using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    [RequireComponent(typeof(Button))]
    public class TabButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private GameObject _selectedIndicator;

        private Button _button;
        private int _index;

        private readonly Subject<int> _onClicked = new();
        public Observable<int> OnClicked => _onClicked;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnButtonClick);
        }

        private void OnDestroy()
        {
            _button?.onClick.RemoveListener(OnButtonClick);
            _onClicked.Dispose();
        }

        private void OnButtonClick() => _onClicked.OnNext(_index);

        public void Setup(int index, string label = null)
        {
            _index = index;
            if (!string.IsNullOrEmpty(label) && _label != null)
                _label.text = label;
        }

        public void SetSelected(bool selected)
        {
            if (_selectedIndicator != null)
                _selectedIndicator.SetActive(selected);
        }
    }
}
