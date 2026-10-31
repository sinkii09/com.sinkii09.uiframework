using System.Text;

namespace Sinkii09.UIFramework.Editor
{
    // Event path → C# member name: "ui/popup-show" → "Ui_PopupShow", "sfx/2x/coin" → "Sfx__2x_Coin".
    // Each '/' segment becomes PascalCase words (any non letter/digit separates words) and segments are
    // joined with '_'. A segment starting with a digit gets a '_' prefix. Every word starts upper-case,
    // so no result can be a C# keyword (they are all lower-case).
    internal static class SoundIdNaming
    {
        public static string MemberName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var name = new StringBuilder(path.Length + 4);
            foreach (string segment in path.Split('/'))
            {
                string pascal = PascalCase(segment);
                if (pascal.Length == 0) continue;
                if (name.Length > 0) name.Append('_');
                if (char.IsDigit(pascal[0])) name.Append('_');
                name.Append(pascal);
            }
            return name.Length > 0 ? name.ToString() : null;
        }

        public static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsDigit(value[0])) return false;
            foreach (char c in value)
                if (!IsAsciiLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private static string PascalCase(string segment)
        {
            var result = new StringBuilder(segment.Length);
            bool startWord = true;
            foreach (char c in segment)
            {
                if (!IsAsciiLetterOrDigit(c)) { startWord = true; continue; }
                result.Append(startWord ? char.ToUpperInvariant(c) : c);
                startWord = false;
            }
            return result.ToString();
        }

        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }
}
