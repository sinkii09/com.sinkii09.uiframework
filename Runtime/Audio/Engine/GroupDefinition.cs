namespace Sinkii09.UIFramework
{
    // The engine's copy of a SoundGroup, so the engine never touches a UnityEngine.Object.
    internal sealed class GroupDefinition
    {
        public string Name = "group";
        public AudioBus Bus = AudioBus.Sfx;
        public float VolumeDb;
        public int Priority = 128;
        public int MaxVoices = 8;
        public int MaxInstancesPerEvent = 4;
        public int ReservedVoices;
        public LimitPolicy OnLimit = LimitPolicy.StealOldest;
        public float CooldownSeconds;
        public int StopFadeMs = 10;

        public bool Ducks;
        public AudioBus DuckTarget;
        public float DuckDb;
        public int DuckAttackMs;
        public int DuckReleaseMs;

        // Engine-owned runtime state.
        internal int Index = -1;
        internal int Playing;
    }
}
