using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Default UI cues, assigned to AudioConfig.Theme. UIButtonSound plays ButtonClick/Toggle, UINavigator
    // plays PopupShow/PopupHide for Popup-layer views. Error is never played by the framework; it is
    // here so every game names its error cue in the same place.
    [CreateAssetMenu(menuName = "UIFramework/Audio/UI Sound Theme", fileName = "UISoundTheme")]
    public sealed class UISoundTheme : ScriptableObject
    {
        public SoundRef ButtonClick;
        public SoundRef Toggle;
        public SoundRef PopupShow;
        public SoundRef PopupHide;
        public SoundRef Error;
    }
}
