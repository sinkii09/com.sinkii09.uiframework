namespace Sinkii09.UIFramework
{
    // Registering and forgetting events and groups. Allocation happens here (definitions, runtime
    // arrays), never on Play.
    internal sealed partial class AudioEngine
    {
        public int GroupCount => _groups.Count;

        public void RegisterGroup(GroupDefinition group)
        {
            if (group.Index >= 0) return;
            group.Index = _groups.Count;
            _groups.Add(group);
            RegisterDucker(group);
            WarnIfOverReserved();
        }

        // Called once a group's last event is gone, so a group reloaded as a new asset instance
        // (Addressables) does not leave the old one holding a reservation or a bus's ducker slot.
        public void UnregisterGroup(GroupDefinition group)
        {
            if (group.Index < 0) return;
            _groups.Remove(group);
            for (int i = 0; i < _groups.Count; i++) _groups[i].Index = i;
            group.Index = -1;
            ReleaseDucker(group);
        }

        public bool RegisterEvent(EventDefinition sound)
        {
            if (sound.ClipCount <= 0)
            {
                _logError($"[Audio] '{sound.Path}' has no clips; it was not registered.");
                return false;
            }
            if (_events.TryGetValue(sound.Id, out var existing))
            {
                if (ReferenceEquals(existing, sound)) return true;
                _logError($"[Audio] SoundId collision: '{sound.Path}' and '{existing.Path}' share {sound.Id}. " +
                          "The second was not registered; rename one path.");
                return false;
            }

            RegisterGroup(sound.Group);
            SoundVariation.Prepare(sound);
            _events.Add(sound.Id, sound);
            return true;
        }

        // Cuts every instance of the event at once (its clips are about to be released) and forgets it.
        public void UnregisterEvent(SoundId id)
        {
            if (!_events.TryGetValue(id, out var sound)) return;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].State != InstanceState.Free && ReferenceEquals(_slots[i].Event, sound))
                    Kill(i);
            _events.Remove(id);
        }

        public bool TryGetEvent(SoundId id, out EventDefinition sound) => _events.TryGetValue(id, out sound);
    }
}
