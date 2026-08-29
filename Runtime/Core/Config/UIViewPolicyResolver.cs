using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Resolves a view Type to its declared UIViewPolicy, bridging the two identities the
    // framework uses: consumers ask by Type, the config declares by load-key string.
    //
    // Always registered, even when no config asset is assigned — VContainer does NOT honour
    // C# optional-parameter defaults (ResolveOrParameter never reads ParameterInfo.HasDefault
    // Value; it falls through to Resolve(type) and throws when unregistered). A conditionally
    // registered resolver would therefore break construction of everything that takes one.
    // With a null config this behaves as a null-object: every view gets UIViewPolicy.Default.
    //
    // MAIN THREAD ONLY — Get() lazily writes its memo dictionary with no synchronisation.
    // Every caller today (container build, navigator refresh, the eviction sweep's UniTask
    // loop on PlayerLoopTiming.Update) runs on the main thread. Keep it that way.
    //
    // Policy is SNAPSHOT at construction: editing the asset's Entries at runtime has no effect
    // until the container is rebuilt. That is the safer semantic (no mid-session behaviour
    // change from an Inspector edit) and is why Entries stays a plain serialized list.
    public sealed class UIViewPolicyResolver
    {
        private readonly Dictionary<string, UIViewPolicy> _byKey;
        private readonly Dictionary<Type, UIViewPolicy> _byType = new();

        public UIViewPolicyResolver(UIViewPolicyConfig config)
        {
            _byKey = BuildLookup(config);
        }

        public UIViewPolicy Get(Type viewType)
        {
            if (viewType == null) return UIViewPolicy.Default;

            // Memoised: the sweeper asks per cached view on every pass, and UIViewKeys.For
            // walks the attribute table each call.
            if (_byType.TryGetValue(viewType, out var cached)) return cached;

            _byKey.TryGetValue(UIViewKeys.For(viewType), out var policy);
            _byType[viewType] = policy;
            return policy;
        }

        public bool IsResident(Type viewType) => Get(viewType).IsResident;

        public bool NeedsBackdrop(Type viewType) => Get(viewType).NeedsBackdrop;

        public IReadOnlyList<UIViewRegistration> PreloadSet(IReadOnlyList<UIViewRegistration> all)
        {
            var result = new List<UIViewRegistration>();
            if (all == null) return result;

            for (int i = 0; i < all.Count; i++)
            {
                if (Get(all[i].ViewType).PreloadOnBoot) result.Add(all[i]);
            }
            return result;
        }

        // A policy key that matches no registered view is silently inert — the most likely
        // cause is a typo or a renamed view class, and the symptom (a view that quietly stopped
        // being resident) is far removed from the cause. Called once at boot.
        internal void ValidateAgainst(IReadOnlyList<UIViewRegistration> registrations)
        {
            if (_byKey.Count == 0 || registrations == null) return;

            var known = new HashSet<string>();
            for (int i = 0; i < registrations.Count; i++) known.Add(registrations[i].Key);

            foreach (var key in _byKey.Keys)
            {
                if (!known.Contains(key))
                {
                    Debug.LogWarning($"[UIViewPolicyConfig] Policy declared for \"{key}\", which matches no " +
                                     "registered view. Renamed or mistyped? This entry has no effect.");
                }
            }
        }

        private static Dictionary<string, UIViewPolicy> BuildLookup(UIViewPolicyConfig config)
        {
            var lookup = new Dictionary<string, UIViewPolicy>();

            // `config == null` (Unity's overloaded operator), NOT `config?.` — the null-conditional
            // operator tests reference null and does not see Unity's "fake null". A destroyed or
            // missing-reference SO would pass `?.` and then throw MissingReferenceException on
            // .Entries — inside Configure(), which fails the whole container build.
            if (config == null || config.Entries == null) return lookup;

            for (int i = 0; i < config.Entries.Count; i++)
            {
                var entry = config.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.ViewKey)) continue;

                if (!lookup.TryAdd(entry.ViewKey, entry.Policy))
                {
                    Debug.LogError($"[UIViewPolicyConfig] Duplicate policy entry for \"{entry.ViewKey}\". " +
                                   "The first entry wins; the duplicate is ignored.");
                }
            }
            return lookup;
        }
    }
}
