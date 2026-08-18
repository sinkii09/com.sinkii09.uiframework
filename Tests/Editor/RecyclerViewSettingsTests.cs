using System;
using NUnit.Framework;
using Sinkii09.UIFramework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Settings are authored in the inspector, so bad values arrive as data rather than as code.
    /// <c>Validate()</c> is the only place that can catch them, and the hysteresis rule in
    /// particular is load-bearing: violate it and the list churns cells every single frame while
    /// still looking correct on screen.
    /// </summary>
    public class RecyclerViewSettingsTests
    {
        /// <summary>Fields are private [SerializeField], so author them the way Unity would.</summary>
        private static RecyclerViewSettings Build(
            float cellSize = 100f, float spacing = 0f,
            float recycleDistance = 300f, float createDistance = 200f, int prewarmCount = 8)
        {
            string json =
                $"{{\"_cellSize\":{cellSize},\"_spacing\":{spacing}," +
                $"\"_recycleDistance\":{recycleDistance},\"_createDistance\":{createDistance}," +
                $"\"_prewarmCount\":{prewarmCount}}}";

            return JsonUtility.FromJson<RecyclerViewSettings>(json);
        }

        [Test]
        public void Defaults_AreValid()
        {
            Assert.DoesNotThrow(() => Build().Validate());
        }

        [Test]
        public void Stride_IsCellSizePlusSpacing()
        {
            Assert.AreEqual(112f, Build(cellSize: 100f, spacing: 12f).Stride, 1e-4f);
        }

        [Test]
        public void Validate_RejectsRecycleDistanceNotExceedingCreateDistance()
        {
            // Equal distances share one boundary: a cell on it is recycled and immediately recreated.
            Assert.Throws<ArgumentException>(() => Build(recycleDistance: 200f, createDistance: 200f).Validate());
            Assert.Throws<ArgumentException>(() => Build(recycleDistance: 150f, createDistance: 200f).Validate());
        }

        [Test]
        public void Validate_RejectsNonPositiveCellSize()
        {
            Assert.Throws<ArgumentException>(() => Build(cellSize: 0f).Validate());
            Assert.Throws<ArgumentException>(() => Build(cellSize: -10f).Validate());
        }

        [Test]
        public void Validate_RejectsNegativeSpacingAndPrewarm()
        {
            Assert.Throws<ArgumentException>(() => Build(spacing: -1f).Validate());
            Assert.Throws<ArgumentException>(() => Build(prewarmCount: -1).Validate());
        }
    }
}
