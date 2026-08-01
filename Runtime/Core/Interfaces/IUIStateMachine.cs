using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIStateMachine
    {
        IViewState CurrentState { get; }

        // Throws InvalidOperationException if the same type is registered twice
        void RegisterState<T>(T state) where T : IViewState;
        // Throws InvalidOperationException if T was never registered via RegisterState
        UniTask ChangeStateAsync<T>(CancellationToken ct = default) where T : IViewState;
        /// <summary>
        /// Clears the current state pointer.
        /// As of v1.2.0, UINavigator.ChangeStateAsync no longer calls this automatically — doing so
        /// skipped the previous state's OnExitAsync, silently dropping non-view cleanup. Call this
        /// manually only when bypassing the normal transition path entirely (e.g. direct ShowAsync
        /// navigation) — the caller is responsible for any cleanup ResetState() skips.
        /// </summary>
        void ResetState();
    }
}
