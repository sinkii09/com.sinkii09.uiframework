using System;
using AOT;
using UnityEngine;
using UnityEngine.Scripting;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Sinkii09.UIFramework
{
    // The C# side of Plugins/WebGL/UIFrameworkBrowserLifecycle.jslib. Two jobs: push IDBFS to
    // IndexedDB after a save (FS.syncfs), and report "the page was hidden" synchronously from inside
    // the browser event, while the player loop is already stopped.
    //
    // The externs are the ONLY compile-time-guarded code in the framework's persistence path, and
    // that guard is not optional: IL2CPP on Android/iOS links "__Internal" symbols statically, so an
    // unresolved one breaks the build. Every decision (when to flush, what to save) stays in normally
    // compiled code that tests drive directly; off WebGL these methods are no-ops.
    internal static class WebGlBrowserBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void UIFramework_RequestStorageFlush();
        [DllImport("__Internal")] private static extern void UIFramework_RegisterLifecycleCallbacks(Action onHidden, Action onSyncError);
        [DllImport("__Internal")] private static extern void UIFramework_UnregisterLifecycleCallbacks();
#endif

        private static Action _onHidden;

        // Fire-and-forget by design, never awaited. JsonSaveService holds the per-key gate across
        // the backend write; awaiting the sync there would hold it across frames, and a tab-hide in
        // that window would make TrySaveSync's Wait(0) decline and drop the newest snapshot — or, once
        // the tab is hidden and the loop stops, hold the gate for good. The jslib coalesces calls
        // (one in flight plus one queued) and forces one sync after a page-hide save.
        internal static void RequestStorageFlush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UIFramework_RequestStorageFlush();
#endif
        }

        internal static void Register(Action onHidden)
        {
            _onHidden = onHidden;
#if UNITY_WEBGL && !UNITY_EDITOR
            UIFramework_RegisterLifecycleCallbacks(HandleHidden, HandleSyncError);
#endif
        }

        // Only the current owner may unregister, so a stale component torn down after a new one
        // registered cannot cut the new one off.
        internal static void Unregister(Action onHidden)
        {
            if (_onHidden != onHidden) return;
            _onHidden = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            UIFramework_UnregisterLifecycleCallbacks();
#endif
        }

        // Called by the jslib inside a visibilitychange/pagehide handler. A managed exception that
        // crosses back into native code aborts the WebGL player, so nothing may escape — otherwise a
        // throwing game subscriber would crash the game on every tab switch.
        // internal (not private) so a test can stand in for the jslib.
        [Preserve, MonoPInvokeCallback(typeof(Action))]
        internal static void HandleHidden()
        {
            try
            {
                _onHidden?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Persistence] Page-hidden handler threw: {ex}");
            }
        }

        [Preserve, MonoPInvokeCallback(typeof(Action))]
        private static void HandleSyncError()
        {
            try
            {
                Debug.LogError("[Persistence] FS.syncfs failed; the last save may not reach IndexedDB. " +
                               "The browser console has the underlying error.");
            }
            catch (Exception)
            {
                // Logging itself failed; there is nowhere left to report it, and throwing here would
                // abort the player.
            }
        }
    }
}
