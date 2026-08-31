using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Base for every tooltip view. Extends UIViewBase (not UIView<T>) so UIViewRegistry.AutoRegister
    // never picks it up: it stays off the nav stack and is never routed through UIViewFactory,
    // whose whole job is ViewModel + child-scope wiring that a tooltip does not want.
    //
    // Subclass this for a custom tooltip look, set _viewKey, place it under the Tooltip layer of
    // the UIRoot, and return that same key from your ITooltipPayload.ViewKey.
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]
    public abstract class TooltipViewBase : UIViewBase
    {
        [SerializeField]
        [Tooltip("Key this view registers under. Leave empty for the built-in TooltipContent view. " +
                 "Must match ITooltipPayload.ViewKey.")]
        private string _viewKey;

        public string ViewKey => _viewKey;

        public override UILayer Layer => UILayer.Tooltip;

        // Render `payload` into this view's widgets. Called immediately before the show animation,
        // and again on every re-show — treat it as a full rebind, not an incremental update.
        public abstract void Bind(ITooltipPayload payload);

        protected override void Awake()
        {
            base.Awake();

            // Pre-hide so the first frame after scene load never flashes the tooltip.
            CanvasGroup.alpha = 0f;
            gameObject.SetActive(false);

            // THE FLICKER LOOP: if a tooltip takes raycasts it appears under the cursor, the
            // pointer "exits" the anchor beneath it, the tooltip hides, the pointer re-enters, and
            // it strobes. Belt and braces, because these defeat different things: the CanvasGroup
            // covers the subtree, while raycastTarget=false survives a child that carries its own
            // CanvasGroup with blocksRaycasts=true.
            MakeNonBlocking();
            StripRaycasts(gameObject);
        }

        public override async UniTask ShowAsync(CancellationToken externalCt = default)
        {
            try
            {
                await base.ShowAsync(externalCt);
            }
            finally
            {
                // UIViewBase.ShowAsync sets interactable/blocksRaycasts = true once its own setup
                // finishes (UIViewBase.cs:54-56), and DOTweenUIAnimator's cancel path does the
                // same. Correct for a normal view, fatal for a tooltip — so re-assert afterwards,
                // in a finally so a cancelled or faulted show cannot leave it blocking either.
                MakeNonBlocking();
            }
        }

        public override async UniTask HideAsync(CancellationToken externalCt = default)
        {
            try
            {
                await base.HideAsync(externalCt);
            }
            finally
            {
                // DOTweenUIAnimator's HideAsync cancel path restores interactable/blocksRaycasts
                // to true (DOTweenUIAnimator.cs:101-102) on its way out. A cancelled hide would
                // otherwise leave a re-shown tooltip raycast-blocking and back in the flicker loop.
                MakeNonBlocking();
            }
        }

        // Strips raycasts from a subtree. Call this for stat rows and any other content a subclass
        // instantiates at runtime, which never passed through Awake's sweep.
        // protected, not internal: subclasses live in game assemblies, and internal would have made
        // this unreachable for exactly the callers the comment invites.
        protected void StripRaycasts(GameObject target)
        {
            foreach (var graphic in target.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        private void MakeNonBlocking()
        {
            if (CanvasGroup == null) return;
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;
        }

        // Sealed, and that is load-bearing: UIViewBase declares this `internal abstract`, so
        // without a concrete override here no assembly outside the framework could subclass
        // TooltipViewBase at all.
        internal sealed override UniTask InitializeNonGenericAsync(
            IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            => throw new NotSupportedException(
                "TooltipViewBase is a resident view, never factory-created. " +
                "Place it directly in the UIRoot hierarchy under the Tooltip layer.");
    }
}
