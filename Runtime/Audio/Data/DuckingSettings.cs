using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // While any voice of the owning group plays, the target bus is pulled down by DuckDb. One ducker
    // per bus in v1: a second group targeting the same bus is refused at registration with an error.
    [Serializable]
    public struct DuckingSettings
    {
        public bool Enabled;
        public AudioBus Target;
        [Tooltip("Attenuation applied to the target bus while ducking, in dB. Negative.")]
        public float DuckDb;
        [Min(0)] public int AttackMs;
        [Min(0)] public int ReleaseMs;
    }
}
