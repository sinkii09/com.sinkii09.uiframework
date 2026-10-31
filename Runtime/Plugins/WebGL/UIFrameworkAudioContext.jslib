// UIFramework audio context gate. C# side: Runtime/Audio/Unity/WebGlAudioBridge.cs.
//
// Reads Unity's own AudioContext (WEBAudio.audioContext, from Unity's Audio.js). Unity queues every
// sound started while that context is suspended and plays the whole queue once it unlocks, so C#
// polls the state and drops SFX while locked instead of feeding that queue.
// Every failure path answers "unknown" (-1), which C# treats as running: fail-open.
var UIFrameworkAudioContext = {
  $UIFrameworkAudio__deps: ['$WEBAudio'],
  $UIFrameworkAudio: {
    context: function () {
      if (typeof WEBAudio === 'undefined' || !WEBAudio || !WEBAudio.audioContext) return null;
      return WEBAudio.audioContext;
    }
  },

  // 0 running, 1 suspended or interrupted (iOS), 2 closed, -1 unknown.
  UIFramework_AudioContextState: function () {
    try {
      var ctx = UIFrameworkAudio.context();
      if (!ctx) return -1;
      switch (ctx.state) {
        case 'running': return 0;
        case 'suspended':
        case 'interrupted': return 1;
        case 'closed': return 2;
        default: return -1;
      }
    } catch (e) {
      return -1;
    }
  },

  // Best effort. iOS Safari can refuse a plain resume after an interruption; suspending first
  // unsticks it. Unity itself also resumes on the next mousedown/touchstart.
  UIFramework_AudioContextResume: function () {
    try {
      var ctx = UIFrameworkAudio.context();
      if (!ctx || ctx.state === 'running' || ctx.state === 'closed') return;
      ctx.resume().catch(function () {
        ctx.suspend().then(function () { return ctx.resume(); }).catch(function (e) {
          console.warn('[UIFramework] audio context resume failed', e);
        });
      });
    } catch (e) {
      console.warn('[UIFramework] audio context resume threw', e);
    }
  }
};

autoAddDeps(UIFrameworkAudioContext, '$UIFrameworkAudio');
mergeInto(LibraryManager.library, UIFrameworkAudioContext);
