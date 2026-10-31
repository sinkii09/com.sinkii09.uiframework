using System.Collections.Generic;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Stands in for the AudioSource pool. Voices stay "playing" until a test finishes them or a fade
    // completes, which is exactly the control the engine's reclaim and steal rules need.
    internal sealed class FakeVoiceBackend : IVoiceBackend
    {
        public FakeVoiceBackend(int voices)
        {
            VoiceCount = voices;
            Active = new bool[voices];
            Fading = new bool[voices];
            Gain = new float[voices];
            Pitch = new float[voices];
            Sound = new EventDefinition[voices];
        }

        public int VoiceCount { get; }
        public readonly bool[] Active;
        public readonly bool[] Fading;
        public readonly float[] Gain;
        public readonly float[] Pitch;
        public readonly EventDefinition[] Sound;
        public readonly List<(int Voice, int FadeMs)> Stops = new();
        public int Starts;

        public void Start(int voice, EventDefinition sound, int clipIndex, float gain, float pitch)
        {
            Active[voice] = true;
            Fading[voice] = false;
            Gain[voice] = gain;
            Pitch[voice] = pitch;
            Sound[voice] = sound;
            Starts++;
        }

        public void Stop(int voice, int fadeMs)
        {
            Stops.Add((voice, fadeMs));
            if (fadeMs <= 0)
            {
                Active[voice] = false;
                Fading[voice] = false;
                Sound[voice] = null;
            }
            else if (Active[voice])
            {
                Fading[voice] = true;
            }
        }

        public void SetGain(int voice, float gain) => Gain[voice] = gain;

        public bool IsFinished(int voice) => !Active[voice];

        public void CompleteFades()
        {
            for (int v = 0; v < VoiceCount; v++)
                if (Fading[v]) { Active[v] = false; Fading[v] = false; }
        }

        public void Finish(int voice) => Active[voice] = false;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int v = 0; v < VoiceCount; v++) if (Active[v]) n++;
                return n;
            }
        }

        public int ActiveOf(EventDefinition sound)
        {
            int n = 0;
            for (int v = 0; v < VoiceCount; v++) if (Active[v] && !Fading[v] && Sound[v] == sound) n++;
            return n;
        }
    }

    internal static class TestSounds
    {
        public static GroupDefinition Group(string name = "g", int maxVoices = 8, int perEvent = 8,
                                            LimitPolicy onLimit = LimitPolicy.StealOldest, int reserved = 0,
                                            AudioBus bus = AudioBus.Sfx, int priority = 128, float cooldown = 0f)
            => new()
            {
                Name = name, MaxVoices = maxVoices, MaxInstancesPerEvent = perEvent, OnLimit = onLimit,
                ReservedVoices = reserved, Bus = bus, Priority = priority, CooldownSeconds = cooldown,
            };

        public static EventDefinition Event(AudioEngine engine, string path, GroupDefinition group,
                                            int clips = 1, float delay = 0f)
        {
            var sound = new EventDefinition(SoundId.FromPath(path), group, clips)
            {
                Path = path,
                StartDelaySeconds = delay,
            };
            engine.RegisterEvent(sound);
            return sound;
        }

        public static PlayHandle Play(this AudioEngine engine, EventDefinition sound)
            => engine.Play(sound.Id, default, out _);

        public static PlayStatus PlayStatusOf(this AudioEngine engine, EventDefinition sound)
        {
            engine.Play(sound.Id, default, out var status);
            return status;
        }
    }
}
