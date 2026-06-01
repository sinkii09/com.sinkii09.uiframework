using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [AddComponentMenu("UIFramework/SafeAreaProvider")]
    public sealed class SafeAreaProvider : MonoBehaviour, ISafeAreaProvider
    {
        private readonly ReactiveProperty<Rect> _safeArea = new();

        public Rect SafeArea => _safeArea.Value;
        public Observable<Rect> OnChanged => _safeArea;

        private void Awake() => UpdateSafeArea();

        private void OnDestroy() => _safeArea.Dispose();

        // Called by Unity whenever the RectTransform dimensions change (orientation, notch, etc.)
        private void OnRectTransformDimensionsChange() => UpdateSafeArea();

        private void UpdateSafeArea() => _safeArea.Value = Screen.safeArea;
    }
}
