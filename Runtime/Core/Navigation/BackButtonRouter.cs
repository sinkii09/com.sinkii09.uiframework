using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Dispatches back/Escape to the highest-priority registered handler.
    // Requires IBackButtonSource registered in your LifetimeScope.
    // Register with VContainer: builder.RegisterEntryPoint<BackButtonRouter>()
    public sealed class BackButtonRouter : IInitializable, IDisposable
    {
        private readonly IBackButtonSource _source;
        private readonly List<IBackButtonHandler> _handlers = new();
        private IDisposable _subscription;
        private bool _isHandling;

        public BackButtonRouter(IBackButtonSource source)
        {
            _source = source;
        }

        public void Initialize()
        {
            if (_source == null)
            {
                Debug.LogWarning("[UIFramework] BackButtonRouter: no IBackButtonSource registered — back button disabled.");
                return;
            }
            _subscription = _source.OnBack.Subscribe(_ => OnBackPressed());
        }

        public void Register(IBackButtonHandler handler) => _handlers.Add(handler);

        public void Unregister(IBackButtonHandler handler) => _handlers.Remove(handler);

        public void Dispose()
        {
            _subscription?.Dispose();
            _handlers.Clear();
        }

        private void OnBackPressed()
        {
            if (_isHandling) return;

            // Snapshot before iteration to guard against concurrent Register/Unregister
            var snapshot = new List<IBackButtonHandler>(_handlers);

            IBackButtonHandler winner = null;
            int highestPriority = int.MinValue;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Priority > highestPriority)
                {
                    highestPriority = snapshot[i].Priority;
                    winner = snapshot[i];
                }
            }

            if (winner == null) return;

            _isHandling = true;
            winner.HandleBackAsync(Application.exitCancellationToken)
                .ContinueWith(() => _isHandling = false)
                .Forget(ex => { _isHandling = false; Debug.LogException(ex); });
        }
    }
}
