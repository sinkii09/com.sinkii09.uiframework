namespace Sinkii09.UIFramework
{
    // Preferred side of the anchor for the tooltip. Any choice is only a preference: the
    // positioner flips to the opposite side when the preferred one would overflow the tooltip
    // layer, then clamps inside it regardless.
    //
    // Auto = Above, falling back to Below. That order matches pointer-driven UI, where the cursor
    // sits below/over the anchor and a tooltip drawn upward stays clear of it.
    public enum TooltipPlacement { Auto, Above, Below, Left, Right }

    // Which input raised the tooltip. The service branches on this: Hover and Touch wait out a
    // delay, while Click and Focus show instantly because both are deliberate acts where any
    // delay reads as lag.
    public enum TooltipSource { Hover, Click, Focus, Touch }
}
