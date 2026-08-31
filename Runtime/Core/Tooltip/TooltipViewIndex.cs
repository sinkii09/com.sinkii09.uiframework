using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Key -> tooltip view lookup, plus the DI wiring those views would otherwise never get.
    //
    // Tooltip views are resident scene objects, not factory-created, so nothing injects them by
    // default: a view that registered itself would never receive UIViewBase's
    // [Inject] Construct(IUIAnimator) and its show/hide transitions would silently never play.
    // Every view added here is injected first, which is the whole reason this type exists.
    internal sealed class TooltipViewIndex
    {
        // The built-in TooltipView registers under the empty key; a null ViewKey resolves to it.
        private const string DefaultKey = "";

        private readonly Dictionary<string, TooltipViewBase> _views = new();
        private readonly HashSet<string> _missingKeysLogged = new();
        private readonly IObjectResolver _resolver;

        public TooltipViewIndex(IObjectResolver resolver) => _resolver = resolver;

        public int Count => _views.Count;

        // One-shot scene sweep, run from TooltipService.Initialize (an entry point, so the
        // container is fully built by then). FindObjectsInactive.Include matters: tooltip views
        // pre-hide themselves in Awake, so by this point they are already inactive.
        public void DiscoverSceneViews()
        {
            // No FindObjectsSortMode overload — Unity 6.4 deprecated that parameter outright.
            foreach (var view in Object.FindObjectsByType<TooltipViewBase>(FindObjectsInactive.Include))
                Add(view);
        }

        public void Add(TooltipViewBase view)
        {
            if (view == null) return;

            string key = Normalize(view.ViewKey);
            if (_views.TryGetValue(key, out var existing) && existing != null)
            {
                if (existing == view) return;   // already indexed; injecting twice is not safe
                Debug.LogError(
                    $"[TooltipService] Two tooltip views share the key '{key}': " +
                    $"'{existing.name}' and '{view.name}'. Keeping the first; give one a distinct " +
                    "ViewKey.", view);
                return;
            }

            // Runs [Inject] Construct on the view. NOT idempotent — a view also listed in the
            // scope's Auto Inject GameObjects would receive Construct twice, which is why
            // Construct on these views must stay assignment-only.
            _resolver.InjectGameObject(view.gameObject);
            _views[key] = view;
        }

        public TooltipViewBase Resolve(string viewKey)
        {
            string key = Normalize(viewKey);

            // Unity fake-null: a scene view destroyed after indexing leaves a live dictionary entry.
            if (_views.TryGetValue(key, out var view) && view != null)
                return view;

            // Logged once per key: Resolve runs on every show, so an unmatched key on a hovered
            // grid would otherwise produce an error per pointer-enter.
            if (_missingKeysLogged.Add(key))
            {
                Debug.LogError(
                    key == DefaultKey
                        ? "[TooltipService] No built-in TooltipView found under the UIRoot's Tooltip layer."
                        : $"[TooltipService] No tooltip view registered under key '{key}'.");
            }
            return null;
        }

        private static string Normalize(string key) => string.IsNullOrEmpty(key) ? DefaultKey : key;
    }
}
