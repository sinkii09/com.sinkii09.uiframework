using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework.Tests
{
    // DI registration of IAudioService, and the page-visible signal it depends on.
    public class AudioRegistrationAndVisibleSignalTests
    {
        private AudioPlayModeFixture _fixture;

        [SetUp]
        public void SetUp() => _fixture = new AudioPlayModeFixture();

        [TearDown]
        public void TearDown() => _fixture.Destroy();

        // Calls the PRODUCTION registration method, like AssetLoaderRegistrationTests.
        private IObjectResolver Build(Action<IContainerBuilder> before = null)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(_fixture.Config);
            builder.RegisterInstance<IAssetLoader>(_fixture.Loader);
            builder.RegisterComponent(_fixture.Signals);
            before?.Invoke(builder);
            UIFrameworkLifetimeScope.RegisterAudio(builder, _fixture.Config);
            return builder.Build();
        }

        [Test]
        public void WithAudioConfig_ResolvesTheBuiltInServiceThroughTheContainer()
        {
            using var container = Build();
            Assert.IsInstanceOf<AudioService>(container.Resolve<IAudioService>());
            Assert.AreSame(container.Resolve<IAudioService>(), container.Resolve<AudioService>());
        }

        [Test]
        public void WithoutAudioConfig_ResolvesNullService()
        {
            _fixture.Config.Audio = null;
            using var container = Build();
            Assert.IsInstanceOf<NullAudioService>(container.Resolve<IAudioService>());
        }

        [Test]
        public void GameRegisteredFirst_IsKept()
        {
            using var container = Build(b => b.Register<IAudioService, NullAudioService>(Lifetime.Singleton));
            Assert.IsInstanceOf<NullAudioService>(container.Resolve<IAudioService>(),
                "Register<TInterface, TImpl> must be seen by the guard (includeInterfaceTypes).");
        }

        [Test]
        public void NullService_IsSilentAndReadyAtOnce()
        {
            var audio = new NullAudioService();
            Assert.IsFalse(audio.Play(SoundId.FromPath("x")).IsValid);
            Assert.IsTrue(audio.ReadyAsync().Status == Cysharp.Threading.Tasks.UniTaskStatus.Succeeded);
        }

        [Test]
        public void RaiseVisible_PublishesOnVisible()
        {
            int visible = 0;
            using var _ = _fixture.Signals.OnVisibleAsObservable.Subscribe(__ => visible++);
            _fixture.Signals.RaiseVisible();
            Assert.AreEqual(1, visible);
        }

        [Test]
        public void Bridge_ThrowingVisibleHandler_IsLoggedNotRethrown()
        {
            Action hidden = () => { };
            WebGlBrowserBridge.Register(hidden, () => throw new InvalidOperationException("boom"));
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[Lifecycle\] Page-visible handler threw"));
                Assert.DoesNotThrow(WebGlBrowserBridge.HandleVisible);
            }
            finally
            {
                WebGlBrowserBridge.Unregister(hidden);
            }
        }

        [Test]
        public void Bridge_OwnerUnregister_ClearsVisibleToo()
        {
            int calls = 0;
            Action hidden = () => { };
            WebGlBrowserBridge.Register(hidden, () => calls++);
            WebGlBrowserBridge.Unregister(hidden);
            WebGlBrowserBridge.HandleVisible();
            Assert.AreEqual(0, calls);
        }
    }
}
