namespace Sinkii09.UIFramework
{
    // Voice limits and stealing. Scopes are checked event, then group, then global; at each, the
    // NEW sound's group OnLimit decides. A steal moves the victim to Stopping, which frees its place
    // under every cap at once; the new sound then still needs a free backend voice, which the fade
    // headroom provides unless it too is exhausted.
    internal sealed partial class AudioEngine
    {
        private enum LimitScope { Event, Group, Global }

        private bool TryStart(int slot)
        {
            ref var instance = ref _slots[slot];
            var sound = instance.Event;
            var group = sound.Group;

            if (sound.Playing >= sound.MaxInstances && !Steal(LimitScope.Event, sound, group, instance.Priority))
                return false;
            if (group.Playing >= group.MaxVoices && !Steal(LimitScope.Group, sound, group, instance.Priority))
                return false;
            if (GlobalAvailable(group) <= 0 && !Steal(LimitScope.Global, sound, group, instance.Priority))
                return false;
            if (_freeVoiceCount == 0) return false;

            int voice = _freeVoices[--_freeVoiceCount];
            instance.Voice = voice;
            instance.State = InstanceState.Playing;
            instance.Sequence = ++_sequence;
            sound.Playing++;
            group.Playing++;
            _playingTotal++;

            float gain = VoiceGain(instance.VoiceLinear, group.Bus);
            instance.AppliedGain = gain;
            _backend.Start(voice, sound, instance.ClipIndex, gain, instance.Pitch);
            _counters.Played++;
            return true;
        }

        // Voices group G may still start under the pool cap: what is left after every OTHER group's
        // unused reservation is held back. A group's own reservation never blocks itself.
        private int GlobalAvailable(GroupDefinition group)
        {
            int held = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                var other = _groups[i];
                if (ReferenceEquals(other, group)) continue;
                if (other.ReservedVoices > other.Playing) held += other.ReservedVoices - other.Playing;
            }
            return _playingCap - _playingTotal - held;
        }

        private void WarnIfOverReserved()
        {
            int reserved = 0;
            for (int i = 0; i < _groups.Count; i++) reserved += _groups[i].ReservedVoices;
            if (reserved > _playingCap)
                _logError($"[Audio] Groups reserve {reserved} voices but the pool has {_playingCap}; " +
                          "unreserved groups will be starved. Lower ReservedVoices or raise the pool size.");
        }

        private bool Steal(LimitScope scope, EventDefinition sound, GroupDefinition group, int priority)
        {
            var policy = group.OnLimit;
            if (policy == LimitPolicy.Reject) return false;

            int victim = -1;
            for (int i = 0; i < _slots.Length; i++)
            {
                ref var candidate = ref _slots[i];
                if (candidate.State != InstanceState.Playing || !InScope(scope, ref candidate, sound, group)) continue;

                if (policy == LimitPolicy.StealLowestPriority)
                {
                    if (candidate.Priority > priority) continue;
                    if (victim < 0 || candidate.Priority < _slots[victim].Priority
                        || (candidate.Priority == _slots[victim].Priority && candidate.Sequence < _slots[victim].Sequence))
                        victim = i;
                }
                else if (victim < 0 || candidate.Sequence < _slots[victim].Sequence)
                {
                    victim = i;
                }
            }

            if (victim < 0) return false;
            StopSlot(victim, -1);
            _counters.Stolen++;
            return true;
        }

        // A global steal may only take from the requester's own group or from a group playing above
        // its reservation, so a reservation actually protects what it reserves.
        private static bool InScope(LimitScope scope, ref InstanceSlot candidate, EventDefinition sound, GroupDefinition group)
        {
            switch (scope)
            {
                case LimitScope.Event: return ReferenceEquals(candidate.Event, sound);
                case LimitScope.Group: return ReferenceEquals(candidate.Event.Group, group);
                default:
                    var owner = candidate.Event.Group;
                    return ReferenceEquals(owner, group) || owner.Playing > owner.ReservedVoices;
            }
        }
    }
}
