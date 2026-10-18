using System;

namespace Sinkii09.UIFramework
{
    /// <summary>A half-open run of item indices, <c>[Start, End)</c>. Every empty range equals every other.</summary>
    internal readonly struct IndexRange : IEquatable<IndexRange>
    {
        public static readonly IndexRange Empty = default;

        public readonly int Start;
        public readonly int End;

        /// <summary>An <paramref name="end"/> before <paramref name="start"/> yields an empty range.</summary>
        public IndexRange(int start, int end)
        {
            Start = start;
            End = end < start ? start : end;
        }

        public int Count => End - Start;
        public bool IsEmpty => End <= Start;
        public bool Contains(int index) => index >= Start && index < End;

        /// <summary>The smallest range covering this one and <paramref name="index"/>.</summary>
        public IndexRange Include(int index)
            => IsEmpty
                ? new IndexRange(index, index + 1)
                : new IndexRange(Math.Min(Start, index), Math.Max(End, index + 1));

        public bool Equals(IndexRange other)
            => (IsEmpty && other.IsEmpty) || (Start == other.Start && End == other.End);

        public override bool Equals(object obj) => obj is IndexRange other && Equals(other);
        public override int GetHashCode() => IsEmpty ? 0 : (Start * 397) ^ End;
        public override string ToString() => IsEmpty ? "[empty]" : $"[{Start}, {End})";
    }
}
