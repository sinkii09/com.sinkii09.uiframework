using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    // One reusable full-rect dimming Image, parked directly beneath whichever view declares
    // NeedsBackdrop in UIViewPolicyConfig. Saves every popup prefab hand-rolling its own dim
    // panel, and keeps the dim consistent across a project.
    //
    // Why not UIRootLayerRefs.BlockLayersBelow: that toggles GraphicRaycaster.enabled per LAYER,
    // so it cannot express "dim behind this popup but not behind the other popup on the same
    // layer". A real GameObject with an explicit sibling index can.
    //
    // Registered unconditionally by UIFrameworkLifetimeScope (VContainer ignores optional
    // constructor defaults, so a conditional registration would break UINavigator's construction).
    // With no policy asset assigned nothing ever declares NeedsBackdrop, so it simply never shows.
    public sealed class UIBackdrop : IDisposable
    {
        private readonly UIViewPolicyResolver _policies;
        private readonly Color _color;

        private GameObject _instance;
        private RectTransform _rect;

        public UIBackdrop(UIViewPolicyResolver policies, UIFrameworkConfig config)
        {
            _policies = policies;
            _color = config != null ? config.BackdropColor : new Color(0f, 0f, 0f, 0.5f);
        }

        // Shows the backdrop directly under `top` if that view's policy asks for it, otherwise
        // hides it. Called on every navigation change, so it must be cheap and idempotent.
        //
        // `isPending` = the caller is about to show this view but hasn't yet (UINavigator refreshes
        // with the incoming view before PushAsync so blocking covers the entrance animation). It
        // is the only reason to accept a view whose GameObject is still inactive.
        public void Refresh(IUIView top, bool isPending = false)
        {
            if (top is not UIViewBase view || view == null)
            {
                Hide();
                return;
            }

            // Never dim for a view that isn't on screen and isn't on its way there. This is the
            // softlock guard: if HideAsync throws, UIViewBase has already deactivated the view but
            // NavigationStack.PopAsync rethrows BEFORE removing it, so the navigator's finally
            // refreshes against a deactivated top-of-stack view. Without this check the backdrop
            // would come up full-screen with nothing above it and no way to dismiss it.
            if (!isPending && !view.gameObject.activeSelf)
            {
                Hide();
                return;
            }

            if (_policies == null || !_policies.NeedsBackdrop(view.GetType()))
            {
                Hide();
                return;
            }

            var parent = view.transform.parent as RectTransform;
            if (parent == null)
            {
                // A view not parented into a UIRoot layer has no meaningful place to put a
                // backdrop; better to show nothing than to attach it to the scene root.
                Hide();
                return;
            }

            EnsureInstance();

            _rect.SetParent(parent, false);
            StretchToParent(_rect);

            // Order matters, and "move the backdrop to the view's index" does NOT work: shifting
            // the backdrop up to the view's slot displaces the view DOWNWARD, leaving the dim on
            // top of the popup it is meant to sit behind. Raising both to the end in sequence is
            // unambiguous — backdrop second-from-top, view on top.
            //
            // Raising the VIEW also normalises a stale sibling index: ReparentToLayer only runs on
            // first creation, so a cached view shown again keeps its old index and can sit under a
            // newer sibling. Deliberately scoped to backdrop-using views, so projects not using the
            // feature see no z-order change at all.
            _rect.SetAsLastSibling();
            view.transform.SetAsLastSibling();

            _instance.SetActive(true);
        }

        public void Hide()
        {
            if (_instance != null) _instance.SetActive(false);
        }

        public void Dispose()
        {
            if (_instance != null) UnityEngine.Object.Destroy(_instance);
            _instance = null;
            _rect = null;
        }

        // Exposed for tests to assert placement without reaching through the scene.
        internal GameObject InstanceForTests => _instance;

        private void EnsureInstance()
        {
            // != null, not ??: the GameObject may have been destroyed by a scene unload, in which
            // case the C# reference is non-null but Unity reports it as null.
            if (_instance != null) return;

            _instance = new GameObject("__UIBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _rect = (RectTransform)_instance.transform;

            var image = _instance.GetComponent<Image>();
            image.color = _color;
            image.raycastTarget = true;   // swallows clicks aimed past the popup
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
