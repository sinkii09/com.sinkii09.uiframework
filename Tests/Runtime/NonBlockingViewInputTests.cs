using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // UIViewBase.AcceptsInput replaced the finally-overrides TooltipViewBase/NotificationHostView used
    // to re-disable input after base.ShowAsync/HideAsync. These pin the exits those overrides covered.
    public class NonBlockingViewInputTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private GatedTooltipView CreateTooltip()
        {
            var view = new GameObject("GatedTooltip", typeof(RectTransform)).AddComponent<GatedTooltipView>();
            _gos.Track(view);
            return view;
        }

        [UnityTest]
        public IEnumerator Tooltip_ShowCompletes_StaysNonBlocking() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateTooltip();
            view.Gate.TrySetResult();

            await view.ShowAsync();

            Assert.IsTrue(view.IsVisible);
            Assert.IsFalse(view.CanvasGroup.interactable, "UIViewBase must not unlock a view that refuses input");
            Assert.IsFalse(view.CanvasGroup.blocksRaycasts);
        });

        [UnityTest]
        public IEnumerator Tooltip_ShowCancelledMidway_StaysNonBlocking() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateTooltip();
            using var cts = new CancellationTokenSource();

            var showTask = view.ShowAsync(cts.Token);
            // Simulates any writer between show start and cancel (a custom IUIAnimator, game code).
            view.CanvasGroup.interactable = true;
            view.CanvasGroup.blocksRaycasts = true;
            cts.Cancel();

            var error = await CaptureAsync(async () => await showTask);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsFalse(view.CanvasGroup.interactable);
            Assert.IsFalse(view.CanvasGroup.blocksRaycasts);
        });

        [UnityTest]
        public IEnumerator Tooltip_HideAfterInputWasForcedOn_EndsNonBlocking() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateTooltip();
            view.Gate.TrySetResult();
            await view.ShowAsync();
            view.CanvasGroup.interactable = true;
            view.CanvasGroup.blocksRaycasts = true;

            await view.HideAsync();

            Assert.IsFalse(view.CanvasGroup.interactable);
            Assert.IsFalse(view.CanvasGroup.blocksRaycasts);
        });

        [UnityTest]
        public IEnumerator NotificationHost_FromAwakeThroughShow_NeverBlocks() => UniTask.ToCoroutine(async () =>
        {
            // A bare host has no row prefab; the validator's report is expected setup noise here.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("_itemPrefab"));
            var host = new GameObject("Host", typeof(RectTransform)).AddComponent<NotificationHostView>();
            _gos.Track(host);

            // A fresh CanvasGroup defaults to interactable/blocksRaycasts = true.
            Assert.IsFalse(host.CanvasGroup.blocksRaycasts, "Awake must lock a view that refuses input");

            await host.ShowAsync();

            Assert.IsFalse(host.CanvasGroup.interactable);
            Assert.IsFalse(host.CanvasGroup.blocksRaycasts);
        });

        private sealed class GatedTooltipView : TooltipViewBase
        {
            public readonly UniTaskCompletionSource Gate = new();

            public override void Bind(ITooltipPayload payload) { }

            protected override async UniTask OnShowAsync(CancellationToken ct) =>
                await Gate.Task.AttachExternalCancellation(ct);
        }
    }
}
