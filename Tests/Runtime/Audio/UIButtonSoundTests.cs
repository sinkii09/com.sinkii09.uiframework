using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    public class UIButtonSoundTests
    {
        private static readonly SoundId Click = SoundId.FromPath("ui/click");
        private static readonly SoundId ToggleCue = SoundId.FromPath("ui/toggle");

        private GameObjectTracker _gos;
        private RecordingAudioService _audio;
        private TestView _view;

        [SetUp]
        public void SetUp()
        {
            _gos = new GameObjectTracker();
            _audio = new RecordingAudioService { Theme = RecordingAudioService.NewTheme() };
            _view = new GameObject("View", typeof(RectTransform)).AddComponent<TestView>();
            _gos.Track(_view);

            var builder = new ContainerBuilder();
            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            builder.RegisterInstance<IAudioService>(_audio);
            builder.Build().Inject(_view);
        }

        [TearDown]
        public void TearDown()
        {
            _gos.DestroyAll();
            if (_audio.Theme != null) Object.DestroyImmediate(_audio.Theme);
        }

        // Selectable first, as Unity's own menus create it, so EventSystem runs its handler first.
        private T Control<T>(UIButtonSoundMode mode = UIButtonSoundMode.Default, Transform parent = null) where T : Selectable
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : _view.transform, false);
            var control = go.AddComponent<T>();
            var sound = go.AddComponent<UIButtonSound>();
            typeof(UIButtonSound).GetField("_mode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(sound, mode);
            return control;
        }

        private static void PressPointer(Component target) =>
            ExecuteEvents.Execute(target.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);

        [Test]
        public void ButtonClick_Default_PlaysThemeButtonClick()
        {
            PressPointer(Control<Button>());

            CollectionAssert.AreEqual(new[] { Click }, _audio.Played);
        }

        [Test]
        public void ButtonClick_HandlerDisablesButton_StillPlays()
        {
            var button = Control<Button>();
            button.onClick.AddListener(() => button.interactable = false);

            PressPointer(button);

            CollectionAssert.AreEqual(new[] { Click }, _audio.Played);
        }

        [Test]
        public void ButtonClick_NotInteractable_IsSilent()
        {
            var button = Control<Button>();
            button.interactable = false;

            PressPointer(button);

            Assert.That(_audio.Played, Is.Empty);
        }

        [Test]
        public void ButtonClick_Silent_IsSilent()
        {
            PressPointer(Control<Button>(UIButtonSoundMode.Silent));

            Assert.That(_audio.Played, Is.Empty);
        }

        [Test]
        public void ToggleClick_PlaysThemeToggle_ButCodeSetIsOnIsSilent()
        {
            var toggle = Control<Toggle>();

            toggle.isOn = !toggle.isOn;
            Assert.That(_audio.Played, Is.Empty, "A binding setting isOn is not a press.");

            PressPointer(toggle);
            CollectionAssert.AreEqual(new[] { ToggleCue }, _audio.Played);
        }

        [Test]
        public void ToggleClick_BindingRevertsValueSameFrame_PlaysOnce()
        {
            var toggle = Control<Toggle>();
            toggle.onValueChanged.AddListener(on => { if (on) toggle.isOn = false; });

            PressPointer(toggle);

            CollectionAssert.AreEqual(new[] { ToggleCue }, _audio.Played);
        }

        [Test]
        public void Click_NoParentView_LogsErrorAndIsSilent()
        {
            var orphanRoot = new GameObject("Orphan", typeof(RectTransform));
            _gos.Track(orphanRoot);
            var button = Control<Button>(parent: orphanRoot.transform);

            LogAssert.Expect(LogType.Error, new Regex("no injected parent view"));
            PressPointer(button);

            Assert.That(_audio.Played, Is.Empty);
        }

        [Test]
        public void AddedWithoutSelectable_LogsError()
        {
            var go = new GameObject("NoSelectable", typeof(RectTransform));
            _gos.Track(go);

            LogAssert.Expect(LogType.Error, new Regex("needs a Button, Toggle or other Selectable"));
            go.AddComponent<UIButtonSound>();
        }
    }
}
