#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Sinkii09.UIFramework
{
    // The C# side of Plugins/WebGL/UIFrameworkAudioContext.jslib. Compile-time guarded for the same
    // reason as WebGlBrowserBridge: IL2CPP links "__Internal" statically on mobile, so an unresolved
    // extern breaks the build. Off WebGL the context is always running and resume does nothing.
    internal static class WebGlAudioBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int UIFramework_AudioContextState();
        [DllImport("__Internal")] private static extern void UIFramework_AudioContextResume();
#endif

        public static int QueryContextState()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return UIFramework_AudioContextState();
#else
            return AudioContextGate.Running;
#endif
        }

        public static void ResumeContext()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UIFramework_AudioContextResume();
#endif
        }
    }
}
