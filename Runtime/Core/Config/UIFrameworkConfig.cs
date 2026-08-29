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
    }
}
