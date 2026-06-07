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
        /// <summary>Clears the current state pointer. Only call after deliberately bypassing ChangeStateAsync.</summary>
        void ResetState();
    }
}
