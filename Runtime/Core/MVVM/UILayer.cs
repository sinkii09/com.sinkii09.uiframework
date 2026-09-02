namespace Sinkii09.UIFramework
{
    // Canvas sort-order layers for UI views. UIViewBase.Layer returns Screen by default.
    // UIViewFactory uses this to reparent views under the correct UIRootLayerRefs transform.
    //
    // VALUES ARE EXPLICIT AND SPACED so that inserting a layer never renumbers the others. Before
    // v2.2.0 these were implicit ordinals and every insert was a breaking renumber, tolerable only
    // because no UILayer value is ever serialized. The spacing removes that hazard permanently:
    // UIRootLayerRefs.BlockLayersBelow compares (int)layer relatively and iterates Enum.GetValues,
    // so gaps change nothing.
    //
    // The numbers happen to match each layer's canvas sortOrder, which is set from the layer tables
    // in UIFrameworkUIRootUpgrader and UIFrameworkInstallerWizardSteps. That is alignment for
    // readability ONLY — nothing reads (int)UILayer as a sortOrder, and nothing should start to.
    //
    // Still true, and still worth re-checking before adding a member: no UILayer value is
    // serialized anywhere (no [SerializeField] UILayer, no UILayer inside a [Serializable] type, no
    // UILayer key in .asset/.prefab YAML). Every use is a code-level `override Layer => UILayer.X`.
    //
    // Tooltip sits above Popup so tooltips work inside modal dialogs. Notification sits above
    // Tooltip, so a transient report is not occluded by a hover tooltip, and below Overlay so
    // neither can ever draw over a loading curtain.
    public enum UILayer
    {
        HUD = 0,
        Screen = 100,
        Popup = 200,
        Tooltip = 250,
        Notification = 275,
        Overlay = 300,
        Debug = 400,
    }
}
