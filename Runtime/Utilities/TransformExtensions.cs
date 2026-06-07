using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class TransformExtensions
    {
        // Axis setters — modify a single axis without constructing the full vector.
        // Usage: transform.SetX(0f);
        public static void SetX(this Transform t, float x) => t.position = new Vector3(x, t.position.y, t.position.z);
        public static void SetY(this Transform t, float y) => t.position = new Vector3(t.position.x, y, t.position.z);
        public static void SetZ(this Transform t, float z) => t.position = new Vector3(t.position.x, t.position.y, z);

        public static void SetLocalX(this Transform t, float x) => t.localPosition = new Vector3(x, t.localPosition.y, t.localPosition.z);
        public static void SetLocalY(this Transform t, float y) => t.localPosition = new Vector3(t.localPosition.x, y, t.localPosition.z);
        public static void SetLocalZ(this Transform t, float z) => t.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, z);

        // Reset position, rotation, and scale to identity in local space.
        public static void ResetLocal(this Transform t)
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        // Recursively set layer on this GameObject and all descendants.
        public static void SetLayerRecursively(this Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t)
                child.SetLayerRecursively(layer);
        }

        // Destroy all direct children (UI panels, card grids, etc.).
        public static void DestroyChildren(this Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        // RectTransform: set anchored position X or Y independently.
        public static void SetAnchoredX(this RectTransform rt, float x)
            => rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);

        public static void SetAnchoredY(this RectTransform rt, float y)
            => rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
    }
}
