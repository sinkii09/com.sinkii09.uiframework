using R3;
using System;
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
    }
}
