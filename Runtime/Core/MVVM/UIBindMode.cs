namespace Sinkii09.UIFramework
{
    // Per-binding override for how a one-way binding delivers values to the UI.
    //
    // The default differs per binding method and is chosen on one rule: DISPLAY paths coalesce,
    // INPUT paths do not. A coalesced write lands up to one frame later, which is invisible for a
    // score label but a correctness bug for anything that gates interaction — a late
    // SetActive(false) leaves an object raycastable, a late interactable=false leaves a button
    // clickable, and a late write into an input field can overwrite what the user just typed.
    //
    // See UIBindingExtensions for the per-method defaults and the reasoning behind each.
    public enum UIBindMode
    {
        // At most one write per rendered frame, carrying the newest value. The FIRST value is
        // still applied synchronously, so a view never displays a frame of unbound state.
        // Intermediate values within a frame are DROPPED — never use this for a setter that
        // accumulates or has side effects.
        Coalesced,

        // Every value is written the moment it arrives, exactly as the framework behaved before
        // v3.0.0. Also the automatic fallback whenever no scheduler is installed.
        Immediate
    }
}
