using R3;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Attach to the safe-area panel inside the UIRoot canvas.
    // Adjusts RectTransform anchors so all UI content respects device notches and rounded corners.
    [AddComponentMenu("UIFramework/UIRootSetup")]
    public class UIRootSetup : MonoBehaviour
    {
        [Inject] private ISafeAreaProvider _safeArea;
        [SerializeField] private RectTransform _safeAreaPanel;

        private void Start()
        {
            // No null-check on _safeArea: ISafeAreaProvider is now always registered (real or
            // NullSafeAreaProvider) by UIFrameworkLifetimeScope, and VContainer field injection
            // throws before Start() runs if it weren't — a post-injection null here was already
            // unreachable dead code.
            if (_safeAreaPanel == null)
            {
                Debug.LogError("[UIRootSetup] _safeAreaPanel not assigned in Inspector.", this);
                return;
            }

            _safeArea.OnChanged
                .Subscribe(ApplySafeArea)
                .AddTo(this);
            ApplySafeArea(_safeArea.SafeArea);
        }

        private void ApplySafeArea(Rect area)
        {
            var screen = new Vector2(Screen.width, Screen.height);
            _safeAreaPanel.anchorMin = area.position / screen;
            _safeAreaPanel.anchorMax = (area.position + area.size) / screen;
            _safeAreaPanel.offsetMin = _safeAreaPanel.offsetMax = Vector2.zero;
        }
    }
}
