using System.Collections.Generic;
using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>A minimal concrete cell, so pool tests need no prefabs.</summary>
    internal class TestCell : RecyclerCell
    {
        public int RecycledCount;
        public override void OnRecycled() => RecycledCount++;
    }

    /// <summary>
    /// Pool helpers. Deliberately an internal static class: <see cref="CellPool"/> is internal to
    /// the runtime assembly and only reachable here through InternalsVisibleTo, so keeping every
    /// signature that mentions it internal sidesteps inconsistent-accessibility rules entirely.
    ///
    /// <para><b>Why PlayMode and not EditMode, where the rest of the recycling logic is tested.</b>
    /// Two hard engine constraints, either one of which is enough:
    /// <list type="number">
    /// <item>Unity refuses <c>AddComponent</c> for a MonoBehaviour compiled into an editor-only
    /// assembly ("Can't add script behaviour ... because it is an editor script"), so
    /// <see cref="TestCell"/> cannot live beside the EditMode tests.</item>
    /// <item><see cref="CellPool.DestroyAll"/> calls <c>Object.Destroy</c>, which throws in edit
    /// mode.</item>
    /// </list>
    /// The split is honest rather than unfortunate: <see cref="CellPool"/> drives
    /// <c>SetActive</c>/<c>Destroy</c>, so it is not the pure logic the EditMode suite is for.</para>
    /// </summary>
    internal static class PoolTestSupport
    {
        /// <summary>Builds a pool backed by throwaway GameObjects, recording them for teardown.</summary>
        internal static CellPool NewPool(int prefabCount, List<GameObject> spawned)
        {
            return new CellPool(prefabCount, _ =>
            {
                var go = new GameObject("TestCell", typeof(RectTransform));
                spawned.Add(go);
                return go.AddComponent<TestCell>();
            });
        }

        /// <summary>
        /// The conservation invariant: every cell the pool created is accounted for in exactly one
        /// place. Catches both leaks (a cell lost on recycle) and double-recycles (a cell in two
        /// tiers at once) — neither of which surfaces as an exception.
        /// </summary>
        internal static void AssertConserved(CellPool pool, int prefabCount)
        {
            int accounted = pool.LiveCount;
            for (int id = 0; id < prefabCount; id++)
                accounted += pool.PooledCount(id) + pool.RecycledThisTickCount(id);

            Assert.AreEqual(pool.CreatedCount, accounted,
                "every created cell must be live, pooled, or staged — never lost or double-counted");
        }

        /// <summary>Destroys everything <see cref="NewPool"/> spawned. Call from TearDown.</summary>
        internal static void DestroySpawned(List<GameObject> spawned)
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }
    }
}
