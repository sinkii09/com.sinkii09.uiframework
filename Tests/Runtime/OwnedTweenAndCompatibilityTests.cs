using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // UIViewBase.PlayOwnedAsync (the helper for custom choreography) and the tooltip/Slide authoring
    // guard. This is the first test file that names DOTween types — the asmdef references DOTween.dll
    // because PlayOwnedAsync takes a Tween and cannot be exercised without one.
    public class OwnedTweenAndCompatibilityTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        private OwnedTweenView CreateView()
        {
            var view = new GameObject("OwnedTweenView", typeof(RectTransform)).AddComponent<OwnedTweenView>();
            _gos.Track(view);
            return view;
        }

        [UnityTest]
        public IEnumerator PlayOwned_AlreadyKilledTween_ReturnsInsteadOfHanging() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView();
            var tween = view.transform.DOScale(2f, 1f);
            tween.Kill(); // a dead tween never fires OnComplete/OnKill again

            var finished = await UniTask.WhenAny(view.PlayOwned(tween), UniTask.Delay(500, DelayType.Realtime));

            Assert.AreEqual(0, finished, "awaiting a dead tween must return, not hang");
        });

        [UnityTest]
        public IEnumerator PlayOwned_TokenCancelled_ThrowsAndKillsTween() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView();
            var tween = view.transform.DOScale(2f, 5f);
            using var cts = new CancellationTokenSource();

            var play = view.PlayOwned(tween, cts.Token);
            cts.Cancel();

            var error = await CaptureAsync(async () => await play);
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsFalse(tween.IsActive());
        });

        [UnityTest]
        public IEnumerator PlayOwned_DOKillOnRootTransform_DoesNotCancel() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView();
            var tween = view.transform.DOScale(1.2f, 0.1f);

            var play = view.PlayOwned(tween);
            view.transform.DOKill(); // what a juice component on the same transform does

            var error = await CaptureAsync(async () => await play);
            Assert.IsNull(error, "an owned tween is targeted at the view, not its transform");
        });

        [Test]
        public void TooltipWithSlideTransition_WarnsOnAwake()
        {
            var go = new GameObject("SlideTooltip", typeof(RectTransform));
            _gos.Track(go);
            go.SetActive(false); // defer Awake until the serialized field is set, like a prefab
            var tooltip = go.AddComponent<SlideTooltipView>();
            var slide = ScriptableObject.CreateInstance<SlideTransition>();
            typeof(UIViewBase)
                .GetField("_showTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(tooltip, slide);

            LogAssert.Expect(LogType.Warning, new Regex("uses a SlideTransition"));
            go.SetActive(true);

            UnityEngine.Object.DestroyImmediate(slide);
        }

        [UnityTest]
        public IEnumerator PlayOwned_CompletedTweenWithoutAutoKill_Returns() => UniTask.ToCoroutine(async () =>
        {
            var view = CreateView();
            var tween = view.transform.DOScale(2f, 1f).SetAutoKill(false);
            tween.Complete(); // finished but still "active" — OnComplete will not fire again

            var finished = await UniTask.WhenAny(view.PlayOwned(tween), UniTask.Delay(500, DelayType.Realtime));

            Assert.AreEqual(0, finished, "a finished tween must return, not hang");
            tween.Kill();
        });

        private sealed class OwnedTweenView : UIView<TestViewModel>
        {
            protected override void BindViewModel(TestViewModel vm) { }

            public UniTask PlayOwned(Tween tween, CancellationToken ct = default) => PlayOwnedAsync(tween, ct);
        }

        private sealed class SlideTooltipView : TooltipViewBase
        {
            public override void Bind(ITooltipPayload payload) { }
        }
    }
}
