namespace Sinkii09.UIFramework
{
    // Canvas sort-order layers for UI views. UIViewBase.Layer returns Screen by default.
    // UIViewFactory uses this to reparent views under the correct UIRootLayerRefs transform.
    public enum UILayer { HUD, Screen, Popup, Overlay, Debug }
}
