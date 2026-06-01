using R3;

namespace Sinkii09.UIFramework
{
    // Manages mutual exclusion for a set of toggle-style buttons.
    // -1 means nothing selected.
    public class ToggleGroup : UIControlBase
    {
        private readonly ReactiveProperty<int> _selectedIndex = new(-1);
        public ReadOnlyReactiveProperty<int> SelectedIndex => _selectedIndex;

        public void Select(int index) => _selectedIndex.Value = index;
        public void Deselect() => _selectedIndex.Value = -1;

        protected override void OnInitialize() { }
        protected override void OnDispose() => _selectedIndex.Dispose();
    }
}
