using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    public partial class UIFrameworkLifetimeScope
    {
        // IAudioService is ALWAYS registered: VContainer ignores C# default parameter values, so any
        // consumer taking it would fail the container build in a game without sound.
        //
        // The Exists guard (with includeInterfaceTypes, see Known Gotchas) lets a game substitute its
        // own service, e.g. FMOD-backed, by registering it BEFORE base.Configure. One registered AFTER
        // wins resolution by last-wins, but the built-in service is then still constructed as an
        // entry point and drives AudioListener.pause; same limit as the persistence guards.
        //
        // Must run after RegisterPersistence, which registers the AppLifecycleSignals component.
        // Static so a test can build a container around it without a scope.
        internal static void RegisterAudio(IContainerBuilder builder, UIFrameworkConfig config)
        {
            if (builder.Exists(typeof(IAudioService), includeInterfaceTypes: true)) return;

            if (config.Audio != null)
                builder.RegisterEntryPoint<AudioService>(Lifetime.Singleton).AsSelf();
            else
                builder.RegisterInstance<IAudioService>(new NullAudioService());
        }
    }
}
