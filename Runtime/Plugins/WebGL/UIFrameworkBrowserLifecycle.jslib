// UIFramework WebGL lifecycle + storage flush. C# side: Runtime/Core/Lifecycle/WebGlBrowserBridge.cs.
//
// - UIFramework_RequestStorageFlush: FS.syncfs(false) pushes IDBFS to IndexedDB. Coalesced during
//   normal play: while a sync is in flight, further requests only queue ONE trailing sync, so a 3-4
//   file-operation save burst costs at most two. IDBFS snapshots the file set when syncfs is CALLED,
//   so a change made after that needs the trailing sync to be pushed.
// - visibilitychange (hidden) and pagehide call C# synchronously, INSIDE the event handler: a hidden
//   tab stops requestAnimationFrame and with it the player loop, so a save deferred to a later frame
//   may never run. While C# saves, flush requests are only recorded; once it returns, ONE sync is
//   forced even if another is still in flight. A queued trailing sync would wait for that earlier
//   sync's async callback, which on a tab close likely never comes — stranding every key the hide
//   save wrote. Emscripten does not serialise concurrent syncfs calls; it counts them and warns.
var UIFrameworkBrowserLifecycle = {
  $UIFrameworkLifecycle__deps: ['$FS'],
  $UIFrameworkLifecycle: {
    onHidden: 0,
    onSyncError: 0,
    listening: false,
    inFlight: 0,
    pending: false,
    inHidden: false,
    visibilityListener: null,
    pageHideListener: null,

    flush: function (force) {
      var state = UIFrameworkLifecycle;
      if (state.inHidden || (state.inFlight > 0 && !force)) {
        state.pending = true;
        return;
      }
      // This sync captures everything written so far, including whatever was queued.
      state.pending = false;
      state.inFlight++;
      try {
        FS.syncfs(false, function (err) {
          state.inFlight--;
          if (err) {
            console.error('[UIFramework] FS.syncfs failed', err);
            var onSyncError = state.onSyncError;
            if (onSyncError) {
              try { {{{ makeDynCall('v', 'onSyncError') }}}(); } catch (e) { console.error(e); }
            }
          }
          if (state.pending && state.inFlight === 0) state.flush(false);
        });
      } catch (e) {
        state.inFlight--;
        console.error('[UIFramework] FS.syncfs threw', e);
      }
    },

    hidden: function () {
      var state = UIFrameworkLifecycle;
      // Zero after Unregister (component destroyed, or unityInstance.Quit()): never call into a
      // torn-down player.
      var onHidden = state.onHidden;
      if (onHidden) {
        state.inHidden = true;
        try { {{{ makeDynCall('v', 'onHidden') }}}(); } catch (e) { console.error('[UIFramework] hidden callback failed', e); }
        state.inHidden = false;
      }
      // Forced, and regardless of what C# did: an earlier write may not have been synced yet.
      state.flush(true);
    }
  },

  UIFramework_RequestStorageFlush: function () {
    UIFrameworkLifecycle.flush(false);
  },

  // Idempotent: a second registration replaces the callbacks and never adds a second listener.
  UIFramework_RegisterLifecycleCallbacks: function (onHidden, onSyncError) {
    var state = UIFrameworkLifecycle;
    state.onHidden = onHidden;
    state.onSyncError = onSyncError;
    if (state.listening) return;

    state.visibilityListener = function () {
      if (document.visibilityState === 'hidden') state.hidden();
    };
    state.pageHideListener = function () { state.hidden(); };
    document.addEventListener('visibilitychange', state.visibilityListener);
    window.addEventListener('pagehide', state.pageHideListener);
    state.listening = true;
  },

  UIFramework_UnregisterLifecycleCallbacks: function () {
    var state = UIFrameworkLifecycle;
    state.onHidden = 0;
    state.onSyncError = 0;
    if (!state.listening) return;

    document.removeEventListener('visibilitychange', state.visibilityListener);
    window.removeEventListener('pagehide', state.pageHideListener);
    state.visibilityListener = null;
    state.pageHideListener = null;
    state.listening = false;
  }
};

autoAddDeps(UIFrameworkBrowserLifecycle, '$UIFrameworkLifecycle');
mergeInto(LibraryManager.library, UIFrameworkBrowserLifecycle);
