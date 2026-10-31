using System;

namespace Sinkii09.UIFramework
{
    // A sound's identity: FNV-1a 32 over the UTF-16LE bytes of its path. A struct so play calls never
    // allocate, and a hash so generated constants are plain integers. Codegen, registration and the
    // validator treat a collision as a hard error, never as a silent alias.
    public readonly struct SoundId : IEquatable<SoundId>
    {
        private const uint OffsetBasis = 2166136261;
        private const uint Prime = 16777619;

        public readonly uint Value;

        public SoundId(uint value) => Value = value;

        public static SoundId Invalid => default;
        public bool IsValid => Value != 0;

        public static SoundId FromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Invalid;

            uint hash = OffsetBasis;
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                hash = (hash ^ (byte)c) * Prime;
                hash = (hash ^ (byte)(c >> 8)) * Prime;
            }
            // 0 is reserved for Invalid, so a path that happens to hash to it must still be playable.
            return new SoundId(hash == 0 ? 1u : hash);
        }

        public bool Equals(SoundId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SoundId other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(SoundId a, SoundId b) => a.Value == b.Value;
        public static bool operator !=(SoundId a, SoundId b) => a.Value != b.Value;
        public override string ToString() => $"SoundId(0x{Value:X8})";
    }
}
