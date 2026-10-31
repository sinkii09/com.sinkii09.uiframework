namespace Sinkii09.UIFramework
{
    // Copies the authored assets into the engine's plain definitions. Done once per registration,
    // so later edits to an asset at runtime are not picked up until its bank is reloaded.
    internal static class SoundDefinitionFactory
    {
        public static GroupDefinition BuildGroup(SoundGroup group) => new()
        {
            Name = group.name,
            Bus = group.Bus,
            VolumeDb = group.VolumeDb,
            Priority = group.Priority,
            MaxVoices = group.MaxVoices,
            MaxInstancesPerEvent = group.MaxInstancesPerEvent,
            ReservedVoices = group.ReservedVoices,
            OnLimit = group.OnLimit,
            CooldownSeconds = group.CooldownSeconds,
            StopFadeMs = group.StopFadeMs,
            Ducks = group.Ducking.Enabled,
            DuckTarget = group.Ducking.Target,
            DuckDb = group.Ducking.DuckDb,
            DuckAttackMs = group.Ducking.AttackMs,
            DuckReleaseMs = group.Ducking.ReleaseMs,
        };

        public static EventDefinition BuildEvent(SoundEvent sound, GroupDefinition group)
        {
            var definition = new EventDefinition(sound.Id, group, sound.Clips?.Length ?? 0)
            {
                Payload = sound,
                Path = sound.Path,
                Container = sound.Container,
                AvoidRepeat = sound.AvoidRepeat,
                VolumeDb = sound.VolumeDb,
                VolumeJitterDb = sound.VolumeJitterDb,
                PitchSemitones = sound.PitchSemitones,
                PitchJitterSemitones = sound.PitchJitterSemitones,
                StartDelaySeconds = sound.StartDelaySeconds,
                Loop = sound.Loop,
            };
            if (sound.OverridePriority) definition.Priority = sound.Priority;
            if (sound.OverrideMaxInstances) definition.MaxInstances = sound.MaxInstances;
            if (sound.OverrideCooldown) definition.CooldownSeconds = sound.CooldownSeconds;
            return definition;
        }
    }
}
