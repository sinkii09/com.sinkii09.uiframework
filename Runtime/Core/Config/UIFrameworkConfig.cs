using UnityEngine;

namespace Sinkii09.UIFramework
{
    public enum LoaderMode { Resources, Addressables }

    [CreateAssetMenu(menuName = "UIFramework/Config", fileName = "UIFrameworkConfig")]
    public class UIFrameworkConfig : ScriptableObject
    {
        // Default = Resources; switch to Addressables after installing the Addressables package.
        public LoaderMode LoaderMode = LoaderMode.Resources;
        public int MaxNavigationDepth = 10;
        public float DefaultShowDuration = 0.25f;
        public float DefaultHideDuration = 0.2f;
    }
}
