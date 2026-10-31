using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Whether the browser's audio context can play right now. Fail-open: if the state cannot be
    // read, assume running, because a wrong "locked" would silence the game for good while a wrong
    // "running" only costs the few sounds Unity replays on unlock.
    // Music requested while locked is remembered (the latest request wins) and started on unlock.
    internal sealed class AudioContextGate
    {
        public const int Running = 0;
        public const int Suspended = 1;
        public const int Closed = 2;
        public const int Unknown = -1;

        private readonly Func<int> _queryState;
        private bool _loggedQueryFailure;
        private bool _hasQueuedMusic;
        private SoundId _queuedMusic;
        private int _queuedFadeMs;

        public AudioContextGate(Func<int> queryState) => _queryState = queryState;

        public bool IsRunning { get; private set; } = true;

        // True when this refresh moved the context from locked to running.
        public bool Refresh()
        {
            bool wasRunning = IsRunning;
            int state = Query();
            IsRunning = state == Running || state == Unknown;
            return !wasRunning && IsRunning;
        }

        public void QueueMusic(SoundId id, int fadeMs)
        {
            _hasQueuedMusic = true;
            _queuedMusic = id;
            _queuedFadeMs = fadeMs;
        }

        public void ClearQueuedMusic() => _hasQueuedMusic = false;

        public bool TryTakeQueuedMusic(out SoundId id, out int fadeMs)
        {
            id = _queuedMusic;
            fadeMs = _queuedFadeMs;
            if (!IsRunning || !_hasQueuedMusic) return false;
            _hasQueuedMusic = false;
            return true;
        }

        private int Query()
        {
            try
            {
                return _queryState();
            }
            catch (Exception ex)
            {
                // The query crosses into browser JS; anything it throws means "cannot tell".
                if (!_loggedQueryFailure)
                {
                    _loggedQueryFailure = true;
                    Debug.LogWarning($"[Audio] Could not read the audio context state; assuming it runs. {ex.Message}");
                }
                return Unknown;
            }
        }
    }
}
