using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Shared base-scale capture for the juice components. Every one of them restores to a CAPTURED
    // base rather than Vector3.one, so a control authored at another scale is never normalised by
    // its first punch. The near-zero guard exists because a captured 0 would make every later
    // restore hide the object permanently — the usual cause is a second script (a pop-in, a
    // transition) driving the same transform's scale, which breaks the one-driver-per-transform rule.
    internal static class JuiceScale
    {
        private const float MinAxis = 0.01f;

        public static Vector3 CaptureBase(Transform target, Object context)
        {
            var scale = target.localScale;
            if (Mathf.Abs(scale.x) >= MinAxis && Mathf.Abs(scale.y) >= MinAxis) return scale;

            Debug.LogWarning(
                $"[UIFramework] {context.GetType().Name} on '{context.name}' captured a near-zero base scale {scale}. " +
                "Another script is driving this transform's scale — put the feedback on a child visual. Falling back to Vector3.one.",
                context);
            return Vector3.one;
        }
    }
}
