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
            if (_safeArea == null)
            {
                Debug.LogError("[UIRootSetup] ISafeAreaProvider not injected — ensure UIFrameworkLifetimeScope is in the scene.", this);
                return;
            }
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
