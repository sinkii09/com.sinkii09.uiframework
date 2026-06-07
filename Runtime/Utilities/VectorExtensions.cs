using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class VectorExtensions
    {
        // Nullable component overrides — same pattern as ColorExtensions.WithA.
        // Usage: transform.position = transform.position.With(y: 0f);
        public static Vector3 With(this Vector3 v, float? x = null, float? y = null, float? z = null)
            => new(x ?? v.x, y ?? v.y, z ?? v.z);

        public static Vector2 With(this Vector2 v, float? x = null, float? y = null)
            => new(x ?? v.x, y ?? v.y);

        // Flatten Y — projects a world position onto the XZ plane (top-down / isometric).
        public static Vector3 Flat(this Vector3 v) => new(v.x, 0f, v.z);

        // Conversions
        public static Vector2 ToVector2(this Vector3 v) => new(v.x, v.y);
        public static Vector3 ToVector3(this Vector2 v, float z = 0f) => new(v.x, v.y, z);

        // Random point inside a circle/sphere of given radius.
        public static Vector2 RandomInsideCircle(float radius)
            => Random.insideUnitCircle * radius;

        public static Vector3 RandomInsideSphere(float radius)
            => Random.insideUnitSphere * radius;

        // Direction from a to b (normalized).
        public static Vector3 DirectionTo(this Vector3 from, Vector3 to)
            => (to - from).normalized;

        public static Vector2 DirectionTo(this Vector2 from, Vector2 to)
            => (to - from).normalized;
    }
}
