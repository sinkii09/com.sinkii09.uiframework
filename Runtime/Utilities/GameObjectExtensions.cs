using UnityEngine;

namespace Sinkii09.UIFramework
{
    public static class GameObjectExtensions
    {
        // Get existing component or add a new one — avoids the TryGetComponent + AddComponent boilerplate.
        // Usage: var rb = gameObject.GetOrAddComponent<Rigidbody>();
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
            => go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        // Null-allocation component presence check.
        public static bool HasComponent<T>(this GameObject go) where T : Component
            => go.TryGetComponent<T>(out _);

        // Toggle active state.
        public static void Toggle(this GameObject go)
            => go.SetActive(!go.activeSelf);

        // Set active only if the state actually changes — avoids a redundant dirty mark.
        public static void SetActiveSafe(this GameObject go, bool active)
        {
            if (go.activeSelf != active)
                go.SetActive(active);
        }

        // Component variants of the above for convenience in MonoBehaviour code.
        public static T GetOrAddComponent<T>(this Component c) where T : Component
            => c.gameObject.GetOrAddComponent<T>();

        public static bool HasComponent<T>(this Component c) where T : Component
            => c.gameObject.HasComponent<T>();
    }
}
