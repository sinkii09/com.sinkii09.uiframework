using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]

    public abstract class UIViewBase : MonoBehaviour, IUIView
    {
        // Null = instant show/hide (no animation). Assign ScriptableObject transitions in Inspector.
        [SerializeField] private UITransition _showTransition;
        [SerializeField] private UITransition _hideTransition;

        private IUIAnimator _animator;

        public string ViewId => GetType().Name;
        public bool IsVisible { get; private set; }
        public virtual UILayer Layer => UILayer.Screen;

        // Cached in Awake — used by UITransition subclasses (FadeTransition, ScaleTransition, SlideTransition).
        public CanvasGroup CanvasGroup { get; private set; }
        public RectTransform RectTransform { get; private set; }

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
            RectTransform = GetComponent<RectTransform>();
        }

        [Inject]
        private void Construct(IUIAnimator animator) => _animator = animator;

        // IUIView contract — component warmup only.
        // Not virtual: prevents silent shadowing in UIView<T>, which uses a different InitializeAsync signature.
        public UniTask InitializeAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        public virtual async UniTask ShowAsync(CancellationToken externalCt = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                externalCt, destroyCancellationToken);
            try
            {
                OnPrepareForShow();
                gameObject.SetActive(true);
                if (_animator != null)
                    await _animator.ShowAsync(this, _showTransition, cts.Token);
                await OnShowAsync(cts.Token);
                IsVisible = true;
            }
            catch (OperationCanceledException)
            {
                IsVisible = false;
                gameObject.SetActive(false);
            }
            catch
            {
                // Non-cancellation exception from animator or OnShowAsync — deactivate before rethrowing
                // so the GO is not left active with IsVisible = false.
                IsVisible = false;
                gameObject.SetActive(false);
                throw;
            }
        }

        public virtual async UniTask HideAsync(CancellationToken externalCt = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                externalCt, destroyCancellationToken);
            try
            {
                if (_animator != null)
                    await _animator.HideAsync(this, _hideTransition, cts.Token);
                await OnHideAsync(cts.Token);
                IsVisible = false;
                gameObject.SetActive(false);
            }
            catch (OperationCanceledException)
            {
                IsVisible = false;
                gameObject.SetActive(false);
            }
        }

        // Called by ShowAsync before SetActive(true) — override to reset visual state (scales, alpha)
        // so the first rendered frame after activation is already at the animation start position.
        protected virtual void OnPrepareForShow() { }
        protected virtual UniTask OnShowAsync(CancellationToken ct) => UniTask.CompletedTask;
        protected virtual UniTask OnHideAsync(CancellationToken ct) => UniTask.CompletedTask;

        // Non-generic bridge for UIViewFactory's type-erased creation path.
        // Each concrete UIView<TViewModel> subclass overrides this to cast and delegate to InitializeAsync.
        internal abstract UniTask InitializeNonGenericAsync(IViewModel viewModel, IObjectResolver scope, CancellationToken ct);

        // Package-internal factory hook — called before re-using a cached instance instead of destroying + re-instantiating.
        // UIViewBase has no per-instance state; UIView<TViewModel> overrides to reset scope, bindings, and init flag.
        internal virtual void FactoryReset() { }
    }
}
