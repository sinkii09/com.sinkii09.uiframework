using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// TMP-compatible rich text helpers and common string utilities.
    /// All tag methods are chainable: "Score".Bold().Colored(Color.yellow)
    /// </summary>
    public static class StringExtensions
    {
        // TMP rich text wrappers — chainable.
        public static string Bold(this string s) => $"<b>{s}</b>";
        public static string Italic(this string s) => $"<i>{s}</i>";
        public static string Underline(this string s) => $"<u>{s}</u>";
        public static string Strikethrough(this string s) => $"<s>{s}</s>";

        // Color by Unity Color value.
        public static string Colored(this string s, Color c)
            => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{s}</color>";

        // Color by hex string ("#FF0000" or "FF0000").
        // Invalid hex (wrong length or non-hex chars) returns the string unmodified to prevent
        // rich text tag injection from untrusted input (localization strings, user content).
        public static string Colored(this string s, string hex)
        {
            if (string.IsNullOrEmpty(hex)) return s;
            hex = hex.TrimStart('#');
            if ((hex.Length != 6 && hex.Length != 8) || !IsValidHex(hex)) return s;
            return $"<color=#{hex}>{s}</color>";
        }

        private static bool IsValidHex(string hex)
        {
            foreach (char c in hex)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        // Override font size (TMP absolute size).
        public static string Sized(this string s, int size) => $"<size={size}>{s}</size>";

        // Truncate with an ellipsis suffix when the string exceeds maxLength.
        // The returned string is at most maxLength characters including the ellipsis.
        public static string Truncate(this string s, int maxLength, string ellipsis = "…")
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s;
            int cut = maxLength - ellipsis.Length;
            return cut <= 0 ? ellipsis : s[..cut] + ellipsis;
        }

        // Null-safe check — shorthand for string.IsNullOrEmpty.
        public static bool IsNullOrEmpty(this string s) => string.IsNullOrEmpty(s);

        // Convert "helloWorld" or "hello_world" to "Hello World" for display labels.
        public static string ToDisplayName(this string s)
        {
            if (s.IsNullOrEmpty()) return s;
            var sb = new System.Text.StringBuilder();
            sb.Append(char.ToUpper(s[0]));
            for (int i = 1; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) || s[i] == '_')
                    sb.Append(' ');
                if (s[i] != '_')
                    sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
