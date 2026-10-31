using System;
using System.Collections.Generic;

namespace Sinkii09.UIFramework
{
    // The audio engine: pure C#, no UnityEngine, single-threaded (main thread), allocation-free on
    // Play and Tick. Decides what plays, at what gain and what gets stolen; IVoiceBackend only obeys.
    //
    // Instances are separate from voices. An instance is one play request (Pending, Playing or
    // Stopping) and is what a PlayHandle points at; a backend voice is taken only when an instance
    // actually starts, so a delayed sound does not hold a voice while it waits. Event and group caps
    // count Playing instances; the global cap (the pool size) counts Playing too, and Stopping
    // instances fade on the backend's headroom voices.
    internal sealed partial class AudioEngine : IPlayHandleOwner
    {
        private readonly IVoiceBackend _backend;
        private readonly Action<string> _logError;
        private readonly InstanceSlot[] _slots;
        private readonly int[] _freeVoices;
        private readonly int _playingCap;
        private readonly float _maxTickDelta;
        private readonly Dictionary<SoundId, EventDefinition> _events = new();
        private readonly List<GroupDefinition> _groups = new();

        private int _freeVoiceCount;
        private AudioRandom _random;
        private double _now;
        private long _sequence;
        private int _playingTotal;
        private AudioCounters _counters;

        public AudioEngine(IVoiceBackend backend, int playingCap, float maxTickDelta, Action<string> logError,
                           uint seed = 0x2545F491)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            if (playingCap < 1 || playingCap > backend.VoiceCount)
                throw new ArgumentOutOfRangeException(nameof(playingCap), playingCap,
                    $"Must be between 1 and the backend's {backend.VoiceCount} voices.");

            _logError = logError ?? (_ => { });
            _playingCap = playingCap;
            _maxTickDelta = maxTickDelta;
            _random = new AudioRandom(seed);

            // Room for every voice plus as many Pending requests as can play at once.
            _slots = new InstanceSlot[backend.VoiceCount + playingCap];
            for (int i = 0; i < _slots.Length; i++) _slots[i].Voice = -1;

            _freeVoices = new int[backend.VoiceCount];
            for (int v = backend.VoiceCount - 1; v >= 0; v--) _freeVoices[_freeVoiceCount++] = v;

            RecomputeBusLinear();
        }

        public AudioCounters Counters => _counters;
        public int PlayingCount => _playingTotal;
        public double Now => _now;

        // Set every frame by the service from the WebGL context gate; always true elsewhere.
        public bool ContextRunning { get; set; } = true;

        // Mirrors AudioListener.pause. While set, nothing is reclaimed and no pending start fires.
        public bool Paused { get; set; }

        public PlayHandle Play(SoundId id, in PlayOptions options, out PlayStatus status)
        {
            if (!_events.TryGetValue(id, out var sound))
            {
                _counters.Missing++;
                status = PlayStatus.Missing;
                return default;
            }

            status = CheckGates(sound);
            if (status != PlayStatus.Started)
            {
                _counters.Dropped++;
                return default;
            }

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                _counters.Rejected++;
                status = PlayStatus.Rejected;
                return default;
            }

            Prepare(slot, sound, options);

            if (sound.StartDelaySeconds > 0f)
            {
                _slots[slot].StartAt = _now + sound.StartDelaySeconds;
                sound.LastAccepted = _now;
                status = PlayStatus.Pending;
                return HandleFor(slot);
            }

            if (!TryStart(slot))
            {
                FreeSlot(slot);
                _counters.Rejected++;
                status = PlayStatus.Rejected;
                return default;
            }

            sound.LastAccepted = _now;
            return HandleFor(slot);
        }

        private PlayStatus CheckGates(EventDefinition sound)
        {
            AudioBus bus = sound.Group.Bus;
            // A source started under AudioListener.pause sits queued, holds its voice (reclaim is
            // skipped while paused) and fires together with all the others on resume.
            if (Paused) return PlayStatus.Paused;
            if (IsMuted(bus)) return PlayStatus.Muted;
            // Unity's WebGL audio queues every sound started while the context is locked and plays the
            // whole queue on unlock; dropping is the only way to avoid that burst. Music is queued by
            // the service instead, because a missing music track is noticed and a missing click is not.
            if (bus != AudioBus.Music && !ContextRunning) return PlayStatus.ContextLocked;
            if (sound.CooldownSeconds > 0f && _now - sound.LastAccepted < sound.CooldownSeconds)
                return PlayStatus.Cooldown;
            return PlayStatus.Started;
        }

        private void Prepare(int slot, EventDefinition sound, in PlayOptions options)
        {
            ref var instance = ref _slots[slot];
            instance.State = InstanceState.Pending;
            instance.Event = sound;
            instance.Priority = sound.Priority;
            instance.ClipIndex = SoundVariation.PickClip(sound, ref _random);

            float db = sound.VolumeDb + sound.Group.VolumeDb + options.VolumeDb
                       + sound.VolumeJitterDb * _random.NextSigned();
            instance.VoiceLinear = AudioMath.ToLinear(db);

            float semitones = sound.PitchSemitones + options.PitchSemitones
                              + sound.PitchJitterSemitones * _random.NextSigned();
            instance.Pitch = AudioMath.SemitonesToPitch(semitones);
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].State == InstanceState.Free) return i;
            return -1;
        }

        private PlayHandle HandleFor(int slot) => new(this, slot, _slots[slot].Generation);
    }
}
