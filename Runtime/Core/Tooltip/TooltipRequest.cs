using UnityEngine;

namespace Sinkii09.UIFramework
{
    // One request to show a tooltip. Passed by `in` — this is raised on every pointer enter across
    // a grid, so it stays a struct and allocates nothing.
    public readonly struct TooltipRequest
    {
        // The rect the tooltip is positioned against. Also the service's identity for this
        // tooltip: a Hide from any other anchor is a stale exit and is ignored.
        public readonly RectTransform Anchor;
        public readonly ITooltipPayload Payload;
        public readonly TooltipPlacement Placement;
        public readonly TooltipSource Source;

        public TooltipRequest(RectTransform anchor, ITooltipPayload payload,
            TooltipPlacement placement = TooltipPlacement.Auto,
            TooltipSource source = TooltipSource.Hover)
        {
            Anchor = anchor;
            Payload = payload;
            Placement = placement;
            Source = source;
        }

        // Unity fake-null: the anchor may have been destroyed between the trigger raising this and
        // the service consuming it, so `== null` (never `?.`) is the correct check.
        public bool IsValid => Anchor != null && Payload != null;
    }
}
