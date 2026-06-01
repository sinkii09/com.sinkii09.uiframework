using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public class TabView : UIControlBase
    {
        [SerializeField] private List<TabButton> _tabs = new();
        [SerializeField] private List<GameObject> _panels = new();
        [SerializeField] private TabIndicator _indicator;

        private readonly ReactiveProperty<int> _selectedIndex = new(0);
        public ReadOnlyReactiveProperty<int> SelectedIndex => _selectedIndex;

        protected override void OnInitialize()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Setup(i);
                _tabs[i].OnClicked
                    .Subscribe(Select)
                    .AddTo(ref _disposables);
            }
            if (_tabs.Count > 0) Select(0);
        }

        protected override void OnDispose() => _selectedIndex.Dispose();

        public void Select(int index)
        {
            if (index < 0 || index >= _tabs.Count || _tabs[index] == null) return;
            _selectedIndex.Value = index;

            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].SetSelected(i == index);

            for (int i = 0; i < _panels.Count; i++)
                _panels[i].SetActive(i == index);

            if (_indicator != null && index < _tabs.Count)
                _indicator.MoveTo(_tabs[index].GetComponent<RectTransform>());
        }
    }
}
