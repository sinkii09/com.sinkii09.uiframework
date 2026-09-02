using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// One toast row. A control, not a view: it never enters the navigation stack and is owned by
    /// <see cref="NotificationHostView"/>, which instantiates and reuses a small fixed set of them.
    ///
    /// <para>Alpha is driven externally by <see cref="NotificationService"/>'s per-slot fade state,
    /// advanced once per tick. There is deliberately no async show/hide here: an awaited fade would
    /// reintroduce the cancelled-tail hazard where a superseded hide deactivates a slot the next
    /// notification has already claimed, stranding an invisible toast in a live slot forever.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class NotificationItemView : UIControlBase
    {
        [SerializeField] private TMP_Text _title;

        [Tooltip("Optional. Hidden when the content has no body text.")]
        [SerializeField] private TMP_Text _body;

        [Tooltip("Optional. Hidden when the content has no icon.")]
        [SerializeField] private Image _icon;

        [Tooltip("Optional. Hidden unless the merged quantity is greater than one.")]
        [SerializeField] private TMP_Text _quantity;

        private CanvasGroup _canvasGroup;

        protected override void OnInitialize()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            // Toasts are display-only in this version. The host is non-blocking as a whole, but a
            // child carrying its own CanvasGroup would otherwise re-enable raycasts for its subtree.
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            SetAlpha(0f);
        }

        protected override void OnDispose() { }

        /// <summary>Full rebind — called on first show and again on every merge.</summary>
        public void Bind(in NotificationContent content)
        {
            if (_title != null) _title.text = content.Title;

            if (_body != null)
            {
                bool hasBody = !string.IsNullOrEmpty(content.Body);
                _body.gameObject.SetActive(hasBody);
                if (hasBody) _body.text = content.Body;
            }

            if (_icon != null)
            {
                _icon.gameObject.SetActive(content.Icon != null);
                if (content.Icon != null) _icon.sprite = content.Icon;
            }

            if (_quantity != null)
            {
                bool stacked = content.Quantity > 1;
                _quantity.gameObject.SetActive(stacked);
                if (stacked) _quantity.text = $"x{content.Quantity}";
            }
        }

        public void SetAlpha(float alpha)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = Mathf.Clamp01(alpha);
        }

        public void SetActive(bool active)
        {
            if (this == null) return;
            gameObject.SetActive(active);
        }
    }
}
