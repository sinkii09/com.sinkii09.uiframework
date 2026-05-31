using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIStateMachine
    {
        IViewState CurrentState { get; }

        // Registering the same type twice silently overwrites the previous entry
        void RegisterState<T>(T state) where T : IViewState;
        // Throws if T was never registered via RegisterState
        UniTask ChangeStateAsync<T>(CancellationToken ct = default) where T : IViewState;
    }
}
