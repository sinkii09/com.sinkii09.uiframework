using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public interface ISafeAreaProvider
    {
        Rect SafeArea { get; }
        // BehaviorSubject semantics: replays the current value immediately on subscribe.
        // Safe to read in InitializeAsync — always returns a valid (non-zero) Rect after first frame.
        Observable<Rect> OnChanged { get; }
    }
}
