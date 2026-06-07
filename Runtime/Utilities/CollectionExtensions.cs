using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class CollectionExtensions
    {
        // Null-safe empty check — avoids null-reference before Count checks.
        public static bool IsNullOrEmpty<T>(this IList<T> list) => list == null || list.Count == 0;

        // Return a random element. Guard against empty lists returns default.
        public static T Random<T>(this IList<T> list)
        {
            if (list.IsNullOrEmpty()) return default;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        // Fisher-Yates in-place shuffle using Unity's RNG.
        public static void Shuffle<T>(this IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Return a shuffled copy without modifying the original.
        public static List<T> ShuffledCopy<T>(this IList<T> list)
        {
            var copy = new List<T>(list);
            copy.Shuffle();
            return copy;
        }

        // Safe index access — returns default instead of throwing on out-of-range.
        public static T GetOrDefault<T>(this IList<T> list, int index)
            => index >= 0 && index < list.Count ? list[index] : default;

        // Remove and return the last element (stack-pop behaviour on a List).
        public static T Pop<T>(this List<T> list)
        {
            int last = list.Count - 1;
            T value = list[last];
            list.RemoveAt(last);
            return value;
        }
    }
}
