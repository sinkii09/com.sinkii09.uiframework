using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// What one prefetch reconcile has to tell the consumer: ranges to cancel, then ranges to announce,
    /// each ascending. Fixed slots rather than a list so a pump allocates nothing. The capacities are
    /// the worst case of <see cref="PrefetchWindow.Diff"/>'s general interval arithmetic (two
    /// remaining pieces, each split around the band; the band split around both), not a claim about
    /// which cases the view can actually reach.
    /// </summary>
    internal struct PrefetchDiff
    {
        private IndexRange _cancel0, _cancel1, _cancel2, _cancel3;
        private IndexRange _prefetch0, _prefetch1, _prefetch2;

        public int CancelCount { get; private set; }
        public int PrefetchCount { get; private set; }

        public IndexRange Cancel(int i) => i switch
        {
            0 => _cancel0, 1 => _cancel1, 2 => _cancel2, 3 => _cancel3,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };

        public IndexRange Prefetch(int i) => i switch
        {
            0 => _prefetch0, 1 => _prefetch1, 2 => _prefetch2,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };

        internal void AddCancel(IndexRange range)
        {
            if (range.IsEmpty) return;
            switch (CancelCount)
            {
                case 0: _cancel0 = range; break;
                case 1: _cancel1 = range; break;
                case 2: _cancel2 = range; break;
                default: _cancel3 = range; break;
            }
            CancelCount++;
        }

        internal void AddPrefetch(IndexRange range)
        {
            if (range.IsEmpty) return;
            switch (PrefetchCount)
            {
                case 0: _prefetch0 = range; break;
                case 1: _prefetch1 = range; break;
                default: _prefetch2 = range; break;
            }
            PrefetchCount++;
        }
    }
}
