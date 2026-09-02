using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework
{
    public static class UIBindingExtensions
    {
        // Set by UIFrameworkLifetimeScope, and cleared by it on teardown. A static hook rather than
        // a constructor dependency because these are extension methods on a static class.
        //
        // NULL IS A SUPPORTED STATE, not an error: every binding falls back to immediate delivery.
        // That is what lets EditMode tests — which build a ContainerBuilder rather than a
        // LifetimeScope, and so never install a scheduler — keep the pre-v3.0.0 synchronous
        // semantics with no changes.
        public static IUIRenderScheduler Scheduler { get; set; }

        // Domain reload can be disabled in play mode, which would otherwise carry a dead scheduler
        // from the previous session into this one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Scheduler = null;

        // The single place the coalescing rule lives. Every one-way binding routes through here.
        private static IDisposable Bind<TValue>(
            Observable<TValue> source, UIBindMode mode, Action<TValue> apply, Object target)
        {
            IUIRenderScheduler scheduler = Scheduler;

            if (mode == UIBindMode.Immediate || scheduler == null)
                return source.Subscribe(v => ApplySafe(apply, v));

            return new CoalescedBinding<TValue>(source, scheduler.Frames, apply, target);
        }

        // Both modes contain a faulting setter identically: logged, subscription kept. Without
        // this, an immediate binding's exception would reach R3's global unhandled handler, where
        // it is indistinguishable from any other error in the app.
        private static void ApplySafe<TValue>(Action<TValue> apply, TValue value)
        {
            try { apply(value); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Generic one-way: observable → any UI element.
        //
        // Immediate by default: the setter is arbitrary caller code, and coalescing DROPS
        // intermediate values — silent data loss for anything that accumulates or has side
        // effects. Opt into Coalesced only for a setter that is a pure display write.
        public static IDisposable BindTo<TValue, TTarget>(
            this Observable<TValue> source,
            TTarget target,
            Action<TValue, TTarget> setter,
            UIBindMode mode = UIBindMode.Immediate)
            where TTarget : class
        {
            return Bind(source, mode, v => setter(v, target), target as Object);
        }

        // Observable<TValue> → TMP_Text (with optional string formatter).
        // Coalesced by default: a pure display write, and the highest-traffic binding there is.
        public static IDisposable BindToText<TValue>(
            this Observable<TValue> source,
            TMP_Text label,
            Func<TValue, string> formatter = null,
            UIBindMode mode = UIBindMode.Coalesced)
        {
            return Bind(source, mode,
                v => label.text = formatter != null ? formatter(v) : v?.ToString() ?? string.Empty,
                label);
        }

        // bool → GameObject.SetActive.
        // Immediate by default: a coalesced SetActive(false) leaves the object raycastable for the
        // rest of the frame, so a "hidden" element can still swallow a click. Correctness, not
        // cosmetics.
        public static IDisposable BindToActive(
            this Observable<bool> source,
            GameObject target,
            UIBindMode mode = UIBindMode.Immediate)
        {
            return Bind(source, mode, v => target.SetActive(v), target);
        }

        // float → Image.fillAmount (clamped 0–1).
        // Coalesced by default: a pure display write.
        public static IDisposable BindToFillAmount(
            this Observable<float> source,
            Image image,
            UIBindMode mode = UIBindMode.Coalesced)
        {
            return Bind(source, mode, v => image.fillAmount = Mathf.Clamp01(v), image);
        }

        // bool → Button.interactable.
        // Immediate by default: interactivity is an input path. A one-frame-late
        // interactable = false leaves the button clickable for that frame.
        public static IDisposable BindToInteractable(
            this Observable<bool> source,
            Button button,
            UIBindMode mode = UIBindMode.Immediate)
        {
            return Bind(source, mode, v => button.interactable = v, button);
        }

        // float → CanvasGroup.alpha (clamped 0–1).
        // Immediate by default: alpha is the one bound property this framework also ANIMATES
        // (DOTweenUIAnimator writes it every frame during a fade). Coalescing would change which
        // writer wins the frame, altering the look of every existing transition.
        public static IDisposable BindToAlpha(
            this Observable<float> source,
            CanvasGroup group,
            UIBindMode mode = UIBindMode.Immediate)
        {
            return Bind(source, mode, v => group.alpha = Mathf.Clamp01(v), group);
        }

        // Two-way: Toggle ↔ ReactiveProperty<bool>
        // Usage: toggle.BindTwoWay(vm.IsMuted, ref _showDisposables);
        // UnityAction<T> used explicitly — not interchangeable with Action<T> for UnityEvent listeners.
        //
        // BOTH LEGS ARE IMMEDIATE AND MUST STAY THAT WAY. The VM→UI leg looks like a display path,
        // but its guard (`if (toggle.isOn != v)`) is evaluated at WRITE time. Coalesced, that
        // evaluation moves to flush time: a user who toggled again in the same frame fails the
        // equality check and has their own input overwritten with the older value. On the
        // TMP_InputField overload below the same defect eats a typed character and moves the caret.
        // This is an input path wearing a display path's clothes.
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
