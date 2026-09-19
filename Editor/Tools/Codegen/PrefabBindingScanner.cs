using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Editor
{
    public sealed class ScanOptions
    {
        /// List components with no entry in the priority table (falls back to RectTransform-level noise).
        public bool ShowAllComponents;

        /// Off by default: a field generated from inside another prefab breaks silently when that
        /// prefab changes. The instance root is the stable boundary.
        public bool DescendIntoNestedPrefabs;
    }

    public sealed class ScannedCandidate
    {
        public BindingCandidate Binding;

        /// True for the highest-priority component on its child — the one ticked on a fresh scan.
        public bool DefaultTicked;

        /// Wizard state; seeded from DefaultTicked or from the previous generation's manifest.
        public bool Selected;
    }

    /// <summary>
    /// Walks a prefab hierarchy and proposes one candidate per (child, component) pair. Not pure —
    /// it traverses GameObjects — so the testable logic lives in BindingFieldNamer/BindingManifest
    /// and this type stays a thin, ordered traversal.
    /// </summary>
    public static class PrefabBindingScanner
    {
        // Decides which pair is default-ticked, not which pairs are listed. A single GameObject
        // legitimately needs two fields (CanvasGroup + Button, Image + Button), so the pair is the
        // unit of choice and this only orders them.
        private static readonly Type[] Priority =
        {
            typeof(TMP_InputField), typeof(TMP_Dropdown), typeof(TMP_Text),
            typeof(Button), typeof(Toggle), typeof(Slider), typeof(Scrollbar), typeof(ScrollRect),
            typeof(Selectable),
            typeof(Image), typeof(RawImage), typeof(CanvasGroup), typeof(Canvas), typeof(Animator),
        };

        /// <summary>
        /// Proposes a candidate per (child, component) pair.
        /// <paramref name="previous"/> is the previous generation's manifest, and when supplied it
        /// is the naming AUTHORITY: any pair it already named keeps that name forever. Without it,
        /// names are allocated first-come in traversal order, so inserting a child ahead of an
        /// existing one silently steals its field name — and because Unity serialises by field
        /// name, the old reference stays non-null while pointing at the wrong child, which no
        /// runtime validation can catch. Callers that have a manifest MUST pass it.
        /// </summary>
        public static List<ScannedCandidate> Scan(GameObject prefabRoot, ScanOptions options,
                                                  IReadOnlyList<BindingCandidate> previous = null)
        {
            var results = new List<ScannedCandidate>();
            if (prefabRoot == null) return results;

            options ??= new ScanOptions();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var pinned = PinPreviousNames(previous, taken);
            var usedPins = new HashSet<string>(StringComparer.Ordinal);

            // Depth-first in hierarchy order: stable across runs, which is what makes regeneration
            // produce a byte-identical file.
            foreach (var child in Descendants(prefabRoot.transform, prefabRoot.transform, options))
                AddCandidates(child, prefabRoot.transform, options, taken, pinned, usedPins, results);

            return results;
        }

        /// <summary>
        /// Reserves every name the previous run emitted before a single new one is allocated, so a
        /// newly discovered child can never be handed a name that already belongs to another pair.
        /// </summary>
        private static Dictionary<string, string> PinPreviousNames(IReadOnlyList<BindingCandidate> previous,
                                                                   HashSet<string> taken)
        {
            var pinned = new Dictionary<string, string>(StringComparer.Ordinal);
            if (previous == null) return pinned;

            foreach (var candidate in previous)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.FieldName)) continue;
                pinned[BindingManifest.PairKey(candidate)] = candidate.FieldName;
                taken.Add(candidate.FieldName);
            }

            return pinned;
        }

        private static IEnumerable<Transform> Descendants(Transform current, Transform root, ScanOptions options)
        {
            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                yield return child;

                // The root itself is a prefab instance root when loaded as an asset, so the check
                // only applies below it.
                if (!options.DescendIntoNestedPrefabs && IsNestedPrefabRoot(child)) continue;

                foreach (var nested in Descendants(child, root, options))
                    yield return nested;
            }
        }

        private static bool IsNestedPrefabRoot(Transform t)
        {
            // Behaviour on an asset-loaded prefab (rather than a scene instance) is verified
            // manually as part of this phase; a false negative only adds extra candidates the user
            // can untick, it cannot corrupt anything.
            try { return PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject); }
            catch (Exception) { return false; }
        }

        private static void AddCandidates(Transform child, Transform root, ScanOptions options,
                                          HashSet<string> taken, Dictionary<string, string> pinned,
                                          HashSet<string> usedPins, List<ScannedCandidate> results)
        {
            var ranked = child.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => (component: c, rank: RankOf(c)))
                .Where(x => x.rank < int.MaxValue || options.ShowAllComponents)
                .OrderBy(x => x.rank)
                .ThenBy(x => x.component.GetType().FullName, StringComparer.Ordinal)
                .ToList();

            var isFirst = true;
            var parentName = child.parent != null ? child.parent.name : string.Empty;
            var childPath = PathFrom(root, child);

            foreach (var (component, _) in ranked)
            {
                var type = component.GetType();
                var typeFullName = (type.FullName ?? type.Name).Replace('+', '.');

                // The top-ranked component owns the plain name ("_healthBar"); the rest are
                // type-qualified ("_healthBarCanvasGroup"). Falling back to the parent name here
                // would read as if the field belonged to a sibling.
                var seed = isFirst ? child.name : child.name + "-" + type.Name;

                // A name the previous run gave this exact pair wins over anything we would derive:
                // the name is already baked into the prefab's serialised data.
                // A pin may be claimed only ONCE: sibling GameObjects may share a name, so two
                // distinct pairs can produce the same (childPath, type) key. Handing both the pinned
                // name would emit two identical fields and make the generated file fail to compile.
                var pairKey = BindingManifest.PairKey(childPath, typeFullName);
                var fieldName = pinned.TryGetValue(pairKey, out var kept) && usedPins.Add(pairKey)
                    ? kept
                    : BindingFieldNamer.ReserveUniqueFieldName(seed, parentName, taken);

                results.Add(new ScannedCandidate
                {
                    Binding = new BindingCandidate
                    {
                        FieldName = fieldName,
                        ChildPath = childPath,
                        TypeFullName = typeFullName,
                        Component = component
                    },
                    DefaultTicked = isFirst,
                    Selected = isFirst
                });
                isFirst = false;
            }
        }

        private static int RankOf(Component c)
        {
            // Framework controls outrank everything: a ProgressBar child is more useful bound as a
            // ProgressBar than as the Image inside it.
            if (c is UIControlBase) return -1;

            for (var i = 0; i < Priority.Length; i++)
                if (Priority[i].IsInstanceOfType(c)) return i;

            return int.MaxValue;
        }

        private static string PathFrom(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (var t = child; t != null && t != root; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
