using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    public static class UIBindingExtensions
    {
        // Generic one-way: observable → any UI element
        public static IDisposable BindTo<TValue, TTarget>(
            this Observable<TValue> source,
            TTarget target,
            Action<TValue, TTarget> setter)
            where TTarget : class
        {
            return source.Subscribe(v => setter(v, target));
        }

        // Observable<TValue> → TMP_Text (with optional string formatter)
        public static IDisposable BindToText<TValue>(
            this Observable<TValue> source,
            TMP_Text label,
            Func<TValue, string> formatter = null)
        {
            return source.Subscribe(v =>
                label.text = formatter != null ? formatter(v) : v?.ToString() ?? string.Empty);
        }

        // bool → GameObject.SetActive
        public static IDisposable BindToActive(
            this Observable<bool> source,
            GameObject target)
        {
            return source.Subscribe(v => target.SetActive(v));
        }

        // float → Image.fillAmount (clamped 0–1)
        public static IDisposable BindToFillAmount(
            this Observable<float> source,
            Image image)
        {
            return source.Subscribe(v => image.fillAmount = Mathf.Clamp01(v));
        }

        // bool → Button.interactable
        public static IDisposable BindToInteractable(
            this Observable<bool> source,
            Button button)
        {
            return source.Subscribe(v => button.interactable = v);
        }

        // float → CanvasGroup.alpha (clamped 0–1)
        public static IDisposable BindToAlpha(
            this Observable<float> source,
            CanvasGroup group)
        {
            return source.Subscribe(v => group.alpha = Mathf.Clamp01(v));
        }

        // Two-way: Toggle ↔ ReactiveProperty<bool>
        // Usage: toggle.BindTwoWay(vm.IsMuted, ref _showDisposables);
        // UnityAction<T> used explicitly — not interchangeable with Action<T> for UnityEvent listeners.
        public static void BindTwoWay(
            this Toggle toggle,
            ReactiveProperty<bool> property,
            ref DisposableBag disposables)
        {
            property.Subscribe(v => { if (toggle.isOn != v) toggle.isOn = v; })
                .AddTo(ref disposables);

            UnityAction<bool> handler = v => property.Value = v;
            toggle.onValueChanged.AddListener(handler);
            Disposable.Create(() => toggle.onValueChanged.RemoveListener(handler))
                .AddTo(ref disposables);
        }

        // Two-way: TMP_InputField ↔ ReactiveProperty<string>
        // Usage: inputField.BindTwoWay(vm.SearchText, ref _showDisposables);
        public static void BindTwoWay(
            this TMP_InputField inputField,
            ReactiveProperty<string> property,
            ref DisposableBag disposables)
        {
            property.Subscribe(v => { if (inputField.text != v) inputField.text = v; })
                .AddTo(ref disposables);

            UnityAction<string> handler = v => property.Value = v;
            inputField.onValueChanged.AddListener(handler);
            Disposable.Create(() => inputField.onValueChanged.RemoveListener(handler))
                .AddTo(ref disposables);
        }

        // Button click → Action, with automatic listener removal on dispose.
        // Prevents stale delegate accumulation when the factory reuses a cached view.
        // Usage: _btn.BindButton(vm.OnClick, ref _showDisposables);
        public static void BindButton(
            this Button button,
            UnityAction handler,
            ref DisposableBag disposables)
        {
            button.onClick.AddListener(handler);
            Disposable.Create(() => button.onClick.RemoveListener(handler))
                .AddTo(ref disposables);
        }

        // Button click → async handler, with re-entrancy protection and the same automatic listener
        // removal as BindButton. Use this whenever the handler awaits.
        //
        // Why it exists: the usual shape is a synchronous UnityAction that fires .Forget(), and
        // nothing then stops the second, third or Nth press from launching that many concurrent
        // operations. UINavigator's own _isTransitioning guard does not help — it protects
        // navigation only (and by silently dropping the call), never the game's own async work.
        //
        // `ct` should be the ViewModel's ShowToken, so an in-flight operation is cancelled when the
        // view hides. Note that disposing `disposables` removes the LISTENER but does not cancel a
        // RUNNING operation — cancellation is the token's job, not the bag's.
        //
        // disableWhileRunning defaults to false deliberately. The re-entrancy guard is what makes
        // this correct; greying the button out is cosmetic, and it is the only mode that conflicts
        // with BindToInteractable on the same button (a ViewModel-pushed change arriving mid-flight
        // is overwritten by the restore below). Opt in only when nothing else drives that button.
        // Usage: _buyBtn.BindButtonAsync(vm.BuyAsync, ref _showDisposables, ShowToken);
        public static void BindButtonAsync(
            this Button button,
            Func<CancellationToken, UniTask> handler,
            ref DisposableBag disposables,
            CancellationToken ct = default,
            bool disableWhileRunning = false)
        {
            bool running = false;

            UnityAction listener = () =>
            {
                // Not a programmer error — a user double-tapping is expected input, so no log.
                if (running) return;
                // Checked before touching interactable, so an already-dead binding does not
                // disable-then-restore for a handler that would immediately throw OCE.
                if (ct.IsCancellationRequested) return;
                running = true;
                RunAsync().Forget();
            };

            button.onClick.AddListener(listener);
            Disposable.Create(() => button.onClick.RemoveListener(listener))
                .AddTo(ref disposables);

            async UniTaskVoid RunAsync()
            {
                // Declared out here so the finally can see it, but assigned INSIDE the try:
                // `running` is already true by this point, so anything that throws before the
                // finally would latch the guard and kill the button for the binding's whole life.
                bool wasInteractable = false;
                try
                {
                    // Captured, never assumed to be true: restoring a hardcoded `true` would
                    // silently re-enable a button whose ViewModel had deliberately bound it false.
                    wasInteractable = button.interactable;
                    if (disableWhileRunning) button.interactable = false;
                    await handler(ct);
                }
                catch (OperationCanceledException)
                {
                    // Expected whenever the view hides mid-operation.
                }
                catch (Exception e)
                {
                    // Matches TooltipService.Enqueue's policy: a faulted handler must not take the
                    // guard down with it, or the button is dead for the rest of the view's life.
                    Debug.LogException(e);
                }
                finally
                {
                    running = false;
                    // Unity fake-null: the view can be destroyed while the operation is in flight.
                    if (disableWhileRunning && button != null)
                        button.interactable = wasInteractable;
                }
            }
        }
    }
}
