using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Resident full-screen curtain shown by GameLifecycleManager around every state transition to
    // hide the CloseAllAsync -> factory load -> ShowAsync blank-screen gap. Extends UIViewBase (not
    // UIView<T>) so UIViewRegistry.AutoRegister never picks it up — it stays off the nav stack and
    // is never routed through UIViewFactory's async load path, which would recreate the same gap.
    //
    // Visual content (background, spinner, logo) is authored per-game as child GameObjects under
    // this component's prefab/scene instance; this class only owns show/hide/min-display timing.
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]
    public class TransitionOverlayView : UIViewBase, ITransitionOverlay
    {
        [SerializeField] private float _minDisplaySeconds = 0.3f;

        private float _shownAt;

        public override UILayer Layer => UILayer.Overlay;
        public bool IsShown => IsVisible;

        protected override void Awake()
        {
            base.Awake();
            // Pre-hide so the first frame after scene load never flashes the overlay.
            CanvasGroup.alpha = 0f;
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        public override async UniTask ShowAsync(CancellationToken externalCt = default)
        {
            if (IsShown) return;
            _shownAt = Time.unscaledTime;
            await base.ShowAsync(externalCt);
        }

        public override async UniTask HideAsync(CancellationToken externalCt = default)
        {
            if (!IsShown) return;

            var remaining = _minDisplaySeconds - (Time.unscaledTime - _shownAt);
            if (remaining > 0f)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(remaining), DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update, externalCt);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation during the min-display wait must not skip the actual hide below —
                    // base.HideAsync has its own cancellation handling and still resets
                    // IsVisible/SetActive(false), so the overlay never gets stuck shown.
                }
            }

            await base.HideAsync(externalCt);
        }

        internal override UniTask InitializeNonGenericAsync(IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            => throw new NotSupportedException(
                "TransitionOverlayView is a resident overlay, never factory-created. " +
                "Place it directly in the UIRoot hierarchy under the Overlay layer.");
    }
}
