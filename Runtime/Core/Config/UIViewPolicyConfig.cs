using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Per-view declarative policy. Entries are keyed by the view's LOAD KEY (the
    // [UIViewKey] value, or the view class name when the attribute is absent) because a
    // ScriptableObject cannot serialize a System.Type. UIViewPolicyResolver does the
    // Type -> key -> policy lookup via UIViewKeys.For.
    //
    // A view with no entry gets UIViewPolicy.Default (everything off), so adding this asset
    // to a project changes nothing until entries are authored.
    [Serializable]
    public struct UIViewPolicy
    {
        [Tooltip("Never evict this view from the factory cache. Use for views that are shown constantly (HUD, loading, connecting) where the reload cost outweighs the memory.")]
        public bool Resident;

        [Tooltip("Draw a dimming backdrop behind this view while it is the top of the navigation stack.")]
        public bool NeedsBackdrop;

        [Tooltip("Warm this view during boot so its first ShowAsync doesn't pay load + instantiate + injection mid-gameplay. Implies Resident.")]
        public bool PreloadOnBoot;

        public static UIViewPolicy Default => default;

        // Preloading a view and then letting the sweeper evict it is strictly wasted work —
        // the next show pays the full load cost the preload was meant to avoid. Rather than
        // make callers remember to tick both boxes, PreloadOnBoot implies residency.
        // "Preloaded but evictable" is still reachable: don't set PreloadOnBoot, and let the
        // view populate the cache on first show like any other.
        public bool IsResident => Resident || PreloadOnBoot;
    }

    [Serializable]
    public class UIViewPolicyEntry
    {
        [Tooltip("The view's [UIViewKey] value, or its class name if it has no [UIViewKey] attribute.")]
        public string ViewKey;

        public UIViewPolicy Policy;
    }

    // INSPECTOR-ASSIGNED ONLY. Unlike UIFrameworkConfig there is deliberately no
    // Resources.Load fallback — that path is already untested in consuming projects (none of
    // them keep a UIFrameworkConfig in a Resources folder), and a second silently-inert
    // lookup path is not worth adding. Assign this on the UIFrameworkLifetimeScope component;
    // leaving it empty is valid and means "framework defaults for every view".
    [CreateAssetMenu(menuName = "UIFramework/View Policy Config", fileName = "UIViewPolicyConfig")]
    public class UIViewPolicyConfig : ScriptableObject
    {
        public List<UIViewPolicyEntry> Entries = new();
    }
}
