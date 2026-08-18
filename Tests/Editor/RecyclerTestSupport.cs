using System.Collections.Generic;
using NUnit.Framework;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>Shared tuning for the recycling tests, mirroring the settings defaults.</summary>
    internal static class RecyclerTestConstants
    {
        public const float CellSize = 100f;
        public const float Stride = 100f;
        public const float ViewportSize = 500f;
        public const float RecycleDistance = 300f;
        public const float CreateDistance = 200f;
    }

    /// <summary>
    /// A uniform-stride stand-in for the shown-cell window that can apply the actions
    /// <see cref="RecycleWindow.Decide"/> returns. Lets the recycling logic be driven to
    /// convergence with no scene, no ScrollRect and no frame waits.
    /// </summary>
    internal class FakeWindow
    {
        private const float CellSize = RecyclerTestConstants.CellSize;
        private const float Stride = RecyclerTestConstants.Stride;
        private const float ViewportSize = RecyclerTestConstants.ViewportSize;

        public int ItemCount = 1000;
        public int Tick = 1;
        public readonly List<int> Shown = new();

        private readonly Dictionary<int, int> _createdTick = new();

        public int Head => Shown[0];
        public int Tail => Shown[Shown.Count - 1];

        public void Seed(int index)
        {
            Shown.Clear();
            _createdTick.Clear();
            Add(index);
        }

        public void Add(int index)
        {
            _createdTick[index] = Tick;
            if (Shown.Count == 0 || index > Tail) Shown.Add(index);
            else Shown.Insert(0, index);
        }

        /// <summary>Advances the tick so nothing shown counts as created-this-tick any more.</summary>
        public void Settle()
        {
            Tick++;
            foreach (int index in Shown) _createdTick[index] = Tick - 1;
        }

        public WindowState State(float viewportStart)
        {
            if (Shown.Count == 0)
                return new WindowState(viewportStart, ViewportSize, ItemCount, 0, Tick,
                    0, 0f, 0f, 0, 0, 0f, 0f, 0);

            return new WindowState(viewportStart, ViewportSize, ItemCount, Shown.Count, Tick,
                Head, Head * Stride, CellSize, _createdTick[Head],
                Tail, Tail * Stride, CellSize, _createdTick[Tail]);
        }

        public WindowAction Decide(float viewportStart)
            => RecycleWindow.Decide(State(viewportStart),
                RecyclerTestConstants.RecycleDistance, RecyclerTestConstants.CreateDistance);

        public void Apply(WindowAction action)
        {
            switch (action)
            {
                case WindowAction.RecycleHead: Shown.RemoveAt(0); break;
                case WindowAction.RecycleTail: Shown.RemoveAt(Shown.Count - 1); break;
                case WindowAction.CreateBeforeHead: Add(Head - 1); break;
                case WindowAction.CreateAfterTail: Add(Tail + 1); break;
            }
        }

        /// <summary>
        /// The same iteration budget the real pump computes for this geometry, so the tests fail if
        /// the budget ever stops covering the window they drive.
        /// </summary>
        public int IterationBudget => RecycleWindow.MaxIterationsFor(
            ViewportSize, RecyclerTestConstants.CreateDistance, Stride, ItemCount);

        /// <summary>Steps until the decision is None, returning how many steps that took.</summary>
        public int Converge(float viewportStart)
        {
            int steps = 0;
            while (steps <= IterationBudget)
            {
                WindowAction action = Decide(viewportStart);
                if (action == WindowAction.None) return steps;

                Apply(action);
                steps++;
            }
            return steps;
        }

        public void AssertContiguous()
        {
            for (int i = 1; i < Shown.Count; i++)
                Assert.AreEqual(Shown[i - 1] + 1, Shown[i], "window has a gap at slot " + i);
        }
    }
}
