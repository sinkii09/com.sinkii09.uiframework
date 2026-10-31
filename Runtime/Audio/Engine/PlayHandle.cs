using System;

namespace Sinkii09.UIFramework
{
    // Implemented by whatever issued a handle: the built-in engine today, a middleware-backed
    // service later. Public so another IAudioService can hand out handles without a breaking change.
    public interface IPlayHandleOwner
    {
        bool IsAlive(in PlayHandle handle);
        bool IsPlaying(in PlayHandle handle);
        void Stop(in PlayHandle handle, int fadeMs);
    }

    // A reference to one play. (slot, generation) means a handle kept after its sound finished, or
    // after the slot was reused by another sound, is simply stale: every call on it is a no-op.
    // default(PlayHandle) is the invalid handle every rejected or dropped request returns.
    public readonly struct PlayHandle : IEquatable<PlayHandle>
    {
        private readonly IPlayHandleOwner _owner;

        public PlayHandle(IPlayHandleOwner owner, int slot, int generation)
        {
            _owner = owner;
            Slot = slot;
            Generation = generation;
        }

        public int Slot { get; }
        public int Generation { get; }

        /// <summary>True while the play is pending, playing or fading out.</summary>
        public bool IsValid => _owner != null && _owner.IsAlive(this);

        /// <summary>True only while audible-or-about-to-be: playing, not pending and not fading out.</summary>
        public bool IsPlaying => _owner != null && _owner.IsPlaying(this);

        /// <summary>Stops the play. -1 uses the group's StopFadeMs; 0 cuts immediately.</summary>
        public void Stop(int fadeMs = -1) => _owner?.Stop(this, fadeMs);

        public bool Equals(PlayHandle other)
            => ReferenceEquals(_owner, other._owner) && Slot == other.Slot && Generation == other.Generation;

        public override bool Equals(object obj) => obj is PlayHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    }
}
