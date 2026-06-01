using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    [RequireComponent(typeof(CanvasGroup))]
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

        protected virtual UniTask OnShowAsync(CancellationToken ct) => UniTask.CompletedTask;
        protected virtual UniTask OnHideAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
