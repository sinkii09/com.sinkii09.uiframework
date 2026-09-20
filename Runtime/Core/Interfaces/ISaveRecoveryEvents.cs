using R3;

namespace Sinkii09.UIFramework
{
    // Reports that a save was rebuilt from its backup.
    //
    // A separate interface rather than a member on ISaveService, for the reason spelled out on
    // JsonSaveService.OnSaveRecoveredAsObservable: adding to that interface breaks every implementer
    // and forces a major version.
    //
    // It exists as an interface at all — rather than SaveRecoveryNotifier simply taking the concrete
    // JsonSaveService — because of what happens when a game substitutes its own ISaveService. The
    // scope also registers JsonSaveService AsSelf, so the concrete type still resolves; the notifier
    // would then be listening to a second save service that nothing in the game ever writes through,
    // and the toast would never fire with nothing logged to explain it. Registering the capability
    // alongside ISaveService means a substituted service that implements this one is followed, and
    // one that does not is a container error rather than a silence.
    public interface ISaveRecoveryEvents
    {
        Observable<SaveRecoveredEventArgs> OnSaveRecoveredAsObservable { get; }
    }
}
