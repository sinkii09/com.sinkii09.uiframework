using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Framework-provided boot state. Override OnEnterAsync to add splash or service
    // warm-up logic. Enter your first game state (e.g. MainMenuState) with
    // _lifecycle.EnqueueStateChange<T>() from the bootstrap's Initialize: the queue waits until
    // boot has run. A direct ChangeStateAsync or navigator ShowAsync there races boot, and either
    // transition can be refused.
    public class BootState : IGameState
    {
        public virtual string SceneName => null;
        public virtual bool PausesGameTime => false;

        public virtual UniTask OnEnterAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        public virtual UniTask OnExitAsync(CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
