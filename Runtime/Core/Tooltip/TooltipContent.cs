using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // One labelled stat row in a TooltipContent — "Damage  12-18", "Cooldown  4s".
    public readonly struct TooltipStatLine
    {
        public readonly string Label;
        public readonly string Value;

        public TooltipStatLine(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    // The built-in composable-sections payload: the WoW/Diablo item-card model. Every section is
    // optional — TooltipView hides whatever is null or empty, so one prefab serves a bare
    // one-line hint and a full stat card without per-case variants.
    //
    // ViewKey is always null: this is what the built-in TooltipView renders. A project wanting a
    // different look supplies its own ITooltipPayload with a non-empty ViewKey and a matching
    // TooltipViewBase.
    public sealed class TooltipContent : ITooltipPayload
    {
        public string Title;
        public Sprite Icon;
        public string Body;
        public IReadOnlyList<TooltipStatLine> Stats;
        public string Footer;

        public string ViewKey => null;
    }
}
