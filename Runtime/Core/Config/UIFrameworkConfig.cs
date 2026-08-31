using UnityEngine;

namespace Sinkii09.UIFramework
{
    public enum LoaderMode { Resources, Addressables }

    [CreateAssetMenu(menuName = "UIFramework/Config", fileName = "UIFrameworkConfig")]
    public class UIFrameworkConfig : ScriptableObject
    {
        // Default = Resources; switch to Addressables after installing the Addressables package.
        public LoaderMode LoaderMode = LoaderMode.Resources;
        public int MaxNavigationDepth = 10;

        // How long a cached view must sit unused before UIViewCacheSweeper destroys it and
        // releases its loader handle.
        //
        // 0 DISABLES eviction entirely — no sweeper is registered and the cache grows for the
        // session, which is the historical behaviour. (Careful: 0 passed directly to
        // UIViewFactory.SweepAsync means the opposite — "evict everything eligible now". The
        // enable check lives in UIFrameworkLifetimeScope and must not be refactored away.)
        //
        // Pin views that should survive regardless via UIViewPolicyConfig (Resident).
        [Min(0f)]
        [Tooltip("Seconds a cached view may sit unused before being destroyed. 0 disables eviction entirely.")]
        public float ViewCacheGraceSeconds = 0f;

        // [Min] guards the Inspector; the sweeper clamps again because a config built in code
        // (ScriptableObject.CreateInstance, as the LifetimeScope's fallback does) bypasses it,
        // and 0 here would mean sweeping every frame.
        [Min(0.1f)]
        [Tooltip("How often the eviction sweep runs. Ignored when ViewCacheGraceSeconds is 0.")]
        public float ViewCacheSweepIntervalSeconds = 5f;

        // One global colour, not per-view: a project wants a consistent dim, and per-view colours
        // were cut as speculative. Only applies to views marked NeedsBackdrop in UIViewPolicyConfig.
        [Tooltip("Dim colour drawn behind views whose policy sets NeedsBackdrop.")]
        public Color BackdropColor = new Color(0f, 0f, 0f, 0.5f);

        // --- Tooltip timing ---------------------------------------------------------------
        // All four are consumed by TooltipService, which copies them into internal fields at
        // construction so tests can drive the state machine at zero delay. Every wait derived
        // from them uses DelayType.UnscaledDeltaTime — this project pauses with timeScale = 0,
        // and a scaled delay would mean no tooltip ever appears in a pause menu.

        [Min(0f)]
        [Tooltip("Hover dwell before a tooltip appears. Click and focus bypass this — both are " +
                 "deliberate acts, where a delay reads as lag.")]
        public float TooltipShowDelaySeconds = 0.5f;

        [Min(0f)]
        [Tooltip("Grace period after the pointer leaves before the tooltip actually hides.")]
        public float TooltipHideGraceSeconds = 0.1f;

        [Min(0f)]
        [Tooltip("While shown or in grace, a new target shows instantly for this long instead of " +
                 "re-waiting the show delay. This is what makes sweeping across a grid feel responsive.")]
        public float TooltipReShowWindowSeconds = 0.3f;

        [Min(0f)]
        [Tooltip("Touch press duration that counts as a long-press.")]
        public float TooltipLongPressSeconds = 0.5f;

        [Min(0f)]
        [Tooltip("Pointer travel that cancels an in-progress long-press, so a scroll gesture " +
                 "starting on a cell does not fire a tooltip.")]
        public float TooltipLongPressMoveCancelPixels = 10f;
    }
}
