using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    // Game developer stub. Subclass or replace to implement your gameplay state.
    //
    // Typical back-button → pause wiring pattern:
    //   [Inject] private GameLifecycleManager _lifecycle;
    //   [Inject] private BackButtonRouter _backButtonRouter;
    //   private IDisposable _backSub;
    //
    //   public override async UniTask OnEnterAsync(CancellationToken ct = default)
    //   {
    //       _backSub = _backButtonRouter.Register(new MyHandler(
    //           priority: 10,
    //           async ct => await _lifecycle.ChangeStateAsync<PauseState>(ct)));
    //       await base.OnEnterAsync(ct);
    //   }
    //
    //   public override async UniTask OnExitAsync(CancellationToken ct = default)
    //   {
    //       _backSub?.Dispose();
    //       await base.OnExitAsync(ct);
    //   }
    //
    // IMPORTANT: PauseState must use try/finally to restore Time.timeScale = 1f on exit.
    //
    // Register before StartAsync runs — in your game bootstrap IInitializable.Initialize():
    //   _lifecycle.RegisterState(container.Resolve<GameplayState>());
    public class GameplayState : IGameState
    {
        public virtual string SceneName => "Gameplay";
        public virtual bool PausesGameTime => false;

        public virtual UniTask OnEnterAsync(CancellationToken ct = default) => UniTask.CompletedTask;
        public virtual UniTask OnExitAsync(CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
