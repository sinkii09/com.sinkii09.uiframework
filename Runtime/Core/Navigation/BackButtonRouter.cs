using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Dispatches Android back-button (Escape) to the highest-priority registered handler.
    // Register with VContainer via builder.RegisterEntryPoint<BackButtonRouter>().
    public sealed class BackButtonRouter : IInitializable, IDisposable
    {
        private readonly List<IBackButtonHandler> _handlers = new();
        private IDisposable _subscription;

        public void Initialize()
        {
            // R3.Unity: Subscribe returns IDisposable; stored and disposed in Dispose()
            _subscription = Observable.EveryUpdate()
                .Where(_ => Input.GetKeyDown(KeyCode.Escape))
                .Subscribe(_ => OnBackPressed());
        }

        public void Register(IBackButtonHandler handler) => _handlers.Add(handler);

        public void Unregister(IBackButtonHandler handler) => _handlers.Remove(handler);

        public void Dispose() => _subscription?.Dispose();

        private void OnBackPressed()
        {
            // Linear scan avoids LINQ allocation on a per-frame hot path
            IBackButtonHandler winner = null;
            int highestPriority = int.MinValue;
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (_handlers[i].Priority > highestPriority)
                {
                    highestPriority = _handlers[i].Priority;
                    winner = _handlers[i];
                }
            }
            winner?.HandleBackAsync(Application.exitCancellationToken)
                .Forget(ex => Debug.LogException(ex));
        }
    }
}
