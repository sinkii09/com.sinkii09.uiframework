using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Inspector-assigned transforms for each canvas layer in the UIRoot prefab.
    // UIViewFactory uses this to reparent views under the correct layer on show.
    [Serializable]
    public class UIRootLayerRefs
    {
        public Transform HUD;      // sortOrder: 0
        public Transform Screen;   // sortOrder: 100  (NavigationStack default parent)
        public Transform Popup;    // sortOrder: 200
        public Transform Overlay;  // sortOrder: 300  (LoadingView, fullscreen overlays)
        public Transform Debug;    // sortOrder: 400  (set inactive in release builds)

        public Transform GetLayer(UILayer layer) => layer switch
        {
            UILayer.HUD     => HUD,
            UILayer.Screen  => Screen,
            UILayer.Popup   => Popup,
            UILayer.Overlay => Overlay,
            UILayer.Debug   => Debug,
            _               => Screen,
        };
    }
}
