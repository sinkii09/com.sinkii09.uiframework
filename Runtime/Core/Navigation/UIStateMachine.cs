using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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

            if (next == _currentState)
            {
                Debug.LogWarning($"[UIStateMachine] Already in state {type.Name} — transition skipped.");
                return;
            }

            // _currentState is only promoted after OnEnterAsync succeeds.
            // On failure (throw or cancellation), rollback to previous so the next
            // ChangeStateAsync call does not exit a state that never fully entered.
            // When called from UINavigator.ChangeStateAsync, the navigation stack has
            // already been cleared before reaching here. OnExitAsync implementations that
            // call CloseAllAsync will hit the _isTransitioning guard in UINavigator and
            // silently return — that is intentional, not a bug. If this ordering ever
            // changes, same-state re-entry (e.g. MemoryGameState → MemoryGameState) will
            // attempt a double-clear while _isTransitioning is true.
            var previous = _currentState;
            try
            {
                if (previous != null) await previous.OnExitAsync(ct);
                await next.OnEnterAsync(ct);
                _currentState = next;
            }
            catch
            {
                _currentState = previous;
                throw;
            }
        }

        public void ResetState() => _currentState = null;
    }
}
