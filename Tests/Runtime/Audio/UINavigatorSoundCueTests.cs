using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    internal sealed class PopupTestView : UIView<TestViewModel>
    {
        public override UILayer Layer => UILayer.Popup;
        protected override void BindViewModel(TestViewModel vm) { }
    }

    public class UINavigatorSoundCueTests
    {
        private static readonly SoundId Show = SoundId.FromPath("ui/popup-show");
        private static readonly SoundId Hide = SoundId.FromPath("ui/popup-hide");

        private GameObjectTracker _gos;
        private RecordingAudioService _audio;
        private UIStateMachine _states;
        private UINavigator _navigator;

        // Shows a popup from inside OnEnterAsync, the way a state opens a welcome or offline popup.
        private sealed class PopupOnEnterState : IViewState
        {
            public UINavigator Navigator;
            public async UniTask OnEnterAsync(CancellationToken ct = default) => await Navigator.ShowAsync<PopupTestView>(ct);
            public UniTask OnExitAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        }

        [SetUp]
        public void SetUp()
        {
            _gos = new GameObjectTracker();
            _audio = new RecordingAudioService { Theme = RecordingAudioService.NewTheme() };
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<PopupTestView>(nameof(PopupTestView));
            loader.RegisterPrefab<TestView>(nameof(TestView));

            var builder = new ContainerBuilder();
            builder.Register<IUIAnimator, DOTweenUIAnimator>(Lifetime.Singleton);
            builder.RegisterInstance<IAudioService>(_audio);
            var factory = new UIViewFactory(loader, builder.Build(), null);

            _states = new UIStateMachine();
            _navigator = new UINavigator(new NavigationStack(null), _states, factory,
                new[] { Reg<PopupTestView>(), Reg<TestView>() }, null, audio: _audio);
        }

        [TearDown]
        public void TearDown()
        {
            _gos.DestroyAll();
            if (_audio.Theme != null) Object.DestroyImmediate(_audio.Theme);
        }

        private static UIViewRegistration Reg<TView>() =>
            new(typeof(TView), typeof(TestViewModel), typeof(TView).Name);

        [UnityTest]
        public IEnumerator ShowThenPop_Popup_PlaysThemeShowThenHide() => UniTask.ToCoroutine(async () =>
        {
            await _navigator.ShowAsync<PopupTestView>();
            await _navigator.PopAsync();

            CollectionAssert.AreEqual(new[] { Show, Hide }, _audio.Played);
        });

        [UnityTest]
        public IEnumerator HideAsync_Popup_PlaysHideCue() => UniTask.ToCoroutine(async () =>
        {
            await _navigator.ShowAsync<PopupTestView>();
            await _navigator.HideAsync<PopupTestView>();

            CollectionAssert.AreEqual(new[] { Show, Hide }, _audio.Played);
        });

        [UnityTest]
        public IEnumerator ShowAndPop_ScreenLayerDefault_IsSilent() => UniTask.ToCoroutine(async () =>
        {
            await _navigator.ShowAsync<TestView>();
            await _navigator.PopAsync();

            Assert.That(_audio.Played, Is.Empty);
        });

        [UnityTest]
        public IEnumerator CloseAll_AfterPopup_PlaysNoHideCue() => UniTask.ToCoroutine(async () =>
        {
            await _navigator.ShowAsync<PopupTestView>();
            await _navigator.CloseAllAsync();

            CollectionAssert.AreEqual(new[] { Show }, _audio.Played);
        });

        [UnityTest]
        public IEnumerator ChangeState_PopupShownByOnEnter_IsSilent() => UniTask.ToCoroutine(async () =>
        {
            _states.RegisterState(new PopupOnEnterState { Navigator = _navigator });

            await _navigator.ChangeStateAsync<PopupOnEnterState>();

            Assert.IsInstanceOf<PopupTestView>(_navigator.Current, "The popup must have been shown.");
            Assert.That(_audio.Played, Is.Empty);
        });

        [UnityTest]
        public IEnumerator Show_NoTheme_IsSilent() => UniTask.ToCoroutine(async () =>
        {
            Object.DestroyImmediate(_audio.Theme);
            _audio.Theme = null;

            await _navigator.ShowAsync<PopupTestView>();

            Assert.That(_audio.Played, Is.Empty);
        });
    }
}
