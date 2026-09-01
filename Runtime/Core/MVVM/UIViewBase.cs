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
        // [UIOptional] because null is a documented, meaningful value here — without it every view
        // that deliberately has no transition would be reported as misconfigured.
        [SerializeField, UIOptional] private UITransition _showTransition;
        [SerializeField, UIOptional] private UITransition _hideTransition;

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

            // Editor/development builds only — compiles away entirely in a release player.
            // Runs here rather than in BindViewModel so the report names the offending prefab
            // before any binding lambda can dereference the missing reference.
            //
            // This Awake is virtual, so a derived view that overrides it without calling
            // base.Awake() skips validation. UIViewFactory therefore repeats the call on the
            // creation path, which no override can bypass; the validator dedupes per type, so the
            // second call costs nothing for well-behaved views. Scene-placed views that the factory
            // never creates (the transition overlay, tooltips) are covered by this call alone.
            UIViewValidator.ValidateSerializedRefs(this);
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
                // Owned here, not by the animator: a view must not become clickable until its own
                // setup work (data population, binding wiring) has actually finished.
                if (CanvasGroup != null)
                {
                    CanvasGroup.interactable = true;
                    CanvasGroup.blocksRaycasts = true;
                }
                IsVisible = true;
            }
            catch (OperationCanceledException)
            {
                IsVisible = false;
                gameObject.SetActive(false);
                // Rethrow: NavigationStack.PushAsync must NOT add a view that was never shown, and
                // every upstream caller composes on this token. HideAsync deliberately does not
                // rethrow — see its comment below.
                throw;
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
                // Deliberately NOT rethrown — asymmetric with ShowAsync on purpose.
                // NavigationStack.PopAsync hides first, then removes; the view IS hidden at this
                // point (IsVisible/SetActive already applied above), so removing it from the stack
                // is correct. Rethrowing here would abort the pop and strand an invisible view on
                // the stack — strictly worse. ClearAsync's per-view catch-and-continue relies on
                // hide failures not aborting its loop for the same reason. Do not "fix" this to
                // match ShowAsync's rethrow — the two are not symmetric by design.
                IsVisible = false;
                gameObject.SetActive(false);
            }
            catch
            {
                // Non-cancellation exception from animator or OnHideAsync — deactivate before
                // rethrowing so the GO is not left active/stuck with IsVisible still true.
                IsVisible = false;
                gameObject.SetActive(false);
                throw;
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
