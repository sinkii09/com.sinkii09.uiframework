using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    // Inspector-assigned transforms for each canvas layer in the UIRoot prefab.
    // UIViewFactory uses this to reparent views under the correct layer on show.
    // UINavigator uses BlockLayersBelow/SetAllLayersInteractable to prevent click-through
    // between layers when a higher-priority view is active.
    [Serializable]
    public class UIRootLayerRefs
    {
        public Transform HUD;      // sortOrder: 0
        public Transform Screen;   // sortOrder: 100  (NavigationStack default parent)
        public Transform Popup;    // sortOrder: 200
        public Transform Tooltip;  // sortOrder: 250  (resident tooltip host; null on pre-v1.7 UIRoots)
        public Transform Overlay;  // sortOrder: 300  (LoadingView, fullscreen overlays)
        public Transform Debug;    // sortOrder: 400  (set inactive in release builds)

        public Transform GetLayer(UILayer layer) => layer switch
        {
            UILayer.HUD     => HUD,
            UILayer.Screen  => Screen,
            UILayer.Popup   => Popup,
            UILayer.Tooltip => Tooltip,
            UILayer.Overlay => Overlay,
            UILayer.Debug   => Debug,
            _               => Screen,
        };

        // Enables raycasters on topLayer and all layers above it; disables everything below.
        // UILayer enum values increase with visual priority, so (int)layer comparisons are correct.
        public void BlockLayersBelow(UILayer topLayer)
        {
            foreach (UILayer layer in (UILayer[])Enum.GetValues(typeof(UILayer)))
                SetLayerInteractable(layer, (int)layer >= (int)topLayer);
        }

        public void SetAllLayersInteractable(bool interactable)
        {
            foreach (UILayer layer in (UILayer[])Enum.GetValues(typeof(UILayer)))
                SetLayerInteractable(layer, interactable);
        }

        private void SetLayerInteractable(UILayer layer, bool interactable)
        {
            var t = GetLayer(layer);
            if (t == null) return;
            var raycaster = t.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UIRootLayerRefs] No GraphicRaycaster on '{t.name}' ({layer}) — attach one to the layer canvas.");
                return;
            }
            raycaster.enabled = interactable;
        }
    }
}
