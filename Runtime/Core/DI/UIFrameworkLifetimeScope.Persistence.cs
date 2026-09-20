using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // The persistence half of the root scope's registrations: storage backend, save service, and
    // the two pieces autosave needs. Split out of Configure, which had grown past 190 lines in a
    // single method — long enough that the next registration would land wherever there was room
    // rather than where it belonged.
    //
    // Behaviour is unchanged. Configure calls this at exactly the point the block used to occupy,
    // so registration ORDER is identical — which matters, because VContainer resolves duplicate
    // registrations last-wins and several of the guards below depend on running when they do.
    public partial class UIFrameworkLifetimeScope
    {
        private void RegisterPersistence(IContainerBuilder builder, UIFrameworkConfig config)
        {
            // --- Persistence ---
            builder.Register<IStorageBackend, LocalFileStorageBackend>(Lifetime.Singleton);

            // Registered unconditionally because VContainer does not honour C# default parameter
            // values — JsonSaveService's registry parameter is a HARD dependency, so leaving it out
            // when no game has migrations would fail the container build outright.
            //
            // The Exists guard makes the override order-independent. A game supplies its own by
            // registering it in Configure, which normally runs AFTER base.Configure and wins by
            // VContainer's last-wins rule; but a game that registers BEFORE calling base would
            // otherwise have its registry overwritten by this empty one, and the symptom is brutal —
            // every migrated save reads as "newer than supported" and the consumer disables saving
            // for the whole session.
            // includeInterfaceTypes matters: ContainerBuilder.Exists(type) alone compares only
            // ImplementationType (ContainerBuilder.cs:118-120), so a game registering
            // Register<TInterface, TImpl>() would NOT be detected and this default would be written
            // over the top of it — last-wins would then hand the service the framework's instance and
            // the game's registration would silently do nothing.
            if (!builder.Exists(typeof(SaveMigrationRegistry), includeInterfaceTypes: true))
                builder.RegisterInstance(SaveMigrationRegistry.Empty);

            // Same shape, same reason: a constructor parameter is a hard dependency, so leaving this
            // out would fail the container build for every game that does not use slots. Singleton
            // ONLY — JsonSaveService is a root singleton and UIViewFactory gives each view its own
            // child container, so a Scoped registration would fork the slot per view and a slot switch
            // would never reach the service. That cannot be detected at runtime; see ISaveSlotContext.
            // AsSelf as well as the interface: ISaveSlotContext is read-only by design, so a game
            // that wants to CHANGE slots resolves the concrete SaveSlotContext and sets ActiveSlot.
            // Without this the only way to switch slots would be to replace the registration.
            if (!builder.Exists(typeof(ISaveSlotContext), includeInterfaceTypes: true))
                builder.Register<ISaveSlotContext, SaveSlotContext>(Lifetime.Singleton).AsSelf();

            // AsSelf so a game can reach OnSaveRecoveredAsObservable, which lives on the concrete type
            // rather than on ISaveService — adding a member to that interface would break every
            // implementer and force a major version. One registration, one instance, two ways to ask
            // for it.
            // ISaveRecoveryEvents as well, so SaveRecoveryNotifier follows a game that substitutes
            // its own save service instead of listening to an instance nobody uses.
            builder.Register<ISaveService, JsonSaveService>(Lifetime.Singleton)
                   .AsSelf()
                   .As<ISaveRecoveryEvents>();

            // --- Application lifecycle + autosave ---
            // The component is ADDED here rather than authored into a scene. This scope already owns
            // the one DontDestroyOnLoad GameObject, and a project that forgot to place the component
            // would lose saves with nothing logged — the exact failure autosave exists to prevent.
            // It is a separate sealed component and not methods on this class because Unity
            // dispatches OnApplicationPause by name against the most-derived type, so a game
            // subclassing this scope and declaring its own would hide ours; see AppLifecycleSignals.
            builder.RegisterComponent(gameObject.GetOrAddComponent<AppLifecycleSignals>());

            // 0 disables autosave outright, matching ViewCacheGraceSeconds' convention. Nothing is
            // registered in that case, so resolving IAutoSaveScheduler fails loudly rather than
            // handing back an object that silently never writes.
            if (config.AutoSaveDebounceSeconds > 0f)
                builder.RegisterEntryPoint<AutoSaveScheduler>(Lifetime.Singleton).AsSelf();

            // --- Save recovery ---
            // Opt-in prompt. Same Exists guard as the registry and slot context above, and for the
            // same reason: a game that registers its own BEFORE calling base.Configure would
            // otherwise have it overwritten by this null object, and the symptom would be an
            // unreadable save silently propagating instead of asking.
            if (!builder.Exists(typeof(ISaveRecoveryPrompt), includeInterfaceTypes: true))
                builder.Register<ISaveRecoveryPrompt, NullSaveRecoveryPrompt>(Lifetime.Singleton);

            builder.Register<SaveRecoveryCoordinator>(Lifetime.Singleton);

            // The toast for a recovery that SUCCEEDED, which until now fired into nothing. An entry
            // point because it must be subscribed before the first load, and Start runs ahead of
            // any game state.
            builder.RegisterEntryPoint<SaveRecoveryNotifier>(Lifetime.Singleton);
        }
    }
}
