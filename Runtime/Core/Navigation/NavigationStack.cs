using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public sealed class NavigationStack : INavigationStack
    {
        private readonly List<IUIView> _views = new();
        private readonly int _maxDepth;

        public int Count => _views.Count;

        public NavigationStack(int maxDepth = 10) => _maxDepth = maxDepth;

        public async UniTask PushAsync(IUIView view, CancellationToken ct = default)
        {
            if (_views.Count >= _maxDepth)
            {
                Debug.LogWarning($"[NavigationStack] Max depth {_maxDepth} reached. Push of {view.ViewId} ignored.");
                return;
            }
            _views.Add(view);
            await view.ShowAsync(ct);
        }

        public async UniTask<IUIView> PopAsync(CancellationToken ct = default)
        {
            if (_views.Count == 0)
            {
                Debug.LogWarning("[NavigationStack] PopAsync on empty stack.");
                return null;
            }
            var view = _views[_views.Count - 1];
            // Hide first, then remove — if HideAsync throws/cancels the view stays in the stack
            // and on screen, keeping both in a consistent visible state.
            // UINavigator._isTransitioning prevents concurrent pushes during this await.
            await view.HideAsync(ct);
            _views.RemoveAt(_views.Count - 1);
            return view;
        }

        public IUIView Peek() => _views.Count > 0 ? _views[_views.Count - 1] : null;

        public async UniTask ClearAsync(CancellationToken ct = default)
        {
            // try/finally ensures each view is removed even if HideAsync throws or ct fires,
            // so stack count stays consistent with actual visible views
            for (int i = _views.Count - 1; i >= 0; i--)
            {
                try { await _views[i].HideAsync(ct); }
                finally { _views.RemoveAt(i); }
            }
        }

        // Concrete-only — not on INavigationStack. UINavigator does not call this through the interface.
        public bool Contains<T>() where T : IUIView
        {
            for (int i = 0; i < _views.Count; i++)
                if (_views[i] is T) return true;
            return false;
        }
    }
}
