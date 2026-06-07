using System.Globalization;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class UIFormatUtils
    {
        // "2:05" from 125.3 seconds
        public static string FormatTime(float totalSeconds)
        {
            totalSeconds = Mathf.Max(0f, totalSeconds);
            int m = (int)(totalSeconds / 60f);
            int s = (int)(totalSeconds % 60f);
            return $"{m}:{s:D2}";
        }

        // "1:02:05" for durations >= 1 hour, falls back to "2:05" otherwise
        public static string FormatTimeLong(float totalSeconds)
        {
            totalSeconds = Mathf.Max(0f, totalSeconds);
            int h = (int)(totalSeconds / 3600f);
            int m = (int)(totalSeconds % 3600f / 60f);
            int s = (int)(totalSeconds % 60f);
            return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
        }

        // "10,000" — invariant culture so output is consistent across device locales
        public static string FormatScore(int score)
            => score.ToString("N0", CultureInfo.InvariantCulture);

        // "75%" from 0.75f
        public static string FormatPercent(float value)
            => $"{Mathf.RoundToInt(value * 100)}%";

        // "3/8" for progress/fraction display (matches found, lives, charges, etc.)
        public static string FormatFraction(int current, int max)
            => $"{current}/{max}";

        // "99+" for badge/notification counts that exceed a display cap
        public static string FormatBadgeCount(int count, int cap = 99)
            => count > cap ? $"{cap}+" : count.ToString();

        // "1.2K" / "3.5M" for large numbers (leaderboards, currencies)
        public static string FormatCompact(long value)
        {
            if (value >= 1_000_000) return $"{value / 1_000_000f:F1}M";
            if (value >= 1_000)     return $"{value / 1_000f:F1}K";
            return value.ToString();
        }
    }
}
