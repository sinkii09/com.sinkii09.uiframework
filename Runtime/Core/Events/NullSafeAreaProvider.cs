using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Default ISafeAreaProvider when no SafeAreaProvider exists in the scene hierarchy.
    // Mirrors NullTransitionOverlay: full-screen Rect, not Rect.zero — UIRootSetup.ApplySafeArea
    // divides by Screen.width/height to compute anchors, so a zero Rect would collapse the safe
    // area panel to a zero-size box and blank the UI, not just skip the feature.
    public sealed class NullSafeAreaProvider : ISafeAreaProvider
    {
        public Rect SafeArea => new Rect(0, 0, Screen.width, Screen.height);

        // Fires once on subscribe, then completes — there is no notch to react to in a Null-Object
        // scene, so no ongoing reactive updates are needed.
        public Observable<Rect> OnChanged => Observable.Return(SafeArea);
    }
}
