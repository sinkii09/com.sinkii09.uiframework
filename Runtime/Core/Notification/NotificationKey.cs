using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Identity of a notification. Two requests with equal keys MERGE into one toast rather than
    /// stacking — five pickups of the same item edit one entry's quantity.
    ///
    /// <para>A readonly struct rather than a cached key object. Melvor caches one key instance per
    /// subject because JS has no value types and needs reference identity for map keys; a C# struct
    /// with proper equality is already allocation-free as a dictionary key, so the cache would be
    /// pure overhead with a lifetime-management problem attached.</para>
    /// </summary>
    public readonly struct NotificationKey : IEquatable<NotificationKey>
    {
        /// <summary>Broad grouping, e.g. "loot", "quest", "error".</summary>
        public readonly string Category;

        /// <summary>Identifier within the category, e.g. an item id.</summary>
        public readonly string Id;

        public NotificationKey(string category, string id)
        {
            Category = category;
            Id = id;
        }

        /// <summary>
        /// False for a default/blank key. Without this guard every keyless notification would share
        /// one identity and merge into a single toast.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Category) || !string.IsNullOrEmpty(Id);

        // Ordinal: these are code-level identifiers, never user-facing text, so culture-sensitive
        // comparison would be both slower and wrong.
        public bool Equals(NotificationKey other)
            => string.Equals(Category, other.Category, StringComparison.Ordinal)
            && string.Equals(Id, other.Id, StringComparison.Ordinal);

        // Overriding both is required, not decorative: implementing IEquatable alone leaves the
        // reflection-based ValueType.GetHashCode in place, which is slow and boxes.
        public override bool Equals(object obj) => obj is NotificationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Category != null ? StringComparer.Ordinal.GetHashCode(Category) : 0;
                return (hash * 397) ^ (Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0);
            }
        }

        public static bool operator ==(NotificationKey a, NotificationKey b) => a.Equals(b);
        public static bool operator !=(NotificationKey a, NotificationKey b) => !a.Equals(b);

        public override string ToString() => $"{Category}/{Id}";
    }
}
