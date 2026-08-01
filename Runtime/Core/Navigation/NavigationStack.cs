using System;
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
        public IReadOnlyList<IUIView> All => _views;

        public NavigationStack(UIFrameworkConfig config) =>
            _maxDepth = config != null ? config.MaxNavigationDepth : 10;

        public async UniTask PushAsync(IUIView view, CancellationToken ct = default)
        {
            if (_views.Count >= _maxDepth)
            {
                Debug.LogWarning($"[NavigationStack] Max depth {_maxDepth} reached. Push of {view.ViewId} ignored.");
                return;
            }
            // Add AFTER ShowAsync succeeds — if cancelled mid-show the view never enters the stack,
            // preventing phantom entries that PopAsync would later try to hide.
            try
            {
                await view.ShowAsync(ct);
                _views.Add(view);
            }
            catch
            {
                // ShowAsync threw (including OperationCanceledException) — view was never shown;
                // do not add to stack. Re-throw so UINavigator knows the push failed. Contract now
                // enforced by UIViewBase.ShowAsync, which rethrows OperationCanceledException
                // instead of swallowing it — previously this catch documented a contract the view
                // layer silently violated.
                throw;
            }
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
            // catch + continue: a non-cancellation exception from one HideAsync must not abort
            // the loop — remaining views would stay visible with the stack partially cleared.
            for (int i = _views.Count - 1; i >= 0; i--)
            {
                try { await _views[i].HideAsync(ct); }
                catch (Exception e) { Debug.LogException(e); }
                _views.RemoveAt(i);
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
