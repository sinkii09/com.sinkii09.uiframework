using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Default ITooltipService when no TooltipViewBase exists anywhere in the scene hierarchy.
    // Keeps UINavigator and every TooltipTrigger free of null-checks: a project that never places
    // a tooltip view behaves exactly as it did before this feature existed.
    public sealed class NullTooltipService : ITooltipService
    {
        public bool IsShown => false;
        public RectTransform CurrentAnchor => null;

        // Same defaults as UIFrameworkConfig, so a trigger's long-press arithmetic stays sane even
        // though nothing will ever be shown.
        public float LongPressSeconds => 0.5f;
        public float LongPressMoveCancelPixels => 10f;

        public void Show(in TooltipRequest request) { }
        public void Hide(RectTransform source) { }
        public void HideImmediate() { }
        public void Register(TooltipViewBase view) { }
    }
}
