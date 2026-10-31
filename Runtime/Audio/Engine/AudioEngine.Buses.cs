using System;

namespace Sinkii09.UIFramework
{
    // Bus gain. A bus's gain is the sum of its contributors in dB (the user setting and the duck)
    // plus Master's; the engine is the only place bus gain is applied.
    internal sealed partial class AudioEngine
    {
        private const int BusCount = 4;

        private readonly float[] _userDb = new float[BusCount];
        private readonly float[] _duckDb = new float[BusCount];
        private readonly bool[] _muted = new bool[BusCount];
        private readonly float[] _busLinear = new float[BusCount];
        private readonly GroupDefinition[] _duckOwner = new GroupDefinition[BusCount];

        public void SetBusMuted(AudioBus bus, bool muted)
        {
            _muted[(int)bus] = muted;
            RecomputeBusLinear();
        }

        public void SetBusUserDb(AudioBus bus, float db)
        {
            _userDb[(int)bus] = db;
            RecomputeBusLinear();
        }

        // Bus gain including Master, unclamped; 0 when muted. Music uses it outside the voice pool.
        public float GetBusLinear(AudioBus bus) => _busLinear[(int)bus];

        public float GetDuckDb(AudioBus bus) => _duckDb[(int)bus];

        private bool IsMuted(AudioBus bus) => _muted[(int)bus] || _muted[(int)AudioBus.Master];

        // Positive dB anywhere in the chain is clamped here rather than clipping in the backend.
        private float VoiceGain(float voiceLinear, AudioBus bus)
        {
            float gain = voiceLinear * _busLinear[(int)bus];
            return gain > 1f ? 1f : gain;
        }

        private void RecomputeBusLinear()
        {
            float masterDb = _userDb[0] + _duckDb[0];
            for (int b = 0; b < BusCount; b++)
            {
                float db = b == 0 ? masterDb : masterDb + _userDb[b] + _duckDb[b];
                _busLinear[b] = IsMuted((AudioBus)b) ? 0f : AudioMath.ToLinear(db);
            }
        }

        private void RegisterDucker(GroupDefinition group)
        {
            if (!group.Ducks) return;
            int target = (int)group.DuckTarget;
            if (_duckOwner[target] != null && !ReferenceEquals(_duckOwner[target], group))
            {
                _logError($"[Audio] Group '{group.Name}' ducks {group.DuckTarget}, which '{_duckOwner[target].Name}' " +
                          "already ducks. One ducker per bus; this group's ducking is ignored.");
                return;
            }
            _duckOwner[target] = group;
        }

        private void ReleaseDucker(GroupDefinition group)
        {
            for (int b = 0; b < BusCount; b++)
            {
                if (!ReferenceEquals(_duckOwner[b], group)) continue;
                _duckOwner[b] = null;
                _duckDb[b] = 0f;
            }
            RecomputeBusLinear();
        }

        // Moves each duck toward -DuckDb while its owner has a voice playing and back to 0 otherwise,
        // at a rate that covers the full depth in AttackMs (down) or ReleaseMs (up).
        private void AdvanceDucking(float dt)
        {
            bool changed = false;
            for (int b = 0; b < BusCount; b++)
            {
                var owner = _duckOwner[b];
                if (owner == null) continue;

                float target = owner.Playing > 0 ? owner.DuckDb : 0f;
                float current = _duckDb[b];
                if (current == target) continue;

                int ms = target < current ? owner.DuckAttackMs : owner.DuckReleaseMs;
                if (ms <= 0)
                {
                    current = target;
                }
                else
                {
                    float step = Math.Abs(owner.DuckDb) * dt * 1000f / ms;
                    current = target < current ? Math.Max(target, current - step) : Math.Min(target, current + step);
                }

                _duckDb[b] = current;
                changed = true;
            }
            if (changed) RecomputeBusLinear();
        }
    }
}
