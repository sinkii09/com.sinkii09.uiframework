using System.Collections.Generic;
using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Stress and lifetime edges for the pool. A leaked or double-recycled cell throws nothing and
    /// renders fine for a while — it only shows up much later as a cell appearing in two places or
    /// an ever-growing instantiate count, so it has to be caught by invariant, not by observation.
    /// </summary>
    public class CellPoolConservationTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown() => PoolTestSupport.DestroySpawned(_spawned);

        [Test]
        public void RandomisedRentRecycleFlush_ConservesEveryCell()
        {
            var pool = PoolTestSupport.NewPool(2, _spawned);
            var live = new List<(RecyclerCell cell, int id)>();
            var random = new System.Random(20260816);

            for (int step = 0; step < 400; step++)
            {
                int roll = random.Next(3);
                if (roll == 0 || live.Count == 0)
                {
                    int id = random.Next(2);
                    live.Add((pool.Rent(id), id));
                }
                else if (roll == 1)
                {
                    int slot = random.Next(live.Count);
                    pool.Recycle(live[slot].cell, live[slot].id);
                    live.RemoveAt(slot);
                }
                else
                {
                    pool.FlushRecycled();
                }

                PoolTestSupport.AssertConserved(pool, 2);
            }

            Assert.AreEqual(live.Count, pool.LiveCount);
        }

        [Test]
        public void Rent_SkipsCellsUnityHasAlreadyDestroyed()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);

            // Scene unload destroys the GameObject but leaves a non-null C# reference behind, so a
            // plain `!= null` check on the C# reference is not enough.
            RecyclerCell doomed = pool.Rent(0);
            pool.Recycle(doomed, 0);
            pool.FlushRecycled();
            Object.DestroyImmediate(doomed.gameObject);

            RecyclerCell fresh = pool.Rent(0);

            Assert.IsTrue(fresh != null, "pool handed back a destroyed cell");
            Assert.AreEqual(2, pool.CreatedCount, "a destroyed cell must be replaced, not reused");
        }

        [Test]
        public void DestroyAll_EmptiesEveryTier()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            pool.Prewarm(0, 3);
            RecyclerCell rented = pool.Rent(0);
            pool.Recycle(rented, 0);

            pool.DestroyAll();

            Assert.AreEqual(0, pool.PooledCount(0));
            Assert.AreEqual(0, pool.RecycledThisTickCount(0));
            Assert.AreEqual(0, pool.LiveCount);
        }
    }
}
