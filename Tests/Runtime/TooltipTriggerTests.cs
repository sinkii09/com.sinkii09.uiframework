using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // Covers the input-source gating that has no business living in the service: which pointer
    // ids count as hover, and the release paths a recycled cell depends on.
    public class TooltipTriggerTests
    {
        private GameObjectTracker _tracker;
        private RecordingTooltipService _service;
        private TooltipTrigger _trigger;

        [SetUp]
        public void SetUp()
        {
            _tracker = new GameObjectTracker();
            _service = new RecordingTooltipService();

            var go = _tracker.Track(new GameObject(
                "Widget", typeof(RectTransform), typeof(CanvasGroup)));
            _trigger = go.AddComponent<TooltipTrigger>();
            _trigger.Construct(_service);

            // Inline content, so the trigger has a payload without needing an ITooltipSource.
            SetPrivate(_trigger, "_title", "Hello");
        }

        [TearDown]
        public void TearDown() => _tracker.DestroyAll();

        private static void SetPrivate(object target, string field, object value)
            => target.GetType()
                .GetField(field, System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);

        private static PointerEventData Pointer(int pointerId)
            => new(EventSystem.current) { pointerId = pointerId, position = Vector2.zero };

        // Mouse pointers use negative ids; touches use 0 and up.
        private const int MouseId = -1;
        private const int TouchId = 0;

        [Test]
        public void OnPointerEnter_FromAMouse_RaisesAHoverTooltip()
        {
            _trigger.OnPointerEnter(Pointer(MouseId));

            Assert.AreEqual(1, _service.ShowCount);
            Assert.AreEqual(TooltipSource.Hover, _service.LastSource);
        }

        [Test]
        public void OnPointerEnter_FromATouch_IsIgnored()
        {
            // Unity raises OnPointerEnter on touch PRESS as well, so an ungated hover path would
            // double-trigger on mobile — once here and again from the long-press timer.
            _trigger.OnPointerEnter(Pointer(TouchId));

            Assert.AreEqual(0, _service.ShowCount);
        }

        [Test]
        public void OnPointerExit_FromATouch_DoesNotReleaseTheHoverTooltip()
        {
            _trigger.OnPointerEnter(Pointer(MouseId));
            _trigger.OnPointerExit(Pointer(TouchId));

            Assert.AreEqual(0, _service.HideCount);
        }

        [Test]
        public void OnDisable_ReleasesTheTooltipItOwns()
        {
            _trigger.OnPointerEnter(Pointer(MouseId));

            // UIControlBase's Awake/OnDestroy are private and non-virtual with no disable hook, so
            // a recycled cell would otherwise strand the tooltip it raised.
            _trigger.gameObject.SetActive(false);

            Assert.AreEqual(1, _service.HideCount);
        }

        [Test]
        public void NotifyContentChanged_WhileOwningTheTooltip_ReleasesIt()
        {
            _trigger.OnPointerEnter(Pointer(MouseId));

            // A pooled cell rebound in place is never deactivated and never moves, so the
            // service's watchdog cannot see the change — the widget must report it.
            _trigger.NotifyContentChanged();

            Assert.AreEqual(1, _service.HideCount);
        }

        [Test]
        public void NotifyContentChanged_WhenNotOwningTheTooltip_DoesNothing()
        {
            _trigger.NotifyContentChanged();

            Assert.AreEqual(0, _service.HideCount);
        }

        [Test]
        public void OnSelect_OnTheArmingFrame_IsIgnored()
        {
            // EventSystem auto-selects firstSelectedGameObject on its first frame, which would
            // otherwise pop a tooltip at boot with no user input at all.
            _trigger.OnSelect(new BaseEventData(EventSystem.current));

            Assert.AreEqual(0, _service.ShowCount);
        }

        [UnityTest]
        public IEnumerator OnSelect_AfterTheArmingFrame_RaisesAFocusTooltip() => UniTask.ToCoroutine(async () =>
        {
            await UniTask.NextFrame();
            await UniTask.NextFrame();

            _trigger.OnSelect(new BaseEventData(EventSystem.current));

            Assert.AreEqual(1, _service.ShowCount);
            Assert.AreEqual(TooltipSource.Focus, _service.LastSource);
        });

        private sealed class RecordingTooltipService : ITooltipService
        {
            public int ShowCount;
            public int HideCount;
            public TooltipSource LastSource;

            public bool IsShown { get; private set; }
            public RectTransform CurrentAnchor { get; private set; }
            public float LongPressSeconds => 0.5f;
            public float LongPressMoveCancelPixels => 10f;

            public void Show(in TooltipRequest request)
            {
                ShowCount++;
                LastSource = request.Source;
                CurrentAnchor = request.Anchor;
                IsShown = true;
            }

            public void Hide(RectTransform source)
            {
                HideCount++;
                IsShown = false;
                CurrentAnchor = null;
            }

            public void HideImmediate() => Hide(null);

            public void Register(TooltipViewBase view) { }
        }
    }
}
