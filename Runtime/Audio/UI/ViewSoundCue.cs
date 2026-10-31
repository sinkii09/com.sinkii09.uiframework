using System;

namespace Sinkii09.UIFramework
{
    public enum ViewSoundCueMode
    {
        /// <summary>The theme's popup cue on the Popup layer; silent on every other layer.</summary>
        Default,
        None,
        Custom,
    }

    /// <summary>What a view plays when UINavigator pushes (show) or pops (hide) it.</summary>
    [Serializable]
    public struct ViewSoundCue
    {
        public ViewSoundCueMode Mode;
        public SoundRef Custom;

        internal SoundId Resolve(UILayer layer, SoundRef themeCue)
        {
            switch (Mode)
            {
                case ViewSoundCueMode.Custom: return Custom.Id;
                case ViewSoundCueMode.None: return SoundId.Invalid;
                default: return layer == UILayer.Popup ? themeCue.Id : SoundId.Invalid;
            }
        }
    }
}
