using System.Collections.Generic;
using System.Text;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>
    /// Maps a prefab child name to a C# serialized-field name: "health-bar", "HealthBar",
    /// "health_bar" and "Health Bar" all become "_healthBar". Pure — no Unity API, no I/O.
    /// </summary>
    public static class BindingFieldNamer
    {
        private const string Fallback = "_field";

        private static readonly char[] Separators = { ' ', '-', '_', '.', '/', '\\', '(', ')', '[', ']' };

        // Inherited members a generated field could shadow. Deliberately short: generated names are
        // always '_'-prefixed camelCase, so they cannot collide with UIViewBase's PascalCase
        // properties (IsVisible, CanvasGroup, RectTransform) or with MonoBehaviour's members. The
        // only reachable collision is UIView<T>'s protected field. Private base fields are not
        // inherited-visible, so redeclaring them is legal and harmless.
        private static readonly HashSet<string> Reserved = new() { "_showDisposables" };

        public static bool IsReserved(string fieldName) => Reserved.Contains(fieldName);

        /// <summary>
        /// A C# keyword can never come out of this: every result is '_'-prefixed, and "_class" is a
        /// legal identifier. That is why there is no '@'-escaping path here.
        /// </summary>
        public static string ToFieldName(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName)) return Fallback;

            var tokens = childName.Split(Separators, System.StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder("_");
            var wroteFirst = false;

            foreach (var raw in tokens)
            {
                var token = KeepIdentifierChars(raw);
                if (token.Length == 0) continue;

                // First token keeps its interior casing so "HealthBar" -> "healthBar"; later tokens
                // are capitalised so "health-bar" lands on the same result.
                sb.Append(wroteFirst ? char.ToUpperInvariant(token[0]) : char.ToLowerInvariant(token[0]));
                sb.Append(token, 1, token.Length - 1);
                wroteFirst = true;
            }

            return wroteFirst ? sb.ToString() : Fallback;
        }

        /// <summary>
        /// Returns a field name not already in <paramref name="taken"/> and never a reserved one,
        /// then ADDS it to that set. Collisions qualify by parent name first ("_iconHealthBar")
        /// and only fall back to a numeric suffix when that collides too, so the common
        /// two-children-named-Icon case reads meaningfully instead of "_icon2".
        /// </summary>
        public static string ReserveUniqueFieldName(string childName, string parentName, ISet<string> taken)
        {
            var basic = ToFieldName(childName);
            if (IsAvailable(basic, taken)) return Take(basic, taken);

            var qualified = ToFieldName(parentName + "-" + childName);
            if (IsAvailable(qualified, taken)) return Take(qualified, taken);

            for (var i = 2; ; i++)
            {
                var numbered = qualified + i;
                if (IsAvailable(numbered, taken)) return Take(numbered, taken);
            }
        }

        private static bool IsAvailable(string name, ISet<string> taken) =>
            !IsReserved(name) && !taken.Contains(name);

        private static string Take(string name, ISet<string> taken)
        {
            taken.Add(name);
            return name;
        }

        private static string KeepIdentifierChars(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}

