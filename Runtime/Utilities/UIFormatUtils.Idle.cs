using System;
using System.Globalization;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static partial class UIFormatUtils
    {
        // Index = group (powers of 1000). Built once: a rolling label formats on most frames, and the
        // letter pairs would otherwise allocate a char[] and a string each time. double.MaxValue is
        // group 102 ("dt"), so two letters always suffice.
        private static readonly string[] IdleSuffixes = BuildIdleSuffixes(maxGroup: 102);

        // Doubles misrepresent decimals: 1150 / 1000 * 100 is 114.99999999999999, and a plain floor
        // prints "1.14K". Rounding to 12 significant digits first absorbs that representation error
        // (~1e-15 relative) while staying far below any digit a player can see, so real values like
        // 1149.9 still truncate down. With 3 integer digits, 12 significant digits = 9 decimals.
        private const int NoiseDecimals = 9;

        private static bool _nanReported;

        /// <summary>
        /// Idle-game notation: "999", "1.23K", "45.6M", "789T", then "1.00aa", "1.00ab" … "1.00zz".
        /// </summary>
        /// <remarks>
        /// Three significant digits, truncated rather than rounded, so a number never shows the next
        /// tier before it gets there (999,999 is "999K", never "1.00M"). Trailing zeros are kept so the
        /// label width stays steady while the value climbs. Values below 1,000 print as whole numbers.
        /// NaN prints "0" (and logs once), infinity prints "∞".
        /// </remarks>
        public static string FormatIdle(double value)
        {
            if (double.IsNaN(value))
            {
                ReportIdleNaN();
                return "0";
            }
            if (double.IsPositiveInfinity(value)) return "∞";
            if (double.IsNegativeInfinity(value)) return "-∞";

            QuantiseIdle(Math.Abs(value), out var group, out var intDigits, out var digits);
            // A negative value that truncates to zero prints "0", not "-0".
            var sign = value < 0 && digits != 0 ? "-" : "";

            if (group == 0)
                return sign + digits.ToString(CultureInfo.InvariantCulture);

            return sign + FormatIdleMantissa(intDigits, digits) + IdleSuffixes[group];
        }

        // Comparable key of what FormatIdle would print, so a caller can skip re-formatting (and the
        // string allocation) when the visible text would not change. Shares QuantiseIdle with
        // FormatIdle, so the two cannot disagree about where a tier boundary is.
        internal static long GetIdleKey(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (double.IsPositiveInfinity(value)) return long.MaxValue;
            if (double.IsNegativeInfinity(value)) return long.MinValue;

            QuantiseIdle(Math.Abs(value), out var group, out var intDigits, out var digits);
            // Group 0 keys are the value itself (0..999); higher groups start at 11,100, so the key
            // is monotonic in the value.
            var key = group == 0 ? digits : group * 10_000L + intDigits * 1_000L + digits;
            return value < 0 ? -key : key;
        }

        internal static void ResetIdleNaNWarningForTests() => _nanReported = false;

        // group 0: digits is the whole number (0..999). group >= 1: digits holds the 3 significant
        // digits (100..999) and intDigits (1..3) how many of them sit before the decimal point.
        private static void QuantiseIdle(double abs, out int group, out int intDigits, out long digits)
        {
            var whole = Math.Floor(Math.Round(abs, NoiseDecimals));
            if (whole < 1000d)
            {
                group = 0;
                intDigits = 0;
                digits = (long)whole;
                return;
            }

            var exponent = Math.Max(3, (int)Math.Floor(Math.Log10(abs)));
            var threeDigits = 100d;
            // The noise rounding can carry a value like 9.99999999999995e5 up to the next power of
            // ten, so the exponent is re-checked against the digits actually printed. Stepping down
            // is defensive only (Log10 is off by at most an ulp, which the rounding absorbs).
            for (var attempt = 0; attempt < 3; attempt++)
            {
                threeDigits = Math.Floor(Math.Round(abs / Math.Pow(10d, exponent - 2), NoiseDecimals));
                if (threeDigits >= 1000d) { exponent++; continue; }
                if (threeDigits < 100d && exponent > 3) { exponent--; continue; }
                break;
            }

            // Clamped so a value that oscillated at a rounding midpoint still prints 3 digits and
            // keeps the key monotonic, rather than "0.99K".
            digits = (long)Math.Min(999d, Math.Max(100d, threeDigits));
            group = exponent / 3;
            intDigits = exponent % 3 + 1;
        }

        private static string FormatIdleMantissa(int intDigits, long digits)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (intDigits)
            {
                case 1: return (digits / 100).ToString(inv) + "." + (digits % 100).ToString("D2", inv);
                case 2: return (digits / 10).ToString(inv) + "." + (digits % 10).ToString(inv);
                default: return digits.ToString(inv);
            }
        }

        // K, M, B, T, then aa (10^15), ab, … az, ba, …
        private static string[] BuildIdleSuffixes(int maxGroup)
        {
            var suffixes = new string[maxGroup + 1];
            string[] named = { "", "K", "M", "B", "T" };
            for (var group = 0; group <= maxGroup; group++)
            {
                if (group < named.Length)
                {
                    suffixes[group] = named[group];
                    continue;
                }

                var index = group - named.Length;
                suffixes[group] = new string(new[] { (char)('a' + index / 26), (char)('a' + index % 26) });
            }
            return suffixes;
        }

        private static void ReportIdleNaN()
        {
            if (_nanReported) return;
            _nanReported = true;
            Debug.LogError("[UIFormatUtils] FormatIdle received NaN; shown as 0. An economy value " +
                           "went NaN upstream (0/0, Infinity - Infinity, or a NaN multiplier).");
        }
    }
}
