using System;

namespace Sinkii09.UIFramework
{
    internal static class AudioMath
    {
        // Treated as silence; also keeps ToLinear from returning denormals.
        public const float SilenceDb = -80f;
        public const float MinPitch = 0.01f;
        public const float MaxPitch = 3f;

        public static float ToLinear(float db)
            => db <= SilenceDb ? 0f : (float)Math.Pow(10.0, db / 20.0);

        public static float SemitonesToPitch(float semitones)
        {
            float ratio = (float)Math.Pow(2.0, semitones / 12.0);
            return ratio < MinPitch ? MinPitch : ratio > MaxPitch ? MaxPitch : ratio;
        }
    }

    // xorshift32: deterministic for a seed, so container and jitter tests are exact, and a struct
    // so drawing a number never allocates.
    internal struct AudioRandom
    {
        private uint _state;

        public AudioRandom(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int Next(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(NextUInt() % (uint)maxExclusive);

        // Uniform in [-1, 1].
        public float NextSigned() => (NextUInt() & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
    }
}
