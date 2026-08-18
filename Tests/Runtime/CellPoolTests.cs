using System.Collections.Generic;
using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Covers the two-tier pool. The tier split is the whole reason scrolling allocates nothing:
    /// a cell recycled off one end must be reusable at the other end within the same tick, without
    /// a SetActive round-trip and without an Instantiate.
    /// </summary>
    public class CellPoolTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown() => PoolTestSupport.DestroySpawned(_spawned);

        [Test]
        public void Prewarm_CreatesCellsInactiveAndPooled()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            pool.Prewarm(0, 5);

            Assert.AreEqual(5, pool.CreatedCount);
            Assert.AreEqual(5, pool.PooledCount(0));
            Assert.AreEqual(0, pool.LiveCount);
            PoolTestSupport.AssertConserved(pool, 1);
        }

        [Test]
        public void Rent_AfterPrewarm_DoesNotInstantiate()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            pool.Prewarm(0, 3);

            for (int i = 0; i < 3; i++) pool.Rent(0);

            Assert.AreEqual(3, pool.CreatedCount, "prewarmed cells should have covered every rent");
            Assert.AreEqual(3, pool.LiveCount);
            PoolTestSupport.AssertConserved(pool, 1);
        }

        [Test]
        public void Rent_BeyondThePool_Instantiates()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            pool.Prewarm(0, 1);

            pool.Rent(0);
            pool.Rent(0);

            Assert.AreEqual(2, pool.CreatedCount);
            PoolTestSupport.AssertConserved(pool, 1);
        }

        [Test]
        public void RecycleThenRent_InTheSameTick_ReusesTheCellAndKeepsItActive()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            RecyclerCell first = pool.Rent(0);

            pool.Recycle(first, 0);
            Assert.IsTrue(first.gameObject.activeSelf,
                "staged cells stay active — deactivating then reactivating in one tick is the churn we avoid");

            RecyclerCell second = pool.Rent(0);

            Assert.AreSame(first, second, "a cell staged this tick must be reused before the pool");
            Assert.AreEqual(1, pool.CreatedCount, "same-tick reuse must not instantiate");
            PoolTestSupport.AssertConserved(pool, 1);
        }

        [Test]
        public void Recycle_CallsOnRecycledAndClearsTheIndex()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            var cell = (TestCell)pool.Rent(0);
            cell.Index = 42;

            pool.Recycle(cell, 0);

            Assert.AreEqual(1, cell.RecycledCount);
            Assert.AreEqual(-1, cell.Index, "a pooled cell must not keep claiming a data index");
        }

        [Test]
        public void FlushRecycled_DeactivatesStagedCellsAndMovesThemToThePool()
        {
            var pool = PoolTestSupport.NewPool(1, _spawned);
            RecyclerCell cell = pool.Rent(0);
            pool.Recycle(cell, 0);

            Assert.AreEqual(1, pool.RecycledThisTickCount(0));

            pool.FlushRecycled();

            Assert.AreEqual(0, pool.RecycledThisTickCount(0));
            Assert.AreEqual(1, pool.PooledCount(0));
            Assert.IsFalse(cell.gameObject.activeSelf);
            PoolTestSupport.AssertConserved(pool, 1);
        }

        [Test]
        public void Pools_AreIndependentPerPrefabId()
        {
            var pool = PoolTestSupport.NewPool(3, _spawned);
            pool.Prewarm(1, 2);

            Assert.AreEqual(0, pool.PooledCount(0));
            Assert.AreEqual(2, pool.PooledCount(1));
            Assert.AreEqual(0, pool.PooledCount(2));

            RecyclerCell fromOne = pool.Rent(1);
            pool.Recycle(fromOne, 1);
            pool.FlushRecycled();

            Assert.AreEqual(2, pool.PooledCount(1));
            Assert.AreEqual(0, pool.PooledCount(0), "recycling into prefab 1 must not feed prefab 0");
            PoolTestSupport.AssertConserved(pool, 3);
        }
    }
}
