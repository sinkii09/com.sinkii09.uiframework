using UnityEngine;

namespace Sinkii09.UIFramework
{
    // The unit of loading. An event is registered only while a bank holding it is loaded, so a game
    // with thousands of events holds only the banks its current state needs. Which banks stay loaded
    // for the session is AudioConfig.ResidentBankKeys' job, not a flag here: one source of truth.
    [CreateAssetMenu(menuName = "UIFramework/Audio/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        public SoundEvent[] Events = System.Array.Empty<SoundEvent>();
    }
}
