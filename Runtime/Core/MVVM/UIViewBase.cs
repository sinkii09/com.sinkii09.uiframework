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
        [SerializeField] private UITransitionType _showTransition = UITransitionType.Fade;
        [SerializeField] private UITransitionType _hideTransition = UITransitionType.Fade;

        private IUIAnimator _animator;

        public string ViewId => GetType().Name;
        public bool IsVisible { get; private set; }
        public UITransitionType Transition => _showTransition;

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
                IsVisible = true; // set after animation completes, not at start
            }
            catch (OperationCanceledException)
            {
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
