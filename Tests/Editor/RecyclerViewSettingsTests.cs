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
            float recycleDistance = 300f, float createDistance = 200f, int prewarmCount = 8,
            int crossAxisCount = 1, float crossSpacing = 0f)
        {
            string json =
                $"{{\"_cellSize\":{cellSize},\"_spacing\":{spacing}," +
                $"\"_recycleDistance\":{recycleDistance},\"_createDistance\":{createDistance}," +
                $"\"_prewarmCount\":{prewarmCount}," +
                $"\"_crossAxisCount\":{crossAxisCount},\"_crossSpacing\":{crossSpacing}}}";

            return JsonUtility.FromJson<RecyclerViewSettings>(json);
        }

        /// <summary>
        /// Settings with the grid keys absent, the way an asset authored before they existed would
        /// carry them.
        ///
        /// <para><b>What this does and does not prove.</b> <c>JsonUtility</c> constructs the object
        /// and then overwrites only the keys present, so it returns 1 by construction — that is the
        /// same shape Unity's asset deserialization uses, but it is not a proof about Unity's YAML
        /// path, and it should not be read as one. The safety actually comes from the code: the
        /// property clamps and <c>Validate</c> rejects only negatives, so <c>0</c> is survivable
        /// whatever Unity does with an absent field.</para>
        /// </summary>
        private static RecyclerViewSettings BuildWithoutGridKeys()
        {
            const string json =
                "{\"_cellSize\":100,\"_spacing\":0,\"_recycleDistance\":300," +
                "\"_createDistance\":200,\"_prewarmCount\":8}";

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

        // ---- grid ---------------------------------------------------------------------------

        [Test]
        public void Validate_RejectsNegativeCrossAxisCount()
        {
            Assert.Throws<ArgumentException>(() => Build(crossAxisCount: -2).Validate());
        }

        /// <summary>
        /// Zero is what an absent field would read as, and an absent field is what every asset
        /// authored before v3.4 has. Throwing on it would brick those the moment their scene opened,
        /// which is a far worse failure than treating it as the 1 it means. <c>[Min(1)]</c> on the
        /// serialized field is what keeps the Inspector from ever being the source of one.
        /// </summary>
        [Test]
        public void Validate_TreatsZeroAsAnAbsentFieldRatherThanAnError()
        {
            RecyclerViewSettings settings = Build(crossAxisCount: 0);

            Assert.DoesNotThrow(() => settings.Validate());
            Assert.That(settings.CrossAxisCount, Is.EqualTo(1));
            Assert.That(settings.IsGrid, Is.False);
        }

        [Test]
        public void Validate_RejectsNegativeCrossSpacing()
        {
            Assert.Throws<ArgumentException>(() => Build(crossSpacing: -1f).Validate());
        }

        [Test]
        public void OneColumn_IsNotAGrid()
        {
            Assert.That(Build(crossAxisCount: 1).IsGrid, Is.False);
            Assert.That(Build(crossAxisCount: 2).IsGrid, Is.True);
        }

        /// <summary>
        /// The migration case, and the reason the settings block can gain a field at all: settings
        /// serialized before the grid keys existed must still validate, and must read as a list.
        /// </summary>
        [Test]
        public void SettingsSerializedBeforeGridsExisted_StillReadAsAList()
        {
            RecyclerViewSettings settings = BuildWithoutGridKeys();

            Assert.That(settings.CrossAxisCount, Is.EqualTo(1));
            Assert.That(settings.IsGrid, Is.False);
            Assert.That(settings.CrossSpacing, Is.Zero);
            Assert.DoesNotThrow(() => settings.Validate());
        }
    }
}
