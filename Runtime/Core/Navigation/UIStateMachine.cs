using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    public sealed class UIStateMachine : IUIStateMachine
    {
        private readonly Dictionary<Type, IViewState> _states = new();
        private IViewState _currentState;

        public IViewState CurrentState => _currentState;

        public void RegisterState<T>(T state) where T : IViewState
        {
            var type = typeof(T);
            if (_states.ContainsKey(type))
                throw new InvalidOperationException($"[UIStateMachine] State {type.Name} is already registered.");
            _states[type] = state;
        }

        public async UniTask ChangeStateAsync<T>(CancellationToken ct = default) where T : IViewState
        {
            var type = typeof(T);
            if (!_states.TryGetValue(type, out var next))
                throw new InvalidOperationException(
                    $"[UIStateMachine] State {type.Name} not registered. Call RegisterState<{type.Name}>() first.");

            if (_currentState != null)
                await _currentState.OnExitAsync(ct);

            _currentState = next;
            await _currentState.OnEnterAsync(ct);
        }
    }
}
