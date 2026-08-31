namespace Sinkii09.UIFramework
{
    // Canvas sort-order layers for UI views. UIViewBase.Layer returns Screen by default.
    // UIViewFactory uses this to reparent views under the correct UIRootLayerRefs transform.
    //
    // ORDINAL IS LOAD-BEARING: UIRootLayerRefs.BlockLayersBelow compares (int)layer, so the
    // declaration order is the visual-priority order and inserting a member renumbers everything
    // after it. That is safe only because no UILayer value is ever serialized — every use in the
    // framework and in consuming projects is a code-level `override Layer => UILayer.X`. Verify
    // that still holds (no [SerializeField] UILayer, no UILayer inside a [Serializable] type, no
    // UILayer key in .asset/.prefab YAML) before inserting another one.
    //
    // Tooltip sits above Popup so tooltips work inside modal dialogs, and below Overlay so they
    // can never draw over a loading curtain.
    public enum UILayer { HUD, Screen, Popup, Tooltip, Overlay, Debug }
}
