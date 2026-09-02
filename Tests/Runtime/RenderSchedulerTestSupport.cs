using System;
using System.Collections.Generic;
using R3;

namespace Sinkii09.UIFramework.Tests
{
    // A FrameProvider driven by hand, so every coalescing test is deterministic with no wall clock
    // and no play mode. Advance() deliberately mirrors UnityFrameProvider.Run's contract: a work
    // item is removed both when it returns false AND when it throws.
    internal sealed class FakeFrameProvider : FrameProvider
    {
        private readonly List<IFrameRunnerWorkItem> _items = new List<IFrameRunnerWorkItem>();
        private long _frame;

        public int RegisterCalls { get; private set; }
        public int ItemCount => _items.Count;
        public long Frame => _frame;

        // Exceptions escaping a work item, as the host would have seen them.
        public List<Exception> Escaped { get; } = new List<Exception>();

        public override long GetFrameCount() => _frame;

        public override void Register(IFrameRunnerWorkItem callback)
        {
            _items.Add(callback);
            RegisterCalls++;
        }

        public void Advance()
        {
            _frame++;

            // Iterate a snapshot: a work item may register re-entrantly while we are stepping.
            var snapshot = _items.ToArray();
            foreach (var item in snapshot)
            {
                bool keep;
                try
                {
                    keep = item.MoveNext(_frame);
                }
                catch (Exception ex)
                {
                    Escaped.Add(ex);
                    _items.Remove(item);
                    continue;
                }

                if (!keep) _items.Remove(item);
            }
        }

        public void Advance(int frames)
        {
            for (int i = 0; i < frames; i++) Advance();
        }
    }

    // Minimal IUIRenderScheduler so UIBindingExtensions can be pointed at a FakeFrameProvider
    // without constructing the real scheduler or a LifetimeScope.
    internal sealed class FakeRenderScheduler : IUIRenderScheduler
    {
        public FakeFrameProvider Fake { get; } = new FakeFrameProvider();
        public FrameProvider Frames => Fake;

        public bool IsSuspended => false;
        public int SuspendedFrames => 0;

        // Deliberately throws rather than returning an inert handle. This fake pumps its
        // FakeFrameProvider directly, so it could not honour a suspension — and a no-op handle
        // would make a suspension test pass while proving nothing. Suspension tests construct a
        // real UIRenderScheduler over a FakeFrameProvider host instead.
        public IDisposable Suspend()
            => throw new NotSupportedException(
                "FakeRenderScheduler does not simulate suspension. Use " +
                "new UIRenderScheduler(fakeHost) and drive fakeHost.Advance().");
    }

    // A work item that is not one of ours, used to prove the scheduler honours R3's FrameProvider
    // contract for foreign registrations rather than the framework's own keep-alive policy.
    internal sealed class ForeignWorkItem : IFrameRunnerWorkItem
    {
        private readonly Func<long, bool> _body;
        public int Calls { get; private set; }

        public ForeignWorkItem(Func<long, bool> body) => _body = body;

        public bool MoveNext(long frame)
        {
            Calls++;
            return _body(frame);
        }
    }
}
