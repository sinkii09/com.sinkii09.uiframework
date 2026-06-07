using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class ColorExtensions
    {
        // Component overrides — create a new Color with one channel replaced.
        // Usage: image.color = image.color.WithA(0.5f);
        public static Color WithR(this Color c, float r) => new(r, c.g, c.b, c.a);
        public static Color WithG(this Color c, float g) => new(c.r, g, c.b, c.a);
        public static Color WithB(this Color c, float b) => new(c.r, c.g, b, c.a);
        public static Color WithA(this Color c, float a) => new(c.r, c.g, c.b, a);

        // Hex string → Color. Accepts "#RRGGBB", "#RRGGBBAA", "RRGGBB", "RRGGBBAA".
        // Returns Color.magenta as a visible error sentinel on invalid input.
        public static Color FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6 && hex.Length != 8) return Color.magenta;
            ColorUtility.TryParseHtmlString("#" + hex, out Color result);
            return result;
        }

        // Color → "#RRGGBB" hex string (no alpha).
        public static string ToHex(this Color c)
            => $"#{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";

        // Multiply alpha without touching RGB — useful for group fade-ins.
        public static Color MultiplyAlpha(this Color c, float multiplier)
            => c.WithA(c.a * multiplier);

        // Lerp only the alpha channel.
        public static Color LerpAlpha(this Color c, float targetAlpha, float t)
            => c.WithA(Mathf.Lerp(c.a, targetAlpha, t));
    }
}
