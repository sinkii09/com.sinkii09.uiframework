using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Locks down idle notation: truncation (never rounding up a tier), the suffix sequence past T,
    /// and the decimal-representation noise that a plain floor gets wrong (1150 → "1.14K").
    /// </summary>
    public class UIFormatUtilsIdleTests
    {
        [SetUp]
        public void SetUp() => UIFormatUtils.ResetIdleNaNWarningForTests();

        [TestCase(0d, "0")]
        [TestCase(12.9d, "12")]
        [TestCase(999d, "999")]
        [TestCase(999.9999d, "999")]
        [TestCase(999.9999999d, "999")]
        [TestCase(1000d, "1.00K")]
        [TestCase(1150d, "1.15K")]
        [TestCase(4350d, "4.35K")]
        [TestCase(1234d, "1.23K")]
        [TestCase(1149.9d, "1.14K")]
        [TestCase(9_999d, "9.99K")]
        [TestCase(10_000d, "10.0K")]
        [TestCase(12345d, "12.3K")]
        [TestCase(99_999d, "99.9K")]
        [TestCase(100_000d, "100K")]
        [TestCase(123456d, "123K")]
        [TestCase(999999d, "999K")]
        [TestCase(1e6d, "1.00M")]
        [TestCase(1e9d, "1.00B")]
        [TestCase(1e12d, "1.00T")]
        [TestCase(0.9999999e15d, "999T")]
        [TestCase(1e15d, "1.00aa")]
        [TestCase(2.5e17d, "250aa")]
        [TestCase(1e18d, "1.00ab")]
        [TestCase(1e90d, "1.00az")]
        [TestCase(1e93d, "1.00ba")]
        [TestCase(double.MaxValue, "179dt")]
        public void FormatIdle_PositiveValue_ReturnsExpectedNotation(double value, string expected)
        {
            Assert.AreEqual(expected, UIFormatUtils.FormatIdle(value));
        }

        [Test]
        public void FormatIdle_SumWithRepresentationError_DoesNotTruncateDownATier()
        {
            // 1150 / 1000 * 100 is 114.99999999999999 in binary floating point.
            Assert.AreEqual("1.15K", UIFormatUtils.FormatIdle(1150d / 1000d * 100d * 10d));
            // 999999.9999999999: representation noise just under a tier is absorbed, not truncated.
            Assert.AreEqual("1.00M", UIFormatUtils.FormatIdle(1e6d * (1d - 1e-16d)));
            // The key must agree with the text at that same boundary.
            Assert.AreEqual(UIFormatUtils.GetIdleKey(1e6d), UIFormatUtils.GetIdleKey(1e6d * (1d - 1e-16d)));
        }

        // One case per tier boundary, approached from below by a real (not noise-sized) amount.
        [TestCase(999.5d, "999")]
        [TestCase(999_999.5d, "999K")]
        [TestCase(999_999_999d, "999M")]
        [TestCase(999.9e12d, "999T")]
        [TestCase(999.9e15d, "999aa")]
        public void FormatIdle_JustBelowTier_StaysInLowerTier(double value, string expected)
        {
            Assert.AreEqual(expected, UIFormatUtils.FormatIdle(value));
        }

        [TestCase(-1234d, "-1.23K")]
        [TestCase(-999d, "-999")]
        [TestCase(-0.5d, "0")]
        public void FormatIdle_NegativeValue_PrefixesMinusExceptForZero(double value, string expected)
        {
            Assert.AreEqual(expected, UIFormatUtils.FormatIdle(value));
        }

        [Test]
        public void FormatIdle_Infinity_ReturnsInfinitySymbol()
        {
            Assert.AreEqual("∞", UIFormatUtils.FormatIdle(double.PositiveInfinity));
            Assert.AreEqual("-∞", UIFormatUtils.FormatIdle(double.NegativeInfinity));
        }

        [Test]
        public void FormatIdle_NaN_ReturnsZeroAndLogsOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex("FormatIdle received NaN"));

            Assert.AreEqual("0", UIFormatUtils.FormatIdle(double.NaN));
            Assert.AreEqual("0", UIFormatUtils.FormatIdle(double.NaN));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(1234d, 1239d)]
        [TestCase(1e15d, 1.009e15d)]
        public void GetIdleKey_SameVisibleText_ReturnsSameKey(double a, double b)
        {
            Assert.AreEqual(UIFormatUtils.FormatIdle(a), UIFormatUtils.FormatIdle(b), "precondition");
            Assert.AreEqual(UIFormatUtils.GetIdleKey(a), UIFormatUtils.GetIdleKey(b));
        }

        [Test]
        public void GetIdleKey_IncreasingValues_IsMonotonic()
        {
            double[] values = { -1e6d, -1d, 0d, 1d, 999d, 1000d, 1010d, 9990d, 10_000d, 999_999d, 1e6d, 1e15d, 1e300d };
            for (var i = 1; i < values.Length; i++)
                Assert.Less(UIFormatUtils.GetIdleKey(values[i - 1]), UIFormatUtils.GetIdleKey(values[i]),
                    $"{values[i - 1]} vs {values[i]}");
        }
    }
}
