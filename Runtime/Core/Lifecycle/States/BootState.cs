using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Framework-provided boot state. Override OnEnterAsync to add splash or service
    // warm-up logic. After boot completes, call _lifecycle.ChangeStateAsync<T>(ct)
    // to enter your first game state (e.g. MainMenuState).
    public class BootState : IGameState
    {
        public virtual string SceneName => null;
        public virtual bool PausesGameTime => false;

        public virtual UniTask OnEnterAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        public virtual UniTask OnExitAsync(CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
